using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>De onde vem o token que autoriza as chamadas ao Firestore.</summary>
public interface IProvedorToken
{
    Task<string> ObterAsync(CancellationToken ct = default);

    /// <summary>Esquece o token atual (o servidor recusou): o próximo pedido faz um login novo.</summary>
    void Invalidar();
}

/// <summary>
/// Login no Firebase Authentication pela API REST (e-mail e senha da licença). Guarda o token na memória, renova
/// sozinho antes de vencer e nunca escreve token nem senha em arquivo, log ou mensagem de erro.
/// </summary>
public sealed class FirebaseAuthRest : IProvedorToken
{
    private const string UrlLogin = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword";
    private const string UrlRenovar = "https://securetoken.googleapis.com/v1/token";
    private static readonly TimeSpan Folga = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _email;
    private readonly Func<string?> _senha;
    private readonly Func<DateTime> _agora;
    private readonly SemaphoreSlim _trava = new(1, 1);

    private string? _token;
    private string? _refresh;
    private DateTime _vence;

    public FirebaseAuthRest(HttpClient http, string apiKey, string email, Func<string?> senha, Func<DateTime>? agora = null)
    {
        _http = http;
        _apiKey = apiKey;
        _email = email;
        _senha = senha;
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    public async Task<string> ObterAsync(CancellationToken ct = default)
    {
        await _trava.WaitAsync(ct);
        try
        {
            if (_token is not null && _agora() < _vence - Folga) return _token;

            if (_refresh is not null && await TentarRenovarAsync(ct)) return _token!;
            await EntrarAsync(ct);
            return _token!;
        }
        finally
        {
            _trava.Release();
        }
    }

    public void Invalidar()
    {
        _token = null;
        _vence = DateTime.MinValue; // o refresh token continua: o próximo pedido tenta renovar antes de pedir a senha de novo
    }

    private async Task EntrarAsync(CancellationToken ct)
    {
        var senha = _senha();
        if (string.IsNullOrEmpty(senha))
            throw new FalhaDestinoException("Falta a senha do login do Firebase. Informe-a na tela Catálogo online.");

        using var resposta = await EnviarAsync(HttpMethod.Post, $"{UrlLogin}?key={Uri.EscapeDataString(_apiKey)}",
            JsonContent.Create(new { email = _email, password = senha, returnSecureToken = true }), ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode) throw TraduzirErroDeLogin((int)resposta.StatusCode, texto);

        var json = JsonNode.Parse(texto)!;
        Guardar(json["idToken"]?.GetValue<string>(), json["refreshToken"]?.GetValue<string>(), json["expiresIn"]?.GetValue<string>());
    }

    private async Task<bool> TentarRenovarAsync(CancellationToken ct)
    {
        using var corpo = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refresh!
        });
        using var resposta = await EnviarAsync(HttpMethod.Post, $"{UrlRenovar}?key={Uri.EscapeDataString(_apiKey)}", corpo, ct);
        if (!resposta.IsSuccessStatusCode)
        {
            _refresh = null; // não serviu: cai no login normal
            return false;
        }
        var json = JsonNode.Parse(await resposta.Content.ReadAsStringAsync(ct))!;
        Guardar(json["id_token"]?.GetValue<string>(), json["refresh_token"]?.GetValue<string>(), json["expires_in"]?.GetValue<string>());
        return _token is not null;
    }

    private void Guardar(string? token, string? refresh, string? expiraEmSegundos)
    {
        if (string.IsNullOrEmpty(token))
            throw new FalhaDestinoException("O Firebase não devolveu o token de acesso.");
        _token = token;
        _refresh = refresh ?? _refresh;
        _vence = _agora().AddSeconds(int.TryParse(expiraEmSegundos, out var s) ? s : 3600);
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, HttpContent corpo, CancellationToken ct)
    {
        try
        {
            using var pedido = new HttpRequestMessage(metodo, url) { Content = corpo };
            return await _http.SendAsync(pedido, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new FalhaDestinoException("Sem conexão com a internet (não foi possível falar com o Firebase).", GravidadeFalha.Interrompe, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new FalhaDestinoException("O Firebase demorou demais para responder. Tente de novo.", GravidadeFalha.Interrompe, ex);
        }
    }

    /// <summary>Traduz o erro do login para português, sem repetir senha nem token.</summary>
    internal static FalhaDestinoException TraduzirErroDeLogin(int status, string corpo)
    {
        var codigo = "";
        try { codigo = JsonNode.Parse(corpo)?["error"]?["message"]?.GetValue<string>() ?? ""; }
        catch (JsonException) { }

        string mensagem =
            codigo.Contains("INVALID_LOGIN_CREDENTIALS") || codigo.Contains("INVALID_PASSWORD") || codigo.Contains("EMAIL_NOT_FOUND")
                ? "E-mail ou senha do Firebase incorretos."
            : codigo.Contains("USER_DISABLED")
                ? "O usuário do Firebase está desativado."
            : codigo.Contains("TOO_MANY_ATTEMPTS")
                ? "Muitas tentativas de login. Aguarde alguns minutos e tente de novo."
            : codigo.Contains("API_KEY") || codigo.Contains("API key not valid")
                ? "A chave da API do Firebase não é válida para este projeto."
            : codigo.Contains("OPERATION_NOT_ALLOWED")
                ? "O login por e-mail e senha não está ligado no Firebase Authentication deste projeto."
            : $"O Firebase recusou o login (código {status}).";
        return new FalhaDestinoException(mensagem);
    }
}
