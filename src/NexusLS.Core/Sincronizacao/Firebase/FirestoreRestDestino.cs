using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>
/// Monta os documentos do Firestore no formato da API REST. Cada produto vira o documento
/// <c>produtos/{guid}</c> (o id é o Guid, então reenviar nunca duplica). Só os campos do catálogo vão para a nuvem.
/// </summary>
public static class FirestoreJson
{
    public const string Origem = "nexus-ls";

    /// <summary>
    /// Os campos que o Nexus LS é dono. A "máscara" de atualização lista exatamente estes: o que existir no documento
    /// além deles (foto, descrição, destaque da vitrine online) NÃO é tocado, e um campo opcional que ficou vazio
    /// no ERP é removido do documento, como deve ser.
    /// </summary>
    public static readonly string[] CamposDoErp =
    {
        "nome", "categoria", "referencia", "codigoBarras", "precoVendaCentavos", "semControleDeEstoque",
        "ativo", "estoqueTotal", "tamanhos", "hashErp", "sincronizadoEm", "origem"
    };

    /// <summary>Id do documento: o Guid em minúsculas, para sempre o mesmo texto.</summary>
    public static string IdDoDocumento(Guid guid) => guid.ToString("D").ToLowerInvariant();

    public static string NomeDoDocumento(string projectId, string colecao, string id) =>
        $"projects/{projectId}/databases/(default)/documents/{colecao}/{id}";

    public static JsonObject Produto(string projectId, OperacaoCatalogo op, DateTime agora)
    {
        var p = op.Item.Produto;
        var campos = new JsonObject
        {
            ["nome"] = Texto(p.Nome),
            ["precoVendaCentavos"] = Inteiro(p.PrecoVendaCentavos),
            ["semControleDeEstoque"] = Booleano(p.SemControleDeEstoque),
            ["ativo"] = Booleano(p.Ativo),
            ["estoqueTotal"] = Inteiro(p.EstoqueTotal),
            ["tamanhos"] = Lista(p.Tamanhos.Select(t => Mapa(new JsonObject
            {
                ["tamanho"] = Texto(t.Tamanho),
                ["estoque"] = Inteiro(t.Estoque)
            }))),
            ["hashErp"] = Texto(op.Item.Hash),
            ["sincronizadoEm"] = Momento(agora),
            ["origem"] = Texto(Origem)
        };
        if (p.Categoria is not null) campos["categoria"] = Texto(p.Categoria);
        if (p.Referencia is not null) campos["referencia"] = Texto(p.Referencia);
        if (p.CodigoBarras is not null) campos["codigoBarras"] = Texto(p.CodigoBarras);

        // A foto só entra na máscara quando o Nexus tem uma: sem foto aqui, a que foi posta pela vitrine online fica como está.
        var mascara = CamposDoErp.AsEnumerable();
        if (p.FotoUrl is not null)
        {
            campos["fotoUrl"] = Texto(p.FotoUrl);
            mascara = mascara.Append("fotoUrl");
        }

        return Escrita(NomeDoDocumento(projectId, "produtos", IdDoDocumento(p.Guid)), campos, mascara);
    }

    /// <summary>O documento <c>meta/catalogo</c>: a vitrine lê dele "atualizado em ...".</summary>
    public static JsonObject MetaCatalogo(string projectId, DateTime agora, int totalProdutosAtivos)
    {
        var campos = new JsonObject
        {
            ["atualizadoEm"] = Momento(agora),
            ["totalProdutosAtivos"] = Inteiro(totalProdutosAtivos),
            ["origem"] = Texto(Origem)
        };
        return Escrita(NomeDoDocumento(projectId, "meta", "catalogo"), campos, new[] { "atualizadoEm", "totalProdutosAtivos", "origem" });
    }

    public static JsonObject Commit(IEnumerable<JsonObject> escritas) => new() { ["writes"] = new JsonArray(escritas.Cast<JsonNode>().ToArray()) };

    private static JsonObject Escrita(string nome, JsonObject campos, IEnumerable<string> mascara) => new()
    {
        ["update"] = new JsonObject { ["name"] = nome, ["fields"] = campos },
        ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(mascara.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()) }
    };

    internal static JsonObject Texto(string valor) => new() { ["stringValue"] = valor };
    internal static JsonObject Inteiro(long valor) => new() { ["integerValue"] = valor.ToString(CultureInfo.InvariantCulture) }; // a API pede o inteiro como texto
    internal static JsonObject Booleano(bool valor) => new() { ["booleanValue"] = valor };
    internal static JsonObject Mapa(JsonObject campos) => new() { ["mapValue"] = new JsonObject { ["fields"] = campos } };
    internal static JsonObject Lista(IEnumerable<JsonObject> itens) =>
        new() { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(itens.Cast<JsonNode>().ToArray()) } };
    internal static JsonObject Momento(DateTime local) =>
        new() { ["timestampValue"] = local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture) };
}

