using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Sincronizacao.Nuvem;

public enum TipoFalhaNuvem { SemConexao, SemPermissao, Login, Dados, Outra }

/// <summary>Falha ao falar com a nuvem das lojas. A mensagem já vem em português e nunca tem senha nem token.</summary>
public sealed class FalhaNuvemException : Exception
{
    public TipoFalhaNuvem Tipo { get; }
    public FalhaNuvemException(string mensagem, TipoFalhaNuvem tipo, Exception? causa = null) : base(mensagem, causa) => Tipo = tipo;

    public bool SemConexao => Tipo == TipoFalhaNuvem.SemConexao;

    /// <summary>Converte a falha do login/Firestore já existente (FalhaDestinoException) para a da nuvem das lojas.</summary>
    public static FalhaNuvemException De(FalhaDestinoException ex) => new(ex.Message,
        ex.InnerException is HttpRequestException or TaskCanceledException ? TipoFalhaNuvem.SemConexao : TipoFalhaNuvem.Login, ex);
}

/// <summary>Um documento a gravar: caminho completo (dentro de empresas/{e}/lojas ou empresas/{e}/precos) e os campos no formato REST.</summary>
public sealed record DocumentoNuvem(string Caminho, JsonObject Campos);

/// <summary>Um documento lido: id (último trecho do caminho) e os campos.</summary>
public sealed record DocumentoLido(string Caminho, string Id, JsonObject Campos);

/// <summary>
/// Fala com o Firestore (API REST) só nos caminhos de loja: <c>empresas/{e}/lojas/**</c> e <c>empresas/{e}/precos/**</c>. Reaproveita o login
/// (<see cref="IProvedorToken"/>) e a trava do projeto do ERP Life (<see cref="GuardaErp"/>). Nunca grava fora desses caminhos nem em
/// coleções do ERP. Gravar um documento SEM máscara o substitui por inteiro, então reenviar o mesmo dia é idempotente.
/// </summary>
public sealed class ClienteNuvemLoja
{
    /// <summary>Máximo de documentos por pedido de gravação (o Firestore aceita 500; ficamos bem abaixo).</summary>
    public const int LimiteLoteEscrita = 100;
    /// <summary>Máximo de documentos por pedido de leitura em lote.</summary>
    public const int LimiteLoteLeitura = 100;
    private const int MaximoPaginas = 50;

    private readonly HttpClient _http;
    private readonly string _projectId;
    private readonly string _empresaId;
    private readonly IProvedorToken _token;

    private string Raiz => $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";
    private string Nome(string caminho) => $"projects/{_projectId}/databases/(default)/documents/{caminho}";

    public string EmpresaId => _empresaId;

    public ClienteNuvemLoja(HttpClient http, string projectId, string empresaId, IProvedorToken token)
    {
        if (GuardaErp.EhProjetoDoErp(projectId)) throw new FalhaNuvemException(GuardaErp.MensagemSomenteLeitura, TipoFalhaNuvem.Dados);
        _http = http;
        _projectId = projectId.Trim();
        _empresaId = CaminhosNuvem.ValidarId(empresaId, "empresa");
        _token = token;
    }

    /// <summary>Só caminhos de loja ou de preço desta empresa podem ser gravados.</summary>
    private void ExigirCaminhoDeLoja(string caminho)
    {
        var p = caminho.Split('/');
        var ok = p.Length >= 4 && p[0] == "empresas" && p[1] == _empresaId
                 && ((p[2] == "lojas" && p.Length is 4 or 6) || (p[2] == "precos" && p.Length == 6 && p[4] == "lojas"));
        if (!ok) throw new FalhaNuvemException("Caminho recusado: o Nexus só grava em lojas e preços por loja.", TipoFalhaNuvem.Dados);
    }

