using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using NexusLS.Core.Services;

namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>
/// Envia fotos ao Firebase Storage pela API REST, com o mesmo login da licença. Devolve o endereço público
/// (com o token de download que o próprio Storage gera). Erros viram <see cref="FalhaDestinoException"/> em português.
/// </summary>
public sealed class StorageRestEnviador : IEnviadorFoto
{
    private readonly HttpClient _http;
    private readonly string _bucket;
    private readonly IProvedorToken _token;

    public StorageRestEnviador(HttpClient http, string bucket, IProvedorToken token)
    {
        _http = http;
        _bucket = bucket;
        _token = token;
    }

    public static string UrlPublica(string bucket, string nome, string tokenDownload) =>
        $"https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{Uri.EscapeDataString(nome)}?alt=media&token={tokenDownload}";

    public async Task<string> EnviarAsync(string nomeNoDestino, byte[] jpeg, CancellationToken ct = default)
    {
        var url = $"https://firebasestorage.googleapis.com/v0/b/{_bucket}/o?uploadType=media&name={Uri.EscapeDataString(nomeNoDestino)}";
        for (var tentativa = 1; ; tentativa++)
        {
            var token = await _token.ObterAsync(ct);
            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(jpeg) };
                pedido.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                pedido.Headers.Authorization = new AuthenticationHeaderValue("Firebase", token);
                using var resposta = await _http.SendAsync(pedido, ct);
                var corpo = await resposta.Content.ReadAsStringAsync(ct);
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 1)
                {
                    _token.Invalidar();
                    continue;
                }
                if (resposta.StatusCode != HttpStatusCode.OK) throw Traduzir(resposta.StatusCode);

                var json = JsonNode.Parse(corpo);
                var nome = json?["name"]?.GetValue<string>() ?? nomeNoDestino;
                var tokens = json?["downloadTokens"]?.GetValue<string>();
                var primeiro = tokens?.Split(',')[0];
                if (string.IsNullOrWhiteSpace(primeiro))
                    throw new FalhaDestinoException("O Storage guardou a foto mas não devolveu o endereço de download.", GravidadeFalha.DadosRecusados);
                return UrlPublica(_bucket, nome, primeiro);
            }
            catch (HttpRequestException ex)
            {
                throw new FalhaDestinoException("Sem conexão com a internet (não foi possível enviar a foto).", GravidadeFalha.Interrompe, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new FalhaDestinoException("O envio da foto demorou demais. Tente de novo.", GravidadeFalha.Interrompe, ex);
            }
        }
    }

    internal static FalhaDestinoException Traduzir(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new FalhaDestinoException("O login do Firebase foi recusado. Confira o e-mail e a senha na tela Catálogo online."),
        HttpStatusCode.Forbidden => new FalhaDestinoException("Sem permissão para gravar fotos no Storage. Confira as regras de segurança do Storage do projeto."),
        HttpStatusCode.NotFound => new FalhaDestinoException("O Storage não foi encontrado neste projeto. Ative o Storage no console do Firebase e confira o nome do bucket."),
        HttpStatusCode.TooManyRequests => new FalhaDestinoException("O Storage recusou por excesso de uso (limite do plano). Tente mais tarde."),
        HttpStatusCode.BadRequest => new FalhaDestinoException("O Storage recusou a foto.", GravidadeFalha.DadosRecusados),
        _ when (int)status >= 500 => new FalhaDestinoException("O Storage está com problema no momento. Tente mais tarde."),
        _ => new FalhaDestinoException($"O Storage respondeu com um erro inesperado (código {(int)status}).")
    };
}

/// <summary>O enviador que o programa usa: monta o login e o destino a partir da configuração salva, a cada uso.</summary>
public sealed class EnviadorFotoConfiguravel : IEnviadorFoto
{
    private readonly ConfiguracaoFirebaseStore _store;
    private readonly HttpClient _http;

    public EnviadorFotoConfiguravel(ConfiguracaoFirebaseStore store, HttpClient http)
    {
        _store = store;
        _http = http;
    }

    public Task<string> EnviarAsync(string nomeNoDestino, byte[] jpeg, CancellationToken ct = default)
    {
        var cfg = _store.Carregar();
        if (!cfg.EstaCompleta)
            throw new FalhaDestinoException("O Firebase ainda não foi configurado. Preencha o projeto, a chave da API, o e-mail e a senha na tela Catálogo online.");
        var auth = _auth is not null && _assinatura == $"{cfg.ApiKey}|{cfg.Email}|{cfg.SenhaProtegida}"
            ? _auth
            : _auth = new FirebaseAuthRest(_http, cfg.ApiKey, cfg.Email, _store.ObterSenha, () => DateTime.UtcNow);
        _assinatura = $"{cfg.ApiKey}|{cfg.Email}|{cfg.SenhaProtegida}";
        return new StorageRestEnviador(_http, cfg.BucketEfetivo, auth).EnviarAsync(nomeNoDestino, jpeg, ct);
    }

    private FirebaseAuthRest? _auth;
    private string? _assinatura;
}
