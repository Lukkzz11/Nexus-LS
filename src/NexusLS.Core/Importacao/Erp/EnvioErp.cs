using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>O documento já existe no ERP (criação recusada de propósito: nunca sobrescreve uma venda).</summary>
public sealed class DocumentoJaExisteNoErpException : Exception
{
    public DocumentoJaExisteNoErpException(string caminho) : base($"O documento {caminho} já existe no ERP.") { }
}

/// <summary>
/// Grava no Firestore do ERP por REST, com o login do usuário (as regras do ERP valem normalmente). Todos os documentos
/// de uma chamada vão em UMA operação <c>commit</c>: ou entram todos, ou nenhum. Criação usa <c>exists=false</c> (nunca
/// sobrescreve); alteração usa <c>exists=true</c> e <c>updateMask</c> (só os campos da máscara). Só é chamada pelo
/// <see cref="GravadorErp"/>, que recusa tudo com a gravação desligada.
/// </summary>
public sealed class FirestoreErpDestino : IDestinoErp
{
    private readonly HttpClient _http;
    private readonly ConfiguracaoErp _config;
    private readonly IProvedorToken _token;

    public FirestoreErpDestino(HttpClient http, ConfiguracaoErp config, IProvedorToken token)
    {
        _http = http;
        _config = config;
        _token = token;
    }

    public async Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default)
    {
        if (documentos.Count == 0) return;
        var baseDoc = $"projects/{_config.ProjectId}/databases/(default)/documents";
        var escritas = new JsonArray();
        foreach (var d in documentos)
        {
            var nome = $"{baseDoc}/{d.Caminho}";
            if (d.Campos.Count > 0)
            {
                var campos = new JsonObject();
                foreach (var (k, v) in d.Campos) campos[k] = Valor(v);
                var mascara = d.Criar ? d.Campos.Keys.ToList() : d.Mascara.ToList();
                escritas.Add(new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = nome, ["fields"] = campos },
                    ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(mascara.Select(m => (JsonNode?)Caminho(m)).ToArray()) },
                    ["currentDocument"] = new JsonObject { ["exists"] = !d.Criar }
                });
            }
            if (d.Incrementos is { Count: > 0 })
            {
                var transformacoes = new JsonArray();
                foreach (var (campo, delta) in d.Incrementos)
                    transformacoes.Add(new JsonObject { ["fieldPath"] = Caminho(campo), ["increment"] = Numero(delta) });
                escritas.Add(new JsonObject
                {
                    ["transform"] = new JsonObject { ["document"] = nome, ["fieldTransforms"] = transformacoes },
                    ["currentDocument"] = new JsonObject { ["exists"] = true }
                });
            }
        }

        var url = $"https://firestore.googleapis.com/v1/{baseDoc}:commit";
        var corpo = new JsonObject { ["writes"] = escritas }.ToJsonString();
        for (var tentativa = 1; ; tentativa++)
        {
            var token = await _token.ObterAsync(ct);
            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
                pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resposta = await _http.SendAsync(pedido, ct);
                var texto = await resposta.Content.ReadAsStringAsync(ct);
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 1) { _token.Invalidar(); continue; }
                if (resposta.IsSuccessStatusCode) return;
                if (resposta.StatusCode == HttpStatusCode.Conflict || texto.Contains("ALREADY_EXISTS", StringComparison.Ordinal))
                    throw new DocumentoJaExisteNoErpException(documentos[0].Caminho);
                throw FirestoreRestDestino.TraduzirErro(resposta.StatusCode, texto);
            }
            catch (HttpRequestException ex)
            {
                throw new FalhaDestinoException("Sem conexão com a internet (não foi possível gravar no ERP).", GravidadeFalha.Interrompe, ex);
            }
        }
    }

    /// <summary>Nomes de campo fora do padrão (ex.: tamanho "36") vão entre crases no caminho do Firestore.</summary>
    internal static string Caminho(string campo) =>
        string.Join(".", campo.Split('.').Select(s => System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z_][A-Za-z0-9_]*$") ? s : $"`{s.Replace("`", "\\`")}`"));

    private static JsonNode Numero(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 9e15
            ? new JsonObject { ["integerValue"] = ((long)v).ToString(CultureInfo.InvariantCulture) }
            : new JsonObject { ["doubleValue"] = v };

    internal static JsonNode Valor(object? v) => v switch
    {
        null => new JsonObject { ["nullValue"] = null },
        string s => new JsonObject { ["stringValue"] = s },
        bool b => new JsonObject { ["booleanValue"] = b },
        int i => new JsonObject { ["integerValue"] = i.ToString(CultureInfo.InvariantCulture) },
        long l => new JsonObject { ["integerValue"] = l.ToString(CultureInfo.InvariantCulture) },
        double d => Numero(d),
        decimal m => Numero((double)m),
        DateTime t => new JsonObject { ["timestampValue"] = t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) },
        System.Collections.IDictionary dic => new JsonObject
        {
            ["mapValue"] = new JsonObject { ["fields"] = MapaCampos(dic) }
        },
        System.Collections.IEnumerable lista => new JsonObject
        {
            ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(lista.Cast<object?>().Select(Valor).ToArray()) }
        },
        _ => new JsonObject { ["stringValue"] = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" }
    };

    private static JsonObject MapaCampos(System.Collections.IDictionary dic)
    {
        var o = new JsonObject();
        foreach (System.Collections.DictionaryEntry e in dic) o[Convert.ToString(e.Key, CultureInfo.InvariantCulture)!] = Valor(e.Value);
        return o;
    }
}

