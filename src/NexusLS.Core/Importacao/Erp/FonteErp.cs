using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>Onde fica o ERP: projeto Firebase dele e a empresa (<c>empresas/{empresaId}/...</c>). Só leitura.</summary>
public class ConfiguracaoErp
{
    public string ProjectId { get; set; } = "";
    public string ApiKey { get; set; } = "";

    /// <summary>Empresa do ERP de onde vêm os dados (no ERP, <c>activeTenantId</c>).</summary>
    public string EmpresaId { get; set; } = "lifesurf-principal";

    public bool EstaCompleta =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(EmpresaId);

    public void Validar()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(ProjectId.Trim(), "^[a-z][a-z0-9\\-]{4,29}$"))
            throw new RegraDeNegocioException("O id do projeto Firebase do ERP deve ter de 5 a 30 letras minúsculas, números ou hífens.");
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new RegraDeNegocioException("Informe a chave da API (Web API Key) do projeto do ERP.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(EmpresaId.Trim(), "^[A-Za-z0-9][A-Za-z0-9_\\-]{1,59}$"))
            throw new RegraDeNegocioException("Informe a empresa do ERP (ex.: lifesurf-principal).");
    }
}

public class ConfiguracaoErpStore
{
    private readonly string _caminho;
    public ConfiguracaoErpStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "erp.json");

    public ConfiguracaoErp Carregar()
    {
        try { return File.Exists(_caminho) ? JsonSerializer.Deserialize<ConfiguracaoErp>(File.ReadAllText(_caminho)) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(); }
    }

    public void Salvar(ConfiguracaoErp configuracao)
    {
        configuracao.ProjectId = configuracao.ProjectId.Trim();
        configuracao.ApiKey = configuracao.ApiKey.Trim();
        configuracao.EmpresaId = configuracao.EmpresaId.Trim();
        configuracao.Validar();
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(configuracao, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}

/// <summary>Um documento do ERP já em JSON simples (datas viram texto ISO, números viram números).</summary>
public record DocumentoErp(string Id, JsonObject Campos);

/// <summary>De onde o importador lê. No programa é o Firestore do ERP; nos testes, uma fonte falsa.</summary>
public interface IFonteErp
{
    /// <summary>
    /// Lê documentos de <c>empresas/{empresa}/{colecao}</c>. Com <paramref name="aPartirDe"/>, só os que têm
    /// <paramref name="campoData"/> igual ou depois dele, em ordem crescente (cada documento lido é uma leitura cobrada).
    /// Sem ele, lê a coleção toda (só na primeira importação).
    /// </summary>
    Task<IReadOnlyList<DocumentoErp>> LerAsync(string colecao, string campoData, DateTime? aPartirDe, int limite, CancellationToken ct = default);
}

/// <summary>Lê o Firestore do ERP pela API REST (<c>runQuery</c>) com o token do usuário. Nunca grava.</summary>
public sealed class FirestoreErpFonte : IFonteErp
{
    private readonly HttpClient _http;
    private readonly ConfiguracaoErp _config;
    private readonly IProvedorToken _token;

    public FirestoreErpFonte(HttpClient http, ConfiguracaoErp config, IProvedorToken token)
    {
        _http = http;
        _config = config;
        _token = token;
    }

    public async Task<IReadOnlyList<DocumentoErp>> LerAsync(string colecao, string campoData, DateTime? aPartirDe, int limite, CancellationToken ct = default)
    {
        var consulta = new JsonObject { ["from"] = new JsonArray(new JsonObject { ["collectionId"] = colecao }) };
        if (aPartirDe is { } desde)
        {
            consulta["where"] = new JsonObject
            {
                ["fieldFilter"] = new JsonObject
                {
                    ["field"] = new JsonObject { ["fieldPath"] = campoData },
                    ["op"] = "GREATER_THAN_OR_EQUAL",
                    ["value"] = new JsonObject { ["timestampValue"] = desde.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture) }
                }
            };
            consulta["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = campoData }, ["direction"] = "ASCENDING" });
            consulta["limit"] = limite;
        }
        var corpo = new JsonObject { ["structuredQuery"] = consulta }.ToJsonString();
        var url = $"https://firestore.googleapis.com/v1/projects/{_config.ProjectId}/databases/(default)/documents/empresas/{_config.EmpresaId}:runQuery";

        var (status, texto) = await PostAsync(url, corpo, ct);
        if (status != HttpStatusCode.OK) throw FirestoreRestDestino.TraduzirErro(status, texto);

        var lista = new List<DocumentoErp>();
        foreach (var item in JsonNode.Parse(texto)!.AsArray())
        {
            if (item?["document"] is not JsonObject doc) continue; // resposta vazia traz só "readTime"
            var nome = doc["name"]!.GetValue<string>();
            lista.Add(new DocumentoErp(nome[(nome.LastIndexOf('/') + 1)..], Decodificar(doc["fields"] as JsonObject)));
        }
        return lista;
    }

    private async Task<(HttpStatusCode, string)> PostAsync(string url, string json, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var token = await _token.ObterAsync(ct);
            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resposta = await _http.SendAsync(pedido, ct);
                var texto = await resposta.Content.ReadAsStringAsync(ct);
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 1) { _token.Invalidar(); continue; }
                return (resposta.StatusCode, texto);
            }
            catch (HttpRequestException ex)
            {
                throw new FalhaDestinoException("Sem conexão com a internet (não foi possível ler o Firebase do ERP).", GravidadeFalha.Interrompe, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new FalhaDestinoException("O Firebase do ERP demorou demais para responder.", GravidadeFalha.Interrompe, ex);
            }
        }
    }

    /// <summary>Converte campos no formato do Firestore (<c>{"stringValue": ...}</c>) para JSON comum.</summary>
    internal static JsonObject Decodificar(JsonObject? campos)
    {
        var o = new JsonObject();
        if (campos is null) return o;
        foreach (var (nome, valor) in campos) o[nome] = Valor(valor as JsonObject);
        return o;
    }

    private static JsonNode? Valor(JsonObject? v)
    {
        if (v is null) return null;
        if (v.TryGetPropertyValue("stringValue", out var s)) return JsonValue.Create(s!.GetValue<string>());
        if (v.TryGetPropertyValue("integerValue", out var i)) return JsonValue.Create(long.Parse(i!.ToString().Trim('"'), CultureInfo.InvariantCulture));
        if (v.TryGetPropertyValue("doubleValue", out var d)) return JsonValue.Create(d!.GetValue<double>());
        if (v.TryGetPropertyValue("booleanValue", out var b)) return JsonValue.Create(b!.GetValue<bool>());
        if (v.TryGetPropertyValue("timestampValue", out var t)) return JsonValue.Create(t!.GetValue<string>());
        if (v.TryGetPropertyValue("mapValue", out var m)) return Decodificar((m as JsonObject)?["fields"] as JsonObject);
        if (v.TryGetPropertyValue("arrayValue", out var a))
        {
            var lista = new JsonArray();
            if ((a as JsonObject)?["values"] is JsonArray itens)
                foreach (var x in itens) lista.Add(Valor(x as JsonObject));
            return lista;
        }
        return null; // nullValue, referenceValue, geoPoint...
    }
}
