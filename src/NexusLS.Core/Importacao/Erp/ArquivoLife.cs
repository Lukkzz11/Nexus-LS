using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Services;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>
/// Exportação do ERP web "Life" (<c>nexus-life-export</c>, versão 1): um JSON com as coleções do Firestore já em JSON comum.
/// <code>{ "formato":"nexus-life-export", "versao":1, "empresaId":"...", "geradoEm":"ISO", "colecoes": { "produtos":[{"id":"...", ...}], ... } }</code>
/// Cada documento traz o <c>id</c> e os mesmos campos do Firestore (datas em texto ISO).
/// </summary>
public sealed class ArquivoLife
{
    public const string Formato = "nexus-life-export";
    public const int VersaoSuportada = 1;
    public const long TamanhoMaximoBytes = 300L * 1024 * 1024;

    /// <summary>Coleções do arquivo que o Nexus sabe importar, e o nome delas dentro do importador.</summary>
    internal static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["produtos"] = "produtos",
        ["clientes"] = "clientes",
        ["caixas_fechamentos"] = "caixa_turnos",
        ["caixa_turnos"] = "caixa_turnos",
        ["vendas"] = "vendas",
        ["contas_receber"] = "contas_receber",
        ["pedidos"] = "pedidos",
        ["transacoes_financeiras"] = "transacoes_financeiras",
        ["materia_prima"] = "materia_prima",
        ["ordens_producao"] = "ordens_producao",
        ["perdas_producao"] = "perdas_producao",
    };

    /// <summary>Campos que nunca entram (credenciais): são descartados em qualquer nível do documento.</summary>
    private static readonly HashSet<string> CamposSecretos = new(StringComparer.OrdinalIgnoreCase)
    {
        "pin", "pinHash", "pinSalt", "senha", "senhaHash", "senhaSalt", "password", "passwordHash", "salt", "token", "accessToken", "refreshToken", "apiKey"
    };

    public string? EmpresaId { get; private init; }
    public DateTime? GeradoEm { get; private init; }
    public int Versao { get; private init; }

    /// <summary>Documentos por coleção do importador (já com o nome do Nexus).</summary>
    public Dictionary<string, List<DocumentoErp>> Colecoes { get; } = new();

    /// <summary>Coleções que vieram no arquivo mas o Nexus não importa (nome, quantidade).</summary>
    public List<(string Colecao, int Quantidade)> NaoImportadas { get; } = new();

    public List<string> Avisos { get; } = new();

    public int TotalDocumentos => Colecoes.Values.Sum(l => l.Count);

    public static ArquivoLife LerArquivo(string caminho)
    {
        FileInfo info;
        try { info = new FileInfo(caminho); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new RegraDeNegocioException("O caminho do arquivo é inválido."); }
        if (!info.Exists) throw new RegraDeNegocioException("O arquivo não foi encontrado.");
        if (info.Length > TamanhoMaximoBytes) throw new RegraDeNegocioException("O arquivo é grande demais para importar (limite de 300 MB).");
        try { return Interpretar(File.ReadAllText(caminho)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new RegraDeNegocioException("Não foi possível abrir o arquivo: " + ex.Message); }
    }

    public static ArquivoLife Interpretar(string json)
    {
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json); }
        catch (JsonException) { throw new RegraDeNegocioException("O arquivo não é um JSON válido. Use o arquivo gerado pelo botão de exportar do Life."); }
        if (raiz is not JsonObject o)
            throw new RegraDeNegocioException("O arquivo não tem o formato de uma exportação do Life.");

        if (o["formato"] is not JsonValue f || !f.TryGetValue<string>(out var formato) || formato != Formato)
            throw new RegraDeNegocioException($"Este arquivo não é uma exportação do Life (esperado formato \"{Formato}\").");

        int versao;
        if (o["versao"] is JsonValue v && v.TryGetValue<int>(out var vi)) versao = vi;
        else throw new RegraDeNegocioException("O arquivo não informa a versão da exportação.");
        if (versao > VersaoSuportada)
            throw new RegraDeNegocioException($"Este arquivo é de uma versão mais nova da exportação (versão {versao}); este Nexus entende até a versão {VersaoSuportada}. Atualize o Nexus para importar.");
        if (versao < 1) throw new RegraDeNegocioException("A versão da exportação informada no arquivo é inválida.");

        if (o["colecoes"] is not JsonObject colecoes)
            throw new RegraDeNegocioException("O arquivo não traz as coleções de dados (campo \"colecoes\").");

        var arquivo = new ArquivoLife
        {
            Versao = versao,
            EmpresaId = o["empresaId"] is JsonValue e && e.TryGetValue<string>(out var emp) && !string.IsNullOrWhiteSpace(emp) ? emp.Trim() : null,
            GeradoEm = o["geradoEm"] is JsonValue g && g.TryGetValue<string>(out var gs)
                       && DateTimeOffset.TryParse(gs, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var gd) ? gd.UtcDateTime : null
        };

        foreach (var (nome, valor) in colecoes)
        {
            if (valor is null) continue;
            if (valor is not JsonArray lista)
                throw new RegraDeNegocioException($"A coleção \"{nome}\" do arquivo deveria ser uma lista de documentos.");
            if (!Aliases.TryGetValue(nome, out var destino))
            {
                if (lista.Count > 0) arquivo.NaoImportadas.Add((nome, lista.Count));
                continue;
            }

            var docs = arquivo.Colecoes.TryGetValue(destino, out var existente) ? existente : arquivo.Colecoes[destino] = new();
            var indice = 0;
            foreach (var item in lista)
            {
                indice++;
                if (item is not JsonObject doc) { arquivo.Avisos.Add($"{nome} #{indice}: item que não é um documento; ignorado."); continue; }
                var id = doc["id"] is JsonValue iv && iv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)
                    ? s.Trim() : doc["id"] is JsonValue nv && nv.TryGetValue<long>(out var n) ? n.ToString(CultureInfo.InvariantCulture) : null;
                if (id is null) { arquivo.Avisos.Add($"{nome} #{indice}: documento sem \"id\"; ignorado."); continue; }
                if (id.Contains('/')) { arquivo.Avisos.Add($"{nome}/{id}: id inválido; ignorado."); continue; }

                var campos = (JsonObject)Limpar(doc)!;
                campos.Remove("id");
                docs.RemoveAll(d => d.Id == id); // repetido no arquivo: vale o último
                docs.Add(new DocumentoErp(id, campos));
            }
        }
        return arquivo;
    }

    /// <summary>Copia o nó sem os campos secretos e com Timestamps do Firestore ({seconds, nanoseconds}) como texto ISO.</summary>
    private static JsonNode? Limpar(JsonNode? no)
    {
        switch (no)
        {
            case JsonObject o:
                if (TimestampComoTexto(o) is { } iso) return JsonValue.Create(iso);
                var copia = new JsonObject();
                foreach (var (k, v) in o)
                    if (!CamposSecretos.Contains(k)) copia[k] = Limpar(v);
                return copia;
            case JsonArray a:
                var lista = new JsonArray();
                foreach (var x in a) lista.Add(Limpar(x));
                return lista;
            case JsonValue v:
                return v.DeepClone();
            default:
                return null;
        }
    }

    private static string? TimestampComoTexto(JsonObject o)
    {
        if (o.Count > 3) return null;
        var segundos = o["seconds"] ?? o["_seconds"];
        if (segundos is not JsonValue sv || !sv.TryGetValue<long>(out var s)) return null;
        var nanos = o["nanoseconds"] ?? o["_nanoseconds"];
        long n = nanos is JsonValue nv && nv.TryGetValue<long>(out var nn) ? nn : 0;
        return DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime.AddTicks(n / 100).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }
}