/// <summary>O que um envio ao ERP fez.</summary>
public sealed record ResultadoEnvioErp(int Vendas, int Clientes, int Produtos, int JaExistiam, IReadOnlyList<string> Falhas)
{
    public string Resumo =>
        $"Enviado ao ERP: {Vendas} venda(s), {Clientes} cliente(s), {Produtos} produto(s)."
        + (JaExistiam > 0 ? $" {JaExistiam} já estavam lá." : "")
        + (Falhas.Count > 0 ? $" {Falhas.Count} falha(s): {Falhas[0]}" : "");
}

/// <summary>
/// Leva ao ERP, no formato dele, as vendas, os clientes e os produtos do Nexus. Regras:
/// vendas só são CRIADAS (nunca sobrescritas) e levam junto, na mesma operação, a baixa de estoque (incremento atômico,
/// sem ler nem regravar o saldo), o Kardex e, se a prazo, o título e o saldo do cliente; clientes e produtos que o ERP já
/// tem só têm os campos da máscara atualizados (nunca estoque, fotos, descrição nem cores do ERP).
/// O que já foi enviado fica anotado (<c>enviado_*</c> em ImportacaoErp), então repetir o envio não duplica nada.
/// </summary>
public sealed class EnviadorErp
{
    private const string EnvVendas = "enviado_vendas", EnvClientes = "enviado_clientes", EnvProdutos = "enviado_produtos";

    private static readonly HashSet<string> CamposDeCadastro =
        MontadorErp.CamposProduto.Concat(MontadorErp.CamposCliente).ToHashSet();

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly ConfiguracaoErp _config;
    private readonly GravadorErp _gravador;
    private readonly Func<DateTime> _agora;