    // ---------------------------------------------------------------- escrita
    /// <summary>Grava (substitui) os documentos, em pedidos de até <see cref="LimiteLoteEscrita"/>. Cada pedido é tudo-ou-nada.</summary>
    public async Task<int> GravarAsync(IReadOnlyList<DocumentoNuvem> documentos, Action<int, int>? progresso = null, CancellationToken ct = default)
    {
        foreach (var d in documentos) ExigirCaminhoDeLoja(d.Caminho);
        var feitos = 0;
        foreach (var lote in documentos.Chunk(LimiteLoteEscrita))
        {
            ct.ThrowIfCancellationRequested();
            var escritas = lote.Select(d => (JsonNode)new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Nome(d.Caminho), ["fields"] = d.Campos.DeepClone() }
            }).ToArray();
            var corpo = new JsonObject { ["writes"] = new JsonArray(escritas) }.ToJsonString();
            var (status, resposta) = await ChamarAsync(HttpMethod.Post, $"{Raiz}:commit", corpo, ct);
            if (status != HttpStatusCode.OK) throw Traduzir(status, resposta);
            feitos += lote.Length;
            progresso?.Invoke(feitos, documentos.Count);
        }
        return feitos;
    }

    // ---------------------------------------------------------------- leitura
    /// <summary>Lê um documento. Nulo se não existe.</summary>
    public async Task<DocumentoLido?> LerAsync(string caminho, CancellationToken ct = default)
    {
        var (status, resposta) = await ChamarAsync(HttpMethod.Get, $"{Raiz}/{caminho}", null, ct);
        if (status == HttpStatusCode.NotFound && !BancoInexistente(resposta)) return null;
        if (status != HttpStatusCode.OK) throw Traduzir(status, resposta);
        return Converter(resposta);
    }

    /// <summary>Lista os documentos de uma coleção (com páginas). Só o administrador deve chamar para <c>lojas</c>.</summary>
    public async Task<List<DocumentoLido>> ListarAsync(string colecao, CancellationToken ct = default)
    {
        var lista = new List<DocumentoLido>();
        string? pagina = null;
        for (var i = 0; i < MaximoPaginas; i++)
        {
            var url = $"{Raiz}/{colecao}?pageSize=100" + (pagina is null ? "" : "&pageToken=" + Uri.EscapeDataString(pagina));
            var (status, resposta) = await ChamarAsync(HttpMethod.Get, url, null, ct);
            if (status == HttpStatusCode.NotFound && !BancoInexistente(resposta)) return lista;
            if (status != HttpStatusCode.OK) throw Traduzir(status, resposta);
            var no = Analisar(resposta);
            if (no?["documents"] is JsonArray docs)
                foreach (var d in docs) if (d is JsonObject o) lista.Add(Converter(o));
            pagina = no?["nextPageToken"]?.GetValue<string>();
            if (string.IsNullOrEmpty(pagina)) break;
        }
        return lista;
    }

    /// <summary>Os resumos de dia de uma loja entre duas datas (inclusive). O id do dia é AAAA-MM-DD, que ordena como texto.</summary>
    public async Task<List<DocumentoLido>> ConsultarDiasAsync(string lojaId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var pai = CaminhosNuvem.Loja(_empresaId, lojaId);
        JsonObject Filtro(string op, string valor) => new()
        {
            ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "dia" }, ["op"] = op, ["value"] = SerializadorPacoteLoja.Texto(valor) }
        };
        var corpo = new JsonObject
        {
            ["structuredQuery"] = new JsonObject
            {
                ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "dias" }),
                ["where"] = new JsonObject
                {
                    ["compositeFilter"] = new JsonObject
                    {
                        ["op"] = "AND",
                        ["filters"] = new JsonArray(Filtro("GREATER_THAN_OR_EQUAL", de.ToString("yyyy-MM-dd")), Filtro("LESS_THAN_OR_EQUAL", ate.ToString("yyyy-MM-dd")))
                    }
                },
                ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "dia" }, ["direction"] = "ASCENDING" }),
                ["limit"] = 800
            }
        }.ToJsonString();
        var (status, resposta) = await ChamarAsync(HttpMethod.Post, $"{Raiz}/{pai}:runQuery", corpo, ct);
        if (status != HttpStatusCode.OK) throw Traduzir(status, resposta);
        var lista = new List<DocumentoLido>();
        if (Analisar(resposta) is JsonArray itens)
            foreach (var i in itens)
                if (i?["document"] is JsonObject d) lista.Add(Converter(d));
        return lista;
    }

    /// <summary>Lê vários documentos de uma vez (por caminho completo), em pedidos de até <see cref="LimiteLoteLeitura"/>. Os que não existem não voltam.</summary>
    public async Task<List<DocumentoLido>> LerVariosAsync(IEnumerable<string> caminhos, CancellationToken ct = default)
    {
        var lista = new List<DocumentoLido>();
        foreach (var lote in caminhos.Chunk(LimiteLoteLeitura))
        {
            ct.ThrowIfCancellationRequested();
            var corpo = new JsonObject { ["documents"] = new JsonArray(lote.Select(c => (JsonNode)JsonValue.Create(Nome(c))!).ToArray()) }.ToJsonString();
            var (status, resposta) = await ChamarAsync(HttpMethod.Post, $"{Raiz}:batchGet", corpo, ct);
            if (status != HttpStatusCode.OK) throw Traduzir(status, resposta);
            if (Analisar(resposta) is JsonArray itens)
                foreach (var i in itens)
                    if (i?["found"] is JsonObject d) lista.Add(Converter(d));
        }
        return lista;
    }

    // ---------------------------------------------------------------- base
    private static JsonNode? Analisar(string texto)
    {
        try { return JsonNode.Parse(texto); }
        catch (JsonException ex) { throw new FalhaNuvemException("A resposta da nuvem veio num formato inesperado.", TipoFalhaNuvem.Outra, ex); }
    }

    private static DocumentoLido Converter(string texto) => Converter(Analisar(texto) as JsonObject ?? new JsonObject());

    private static DocumentoLido Converter(JsonObject doc)
    {
        var nome = doc["name"]?.GetValue<string>() ?? "";
        var depois = nome.IndexOf("/documents/", StringComparison.Ordinal);
        var caminho = depois >= 0 ? nome[(depois + "/documents/".Length)..] : nome;
        return new DocumentoLido(caminho, caminho[(caminho.LastIndexOf('/') + 1)..], doc["fields"] as JsonObject ?? new JsonObject());
    }

    private static bool BancoInexistente(string corpo) =>
        corpo.Contains("database", StringComparison.OrdinalIgnoreCase) && corpo.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    private async Task<(HttpStatusCode Status, string Corpo)> ChamarAsync(HttpMethod metodo, string url, string? json, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            string token;
            try { token = await _token.ObterAsync(ct); }
            catch (FalhaDestinoException ex) { throw FalhaNuvemException.De(ex); }
            try
            {
                using var pedido = new HttpRequestMessage(metodo, url);
                pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (json is not null) pedido.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resposta = await _http.SendAsync(pedido, ct);
                var texto = await resposta.Content.ReadAsStringAsync(ct);
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 1)
                {
                    _token.Invalidar();
                    continue;
                }
                return (resposta.StatusCode, texto);
            }
            catch (HttpRequestException ex)
            {
                throw new FalhaNuvemException("Sem conexão com a internet. Nada foi alterado; tente de novo quando a conexão voltar.", TipoFalhaNuvem.SemConexao, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new FalhaNuvemException("A nuvem demorou demais para responder. Nada foi alterado; tente de novo.", TipoFalhaNuvem.SemConexao, ex);
            }
        }
    }

    internal static FalhaNuvemException Traduzir(HttpStatusCode status, string corpo)
    {
        var detalhe = "";
        try { detalhe = JsonNode.Parse(corpo) is JsonNode n ? (n as JsonArray)?[0]?["error"]?["message"]?.GetValue<string>() ?? n["error"]?["message"]?.GetValue<string>() ?? "" : ""; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return status switch
        {
            HttpStatusCode.BadRequest => new FalhaNuvemException(
                string.IsNullOrWhiteSpace(detalhe) ? "A nuvem recusou o conteúdo enviado." : "A nuvem recusou: " + detalhe, TipoFalhaNuvem.Dados),
            HttpStatusCode.Unauthorized => new FalhaNuvemException("O login da nuvem foi recusado. Confira o e-mail e a senha na tela Catálogo online.", TipoFalhaNuvem.Login),
            HttpStatusCode.Forbidden => new FalhaNuvemException(
                "Sem permissão na nuvem para esta loja. Confira com o administrador se as regras do Firestore por loja foram publicadas e se este computador tem a conta da loja.",
                TipoFalhaNuvem.SemPermissao),
            HttpStatusCode.NotFound when BancoInexistente(corpo) => new FalhaNuvemException("O banco Firestore ainda não foi criado neste projeto.", TipoFalhaNuvem.Outra),
            HttpStatusCode.NotFound => new FalhaNuvemException("O projeto Firebase não foi encontrado. Confira o id do projeto.", TipoFalhaNuvem.Outra),
            HttpStatusCode.TooManyRequests => new FalhaNuvemException("A nuvem recusou por excesso de uso (limite do plano). Tente mais tarde.", TipoFalhaNuvem.Outra),
            HttpStatusCode.Conflict => new FalhaNuvemException("A nuvem estava ocupada (conflito). Tente de novo.", TipoFalhaNuvem.Outra),
            _ when (int)status >= 500 => new FalhaNuvemException("A nuvem está com problema no momento. Tente mais tarde.", TipoFalhaNuvem.Outra),
            _ => new FalhaNuvemException($"A nuvem respondeu com um erro inesperado (código {(int)status}).", TipoFalhaNuvem.Outra)
        };
    }
}