/// <summary>Fonte do importador que entrega o conteúdo do arquivo do Life, sem rede e sem filtro de data.</summary>
public sealed class FonteArquivoLife : IFonteErp
{
    private readonly ArquivoLife _arquivo;
    public FonteArquivoLife(ArquivoLife arquivo) => _arquivo = arquivo;

    public Task<IReadOnlyList<DocumentoErp>> LerAsync(string colecao, string campoData, DateTime? aPartirDe, int limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DocumentoErp>>(_arquivo.Colecoes.TryGetValue(colecao, out var l) ? l : new List<DocumentoErp>());
}

/// <summary>Prévia (simulação em uma cópia do banco) e importação real de um arquivo do Life, com o mesmo <see cref="ImportadorErp"/>.</summary>
public static class ImportacaoArquivoLife
{
    public static Task<ResultadoImportacaoErp> ImportarAsync(ArquivoLife arquivo, IDbContextFactory<PdvDbContext> fabrica,
        CancellationToken ct = default, IProgress<string>? progresso = null, Func<DateTime>? agora = null) =>
        new ImportadorErp(fabrica, new FonteArquivoLife(arquivo), agora, usarCursores: false).ImportarAsync(ct, progresso);

    /// <summary>
    /// Roda a importação inteira numa cópia do banco em memória e devolve os números (novos, atualizados, sem mudança,
    /// ignorados) sem tocar no banco de verdade e sem a fila de sincronização.
    /// </summary>
    public static async Task<ResultadoImportacaoErp> PreverAsync(ArquivoLife arquivo, IDbContextFactory<PdvDbContext> fabrica,
        CancellationToken ct = default, IProgress<string>? progresso = null, Func<DateTime>? agora = null)
    {
        await using var origem = await fabrica.CreateDbContextAsync(ct);
        if (origem.Database.GetDbConnection() is not SqliteConnection real)
            throw new RegraDeNegocioException("Não foi possível preparar a prévia neste banco de dados.");
        var abriu = real.State != System.Data.ConnectionState.Open;
        if (abriu) await real.OpenAsync(ct);
        using var copia = new SqliteConnection("DataSource=:memory:");
        copia.Open();
        try { real.BackupDatabase(copia); }
        finally { if (abriu) await real.CloseAsync(); }

        var opcoes = new DbContextOptionsBuilder<PdvDbContext>().UseSqlite(copia).Options;
        return await ImportarAsync(arquivo, new FabricaContexto(opcoes), ct, progresso, agora);
    }
}
