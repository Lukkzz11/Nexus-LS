using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Vitrine;

/// <summary>Publica a configuração da vitrine no documento <c>meta/vitrine</c> do Firestore (substitui o documento inteiro).</summary>
public interface IPublicadorVitrine
{
    Task PublicarAsync(ConfiguracaoVitrine config, CancellationToken ct = default);
}

/// <summary>Publica a vitrine completa do editor visual (o JSON inteiro, num campo <c>v2</c> do mesmo documento, sem tocar nos outros campos).</summary>
public interface IPublicadorVitrineCompleta
{
    Task PublicarCompletaAsync(string json, CancellationToken ct = default);
}

/// <summary>Publica, em cada produto já enviado, só os campos do catálogo online (visível, destaque, preço promocional).</summary>
public interface IPublicadorAjustesProduto
{
    /// <summary>Devolve falso se o produto ainda não existe na nuvem (nada é criado: o documento nasce na sincronização do catálogo).</summary>
    Task<bool> PublicarAjusteAsync(string idProduto, bool visivel, bool destaque, long promocaoCentavos, CancellationToken ct = default);
}

public static class VitrineJson
{
    private static JsonObject Mapa(Dictionary<string, JsonObject> campos)
    {
        var f = new JsonObject();
        foreach (var (k, v) in campos) f[k] = v;
        return FirestoreJson.Mapa(f);
    }

    /// <summary>Os campos do documento. Só entra o que a vitrine usa; texto vazio é omitido (a vitrine usa o padrão dela).</summary>
    public static JsonObject Campos(ConfiguracaoVitrine c, DateTime agora)
    {
        static Dictionary<string, JsonObject> SemVazios(params (string Chave, string Valor)[] itens) =>
            itens.Where(i => i.Valor.Length > 0).ToDictionary(i => i.Chave, i => FirestoreJson.Texto(i.Valor));

        var campos = new JsonObject
        {
            ["modelo"] = FirestoreJson.Texto(c.Modelo),
            ["fonte"] = FirestoreJson.Texto(c.Fonte),
            ["cores"] = Mapa(c.Cores.ToDictionary(k => k.Key, k => FirestoreJson.Texto(k.Value))),
            ["textos"] = Mapa(SemVazios(("anuncio", c.TextoAnuncio), ("eyebrow", c.Eyebrow), ("titulo", c.Titulo))),
            ["loja"] = Mapa(SemVazios(("nome", c.LojaNome), ("apresentacao", c.LojaApresentacao), ("telefone", c.Telefone),
                ("whatsapp", c.Whatsapp), ("instagram", c.Instagram), ("endereco", c.Endereco), ("email", c.Email))),
            ["atacado"] = Mapa(new() { ["minPecas"] = FirestoreJson.Inteiro(c.AtacadoMinPecas) }),
            ["carrossel"] = Mapa(new() { ["novidadesQtd"] = FirestoreJson.Inteiro(c.NovidadesQtd) }),
            ["produtos"] = Mapa(new() { ["ordem"] = FirestoreJson.Texto(c.OrdemProdutos), ["mostrarDescricao"] = FirestoreJson.Booleano(c.MostrarDescricao) }),
            ["banners"] = FirestoreJson.Lista(c.Banners.Select(b => Mapa(SemVazios(
                ("badge", b.Selo), ("title", b.Titulo), ("highlight", b.Destaque), ("subtitle", b.Subtitulo), ("ctaLabel", b.BotaoRotulo))))),
            ["atualizadoEm"] = FirestoreJson.Momento(agora),
            ["origem"] = FirestoreJson.Texto(FirestoreJson.Origem)
        };
        return campos;
    }
}

public sealed class PublicadorVitrineRest : IPublicadorVitrine, IPublicadorVitrineCompleta, IPublicadorAjustesProduto
{
    private readonly HttpClient _http;
    private readonly string _projectId;
    private readonly IProvedorToken _token;
    private readonly Func<DateTime> _agora;

    public PublicadorVitrineRest(HttpClient http, string projectId, IProvedorToken token, Func<DateTime>? agora = null)
    {
        if (NexusLS.Core.Importacao.Erp.GuardaErp.EhProjetoDoErp(projectId)) throw new FalhaDestinoException(NexusLS.Core.Importacao.Erp.GuardaErp.MensagemSomenteLeitura);
        _http = http;
        _projectId = projectId;
        _token = token;
        _agora = agora ?? (() => DateTime.Now);
    }