/// <summary>
/// Publica o catálogo no Firestore pela API REST, com o token do login da licença (nunca chave de administrador).
/// Traduz cada tipo de erro para o que o sincronizador precisa saber: parar tudo (rede, login, permissão, limite)
/// ou recusar só o conteúdo (dado inválido).
/// </summary>
public sealed class FirestoreRestDestino : IDestinoCatalogo
{
    private readonly HttpClient _http;
    private readonly string _projectId;
    private readonly IProvedorToken _token;
    private readonly Func<DateTime> _agora;

    private string Raiz => $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";

    public FirestoreRestDestino(HttpClient http, string projectId, IProvedorToken token, Func<DateTime>? agora = null)
    {
        _http = http;
        _projectId = projectId;
        _token = token;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task VerificarAsync(CancellationToken ct = default)
    {
        // Lê o documento meta/catalogo: 200 e 404 (ainda não existe) significam "login e permissão ok".
        var (status, corpo) = await ChamarAsync(HttpMethod.Get, $"{Raiz}/meta/catalogo", null, ct);
        if (status is HttpStatusCode.OK or HttpStatusCode.NotFound && !EhBancoInexistente(status, corpo)) return;
        throw TraduzirErro(status, corpo);
    }

    public async Task EnviarLoteAsync(IReadOnlyList<OperacaoCatalogo> lote, CancellationToken ct = default)
    {
        if (lote.Count == 0) return;
        var agora = _agora();
        var corpo = FirestoreJson.Commit(lote.Select(op => FirestoreJson.Produto(_projectId, op, agora)));
        await Commit(corpo, ct);
    }

    public async Task RegistrarAtualizacaoAsync(DateTime agora, int totalProdutosAtivos, CancellationToken ct = default) =>
        await Commit(FirestoreJson.Commit(new[] { FirestoreJson.MetaCatalogo(_projectId, agora, totalProdutosAtivos) }), ct);

    private async Task Commit(JsonObject corpo, CancellationToken ct)
    {
        var (status, resposta) = await ChamarAsync(HttpMethod.Post, $"{Raiz}:commit", corpo.ToJsonString(), ct);
        if (status != HttpStatusCode.OK) throw TraduzirErro(status, resposta);
    }

    private async Task<(HttpStatusCode Status, string Corpo)> ChamarAsync(HttpMethod metodo, string url, string? json, CancellationToken ct)
    {
        // Se o token foi recusado (venceu), faz UM login novo e tenta de novo.
        for (var tentativa = 1; ; tentativa++)
        {
            var token = await _token.ObterAsync(ct);
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
                throw new FalhaDestinoException("Sem conexão com a internet (não foi possível falar com o Firestore).", GravidadeFalha.Interrompe, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new FalhaDestinoException("O Firestore demorou demais para responder. Tente de novo.", GravidadeFalha.Interrompe, ex);
            }
        }
    }

    private static bool EhBancoInexistente(HttpStatusCode status, string corpo) =>
        status == HttpStatusCode.NotFound && corpo.Contains("database", StringComparison.OrdinalIgnoreCase)
        && corpo.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    /// <summary>Traduz a resposta de erro do Firestore. A mensagem do servidor só entra quando o erro é de dado, nunca com credencial.</summary>
    internal static FalhaDestinoException TraduzirErro(HttpStatusCode status, string corpo)
    {
        var detalhe = "";
        try { detalhe = JsonNode.Parse(corpo)?["error"]?["message"]?.GetValue<string>() ?? ""; }
        catch (JsonException) { }

        return status switch
        {
            HttpStatusCode.BadRequest => new FalhaDestinoException(
                string.IsNullOrWhiteSpace(detalhe) ? "o Firestore recusou o conteúdo" : "o Firestore recusou: " + detalhe,
                GravidadeFalha.DadosRecusados),
            HttpStatusCode.Unauthorized => new FalhaDestinoException("O login do Firebase foi recusado. Confira o e-mail e a senha na tela Catálogo online."),
            HttpStatusCode.Forbidden => new FalhaDestinoException(
                "Sem permissão para gravar no Firestore. Confira se as regras de segurança do projeto liberam este login (veja firebase/firestore.rules)."),
            HttpStatusCode.NotFound when EhBancoInexistente(status, corpo) => new FalhaDestinoException(
                "O banco Firestore ainda não foi criado neste projeto. Crie-o no console do Firebase (modo produção)."),
            HttpStatusCode.NotFound => new FalhaDestinoException("O projeto Firebase não foi encontrado. Confira o id do projeto."),
            HttpStatusCode.TooManyRequests => new FalhaDestinoException("O Firestore recusou por excesso de uso (limite do plano). Tente mais tarde."),
            HttpStatusCode.Conflict => new FalhaDestinoException("O Firestore estava ocupado (conflito). Tente de novo."),
            _ when (int)status >= 500 => new FalhaDestinoException("O Firestore está com problema no momento. Tente mais tarde."),
            _ => new FalhaDestinoException($"O Firestore respondeu com um erro inesperado (código {(int)status}).")
        };
    }
}