    public EnviadorErp(IDbContextFactory<PdvDbContext> fabrica, ConfiguracaoErp config, IDestinoErp destino, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _config = config;
        _gravador = new GravadorErp(config, destino);
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    public async Task<ResultadoEnvioErp> EnviarAsync(CancellationToken ct = default)
    {
        if (!_config.GravacaoAtivada)
            throw new RegraDeNegocioException("A gravação no ERP está desligada. Ligue \"Permitir gravar no ERP\" na Conexão com a nuvem.");

        await using var db = await _fabrica.CreateDbContextAsync(ct);
        var empresa = _config.EmpresaId;
        var agora = _agora().ToUniversalTime();
        var falhas = new List<string>();
        int vendas = 0, clientes = 0, produtos = 0, ja = 0;

        var ligacoes = await db.ImportacaoErp.AsNoTracking()
            .Where(l => l.Colecao == "produtos" || l.Colecao == "clientes" || l.Colecao == "vendas")
            .ToListAsync(ct);
        var idErp = ligacoes.ToDictionary(l => (l.Colecao, l.EntidadeGuid), l => l.ErpId);
        var enviados = await db.ImportacaoErp.Where(l => l.Colecao == EnvVendas || l.Colecao == EnvClientes || l.Colecao == EnvProdutos).ToListAsync(ct);
        string IdDe(string colecao, Guid guid) => idErp.TryGetValue((colecao, guid), out var id) ? id : guid.ToString("D").ToLowerInvariant();
        bool ExisteNoErp(string colecao, Guid guid) =>
            idErp.ContainsKey((colecao, guid)) || enviados.Any(e => e.Colecao == "enviado_" + colecao && e.EntidadeGuid == guid);

        // ---- 1) Clientes ----
        foreach (var c in await db.Clientes.AsNoTracking().Where(c => c.Ativo).ToListAsync(ct))
        {
            var existe = ExisteNoErp("clientes", c.Guid);
            var doc = MontadorErp.Cliente(empresa, c, agora, IdDe("clientes", c.Guid), novo: !existe);
            if (await JaEnviadoIgualAsync(enviados, EnvClientes, c.Guid, doc)) continue;
            if (await TentarAsync(new[] { doc }, $"cliente {c.Nome}", falhas, ct))
            {
                Anotar(db, enviados, EnvClientes, c.Guid, IdDe("clientes", c.Guid), Hash(doc), agora);
                clientes++;
            }
        }

        // ---- 2) Produtos ----
        foreach (var p in await db.Produtos.AsNoTracking().Include(p => p.Tamanhos).ToListAsync(ct))
        {
            var existe = ExisteNoErp("produtos", p.Guid);
            if (!existe && !p.Ativo) continue; // não cria no ERP um produto já desativado
            var doc = MontadorErp.Produto(empresa, p, p.Tamanhos, agora, IdDe("produtos", p.Guid), novo: !existe, incluirEstoque: !existe);
            if (await JaEnviadoIgualAsync(enviados, EnvProdutos, p.Guid, doc)) continue;
            if (await TentarAsync(new[] { doc }, $"produto {p.Nome}", falhas, ct))
            {
                Anotar(db, enviados, EnvProdutos, p.Guid, IdDe("produtos", p.Guid), Hash(doc), agora);
                produtos++;
                idErp[("produtos", p.Guid)] = IdDe("produtos", p.Guid);
            }
        }
        await db.SaveChangesAsync(ct);

        // ---- 3) Vendas (só as do Nexus; as importadas do ERP já estão lá) ----
        var jaEnviadas = enviados.Where(e => e.Colecao == EnvVendas).Select(e => e.EntidadeGuid).ToHashSet();
        var doErp = idErp.Where(k => k.Key.Colecao == "vendas").Select(k => k.Key.EntidadeGuid).ToHashSet();
        var pendentes = await db.Vendas.AsNoTracking().Include(v => v.Itens).Include(v => v.Cliente)
            .Where(v => !v.Cancelada).OrderBy(v => v.DataHora).ToListAsync(ct);
        var produtosPorId = await db.Produtos.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        foreach (var v in pendentes.Where(v => !jaEnviadas.Contains(v.Guid) && !doErp.Contains(v.Guid)))
        {
            var docs = MontarVendaCompleta(empresa, v, produtosPorId, c => IdDe("clientes", c.Guid), g => IdDe("produtos", g), agora);
            try
            {
                await _gravador.GravarAsync(docs, ct);
                vendas++;
            }
            catch (DocumentoJaExisteNoErpException)
            {
                ja++; // a venda já está no ERP: nada é baixado de novo (o commit inteiro foi recusado)
            }
            catch (Exception ex) when (ex is FalhaDestinoException or RegraDeNegocioException)
            {
                falhas.Add($"venda {v.Id}: {ex.Message}");
                if (ex is FalhaDestinoException { Gravidade: GravidadeFalha.Interrompe }) break;
                continue;
            }
            Anotar(db, enviados, EnvVendas, v.Guid, v.Guid.ToString("D"), "enviada", agora);
            await db.SaveChangesAsync(ct); // anota venda a venda: se cair no meio, a próxima continua de onde parou
        }
        await db.SaveChangesAsync(ct);
        return new ResultadoEnvioErp(vendas, clientes, produtos, ja, falhas);
    }

    private async Task<bool> TentarAsync(IReadOnlyList<DocumentoGravacaoErp> docs, string quem, List<string> falhas, CancellationToken ct)
    {
        try { await _gravador.GravarAsync(docs, ct); return true; }
        catch (DocumentoJaExisteNoErpException) { return true; }
        catch (Exception ex) when (ex is FalhaDestinoException or RegraDeNegocioException)
        {
            falhas.Add($"{quem}: {ex.Message}");
            if (ex is FalhaDestinoException { Gravidade: GravidadeFalha.Interrompe }) throw;
            return false;
        }
    }

    private static Task<bool> JaEnviadoIgualAsync(List<ImportacaoErpItem> enviados, string colecao, Guid guid, DocumentoGravacaoErp doc)
    {
        var h = Hash(doc);
        return Task.FromResult(enviados.Any(e => e.Colecao == colecao && e.EntidadeGuid == guid && e.Hash == h));
    }

    private static void Anotar(PdvDbContext db, List<ImportacaoErpItem> enviados, string colecao, Guid guid, string erpId, string hash, DateTime agora)
    {
        var item = enviados.FirstOrDefault(e => e.Colecao == colecao && e.EntidadeGuid == guid);
        if (item is null)
        {
            item = new ImportacaoErpItem { Colecao = colecao, ErpId = erpId, EntidadeGuid = guid };
            db.ImportacaoErp.Add(item);
            enviados.Add(item);
        }
        item.Hash = hash;
        item.ImportadoEm = agora;
    }

    /// <summary>Retrato do documento sem o carimbo de hora (hora diferente não conta como mudança).</summary>
    private static string Hash(DocumentoGravacaoErp d)
    {
        // Só conta o que o Nexus mantém (cadastro): o estoque inicial de um documento novo e os padrões da criação não entram.
        var campos = d.Campos.Where(k => k.Key != "atualizadoEm" && k.Key != "gradeTamanhos" && k.Key != "estoqueTotal"
                                         && CamposDeCadastro.Contains(k.Key))
            .OrderBy(k => k.Key, StringComparer.Ordinal)
            .ToDictionary(k => k.Key, k => k.Value);
        var json = JsonSerializer.Serialize(campos);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(d.Caminho + "|" + json)));
    }

    /// <summary>A venda e tudo que o ERP faria junto (estoque, Kardex, título a prazo e saldo do cliente), numa lista só.</summary>
    internal static IReadOnlyList<DocumentoGravacaoErp> MontarVendaCompleta(string empresa, Venda v,
        IReadOnlyDictionary<int, Produto> produtos, Func<Cliente, string> idCliente, Func<Guid, string> idProduto, DateTime agoraUtc)
    {
        var docs = new List<DocumentoGravacaoErp>
        {
            MontadorErp.Venda(empresa, v, v.Itens.Select(i => (i, produtos.TryGetValue(i.ProdutoId, out var p) ? p : null)),
                v.Cliente is null ? null : idCliente(v.Cliente), g => idProduto(g), agoraUtc)
        };
        docs.AddRange(MontadorErp.EfeitosDaVenda(empresa, v, produtos, v.Cliente is null ? null : idCliente(v.Cliente), idProduto, agoraUtc));
        return docs;
    }
}