    public Task PublicarAsync(ConfiguracaoVitrine config, CancellationToken ct = default)
    {
        config.Normalizar();
        var corpo = new JsonObject { ["fields"] = VitrineJson.Campos(config, _agora()) }.ToJsonString();
        return EnviarAsync(UrlDocumento, corpo, ct);
    }

    /// <summary>Grava só <c>v2</c> e <c>atualizadoEm</c> (máscara de campos): o resto do documento fica como está.</summary>
    public Task PublicarCompletaAsync(string json, CancellationToken ct = default)
    {
        var campos = new JsonObject { ["v2"] = FirestoreJson.Texto(json), ["atualizadoEm"] = FirestoreJson.Momento(_agora()) };
        var corpo = new JsonObject { ["fields"] = campos }.ToJsonString();
        return EnviarAsync(UrlDocumento + "?updateMask.fieldPaths=v2&updateMask.fieldPaths=atualizadoEm", corpo, ct);
    }

    public Task<bool> PublicarAjusteAsync(string idProduto, bool visivel, bool destaque, long promocaoCentavos, CancellationToken ct = default)
    {
        var campos = new JsonObject
        {
            ["ativoNoCatalogo"] = FirestoreJson.Booleano(visivel),
            ["destaque"] = FirestoreJson.Booleano(destaque),
            ["precoPromocionalCentavos"] = FirestoreJson.Inteiro(promocaoCentavos)
        };
        var corpo = new JsonObject { ["fields"] = campos }.ToJsonString();
        var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/produtos/{Uri.EscapeDataString(idProduto)}"
            + "?updateMask.fieldPaths=ativoNoCatalogo&updateMask.fieldPaths=destaque&updateMask.fieldPaths=precoPromocionalCentavos&currentDocument.exists=true";
        return EnviarAsync(url, corpo, ct, toleraNaoEncontrado: true);
    }

    private string UrlDocumento => $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/meta/vitrine";

    private async Task<bool> EnviarAsync(string url, string corpo, CancellationToken ct, bool toleraNaoEncontrado = false)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var token = await _token.ObterAsync(ct);
            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Patch, url) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
                pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resposta = await _http.SendAsync(pedido, ct);
                var texto = await resposta.Content.ReadAsStringAsync(ct);
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 1) { _token.Invalidar(); continue; }
                if (resposta.StatusCode == HttpStatusCode.NotFound && toleraNaoEncontrado) return false;
                if (resposta.StatusCode != HttpStatusCode.OK) throw FirestoreRestDestino.TraduzirErro(resposta.StatusCode, texto);
                return true;
            }
            catch (HttpRequestException ex)
            {
                throw new FalhaDestinoException("Sem conexão com a internet (não foi possível publicar a vitrine).", GravidadeFalha.Interrompe, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new FalhaDestinoException("O Firestore demorou demais para responder. Tente de novo.", GravidadeFalha.Interrompe, ex);
            }
        }
    }
}

/// <summary>O publicador que o programa usa: lê a configuração do Firebase salva a cada uso.</summary>
public sealed class PublicadorVitrineConfiguravel : IPublicadorVitrine, IPublicadorVitrineCompleta, IPublicadorAjustesProduto
{
    private readonly ConfiguracaoFirebaseStore _store;
    private readonly HttpClient _http;
    public PublicadorVitrineConfiguravel(ConfiguracaoFirebaseStore store, HttpClient http) { _store = store; _http = http; }

    public Task PublicarAsync(ConfiguracaoVitrine config, CancellationToken ct = default) => Criar().PublicarAsync(config, ct);

    public Task PublicarCompletaAsync(string json, CancellationToken ct = default) => Criar().PublicarCompletaAsync(json, ct);

    public Task<bool> PublicarAjusteAsync(string idProduto, bool visivel, bool destaque, long promocaoCentavos, CancellationToken ct = default) =>
        Criar().PublicarAjusteAsync(idProduto, visivel, destaque, promocaoCentavos, ct);

    private PublicadorVitrineRest Criar()
    {
        var cfg = _store.Carregar();
        if (!cfg.EstaCompleta)
            throw new FalhaDestinoException("O Firebase ainda não foi configurado. Preencha o projeto, a chave da API, o e-mail e a senha na tela Catálogo online.");
        var auth = new FirebaseAuthRest(_http, cfg.ApiKey, cfg.Email, _store.ObterSenha, () => DateTime.UtcNow);
        return new PublicadorVitrineRest(_http, cfg.ProjectId, auth);
    }
}
