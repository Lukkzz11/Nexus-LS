using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Sincronizacao.Operacional;

/// <summary>
/// Grava no Firestore do projeto operacional pela API REST, com o token do usuário logado (nunca chave de administrador).
/// Cada documento vai para <c>lojas/{lojaId}/{coleção}/{guid}</c>; o id é o Guid, então reenviar sobrescreve e nunca duplica.
/// </summary>
public sealed class FirestoreOperacionalDestino : IDestinoOperacional
{
    private readonly HttpClient _http;
    private readonly string _projectId;
    private readonly string _lojaId;
    private readonly IProvedorToken _token;

    private string Raiz => $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";

    public FirestoreOperacionalDestino(HttpClient http, string projectId, string lojaId, IProvedorToken token)
    {
        if (NexusLS.Core.Importacao.Erp.GuardaErp.EhProjetoDoErp(projectId)) throw new FalhaDestinoException(NexusLS.Core.Importacao.Erp.GuardaErp.MensagemSomenteLeitura);
        _http = http;
        _projectId = projectId;
        _lojaId = lojaId;
        _token = token;
    }

    public async Task VerificarAsync(CancellationToken ct = default)
    {
        // Lê o documento da loja: 200 e 404 (ainda não existe) significam "login e permissão ok".
        var (status, corpo) = await ChamarAsync(HttpMethod.Get, $"{Raiz}/lojas/{_lojaId}", null, ct);
        if (status is HttpStatusCode.OK || (status == HttpStatusCode.NotFound && !EhBancoInexistente(corpo))) return;
        throw FirestoreRestDestino.TraduzirErro(status, corpo);
    }

    public async Task EnviarLoteAsync(IReadOnlyList<DocumentoOperacional> lote, CancellationToken ct = default)
    {
        if (lote.Count == 0) return;
        var escritas = lote.Select(Escrita).ToList();
        var (status, resposta) = await ChamarAsync(HttpMethod.Post, $"{Raiz}:commit", FirestoreJson.Commit(escritas).ToJsonString(), ct);
        if (status != HttpStatusCode.OK) throw FirestoreRestDestino.TraduzirErro(status, resposta);
    }

    internal JsonObject Escrita(DocumentoOperacional doc)
    {
        var campos = new JsonObject();
        foreach (var (nome, valor) in doc.Campos)
            if (Valor(valor) is { } v) campos[nome] = v;
        return new JsonObject
        {
            ["update"] = new JsonObject
            {
                ["name"] = FirestoreJson.NomeDoDocumento(_projectId, $"lojas/{_lojaId}/{doc.Colecao}", FirestoreJson.IdDoDocumento(doc.Guid)),
                ["fields"] = campos
            },
            // A máscara lista TODOS os campos: um campo que ficou vazio no SQLite é removido do documento.
            ["updateMask"] = new JsonObject
            {
                ["fieldPaths"] = new JsonArray(doc.Campos.Keys.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray())
            }
        };
    }

    /// <summary>Converte um valor simples para o formato de valor do Firestore. Nulo vira "sem campo".</summary>
    internal static JsonObject? Valor(object? valor) => valor switch
    {
        null => null,
        string s => FirestoreJson.Texto(s),
        bool b => FirestoreJson.Booleano(b),
        int i => FirestoreJson.Inteiro(i),
        long l => FirestoreJson.Inteiro(l),
        Guid g => FirestoreJson.Texto(FirestoreJson.IdDoDocumento(g)),
        DateTime d => new JsonObject { ["timestampValue"] = d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture) },
        Enum e => FirestoreJson.Texto(e.ToString()),
        IDictionary<string, object?> mapa => FirestoreJson.Mapa(Campos(mapa)),
        IEnumerable lista => FirestoreJson.Lista(lista.Cast<object?>().Select(x => Valor(x) ?? new JsonObject { ["nullValue"] = null })),
        _ => throw new ArgumentException($"Tipo sem conversão para o Firestore: {valor.GetType().Name}")
    };

    private static JsonObject Campos(IDictionary<string, object?> mapa)
    {
        var o = new JsonObject();
        foreach (var (nome, valor) in mapa)
            if (Valor(valor) is { } v) o[nome] = v;
        return o;
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

    private static bool EhBancoInexistente(string corpo) =>
        corpo.Contains("database", StringComparison.OrdinalIgnoreCase) && corpo.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
}
