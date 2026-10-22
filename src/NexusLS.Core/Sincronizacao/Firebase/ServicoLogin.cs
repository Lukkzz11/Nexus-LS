using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>Sessão do usuário. Guarda só o refresh token (protegido no disco); a senha nunca é gravada.</summary>
public class SessaoUsuario
{
    public string Uid { get; set; } = "";
    public string Email { get; set; } = "";
    public string? RefreshTokenProtegido { get; set; }

    /// <summary>Última vez em que o Firebase confirmou esta sessão (login ou renovação). Base do prazo offline.</summary>
    public DateTime UltimaValidacaoOnline { get; set; }
}

public enum TipoFalhaLogin { Credenciais, Conexao, Servidor, SessaoExpirada }

public class FalhaLoginException : Exception
{
    public TipoFalhaLogin Tipo { get; }
    public FalhaLoginException(string mensagem, TipoFalhaLogin tipo, Exception? causa = null) : base(mensagem, causa) => Tipo = tipo;
}

public class SessaoStore
{
    private readonly string _caminho;
    public SessaoStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "sessao.json");

    public SessaoUsuario? Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return null;
            var s = JsonSerializer.Deserialize<SessaoUsuario>(File.ReadAllText(_caminho));
            return s is { Uid.Length: > 0 } ? s : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public void Salvar(SessaoUsuario sessao)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(sessao));
        File.Move(tmp, _caminho, overwrite: true);
    }

    public void Limpar()
    {
        if (File.Exists(_caminho)) File.Delete(_caminho);
    }
}

/// <summary>
/// Login do usuário pelo Firebase Authentication (REST, só com a chave pública da API — nenhuma credencial de
/// administrador). Depois do primeiro login a sessão é restaurada ao abrir o programa: com internet, renovando o token;
/// sem internet, continua valendo por <see cref="LimiteOffline"/> desde a última validação.
/// </summary>
public sealed class ServicoLogin : IProvedorToken
{
    private const string UrlLogin = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword";
    private const string UrlRenovar = "https://securetoken.googleapis.com/v1/token";
    private static readonly TimeSpan Folga = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly SessaoStore _store;
    private readonly IProtetorSegredo _protetor;
    private readonly Func<DateTime> _agora;
    private readonly SemaphoreSlim _trava = new(1, 1);

    private string? _idToken;
    private DateTime _vence;

    public TimeSpan LimiteOffline { get; init; } = TimeSpan.FromDays(14);
    public SessaoUsuario? Atual { get; private set; }

    /// <summary>A sessão foi aceita só pelo prazo offline (não deu para falar com o Firebase).</summary>
    public bool Offline { get; private set; }

    public string? Uid => Atual?.Uid;

    public ServicoLogin(HttpClient http, string apiKey, SessaoStore store, IProtetorSegredo protetor, Func<DateTime>? agora = null)
    {
        _http = http;
        _apiKey = apiKey;
        _store = store;
        _protetor = protetor;
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    public async Task<SessaoUsuario> EntrarAsync(string email, string senha, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(senha))
            throw new FalhaLoginException("Informe o e-mail e a senha.", TipoFalhaLogin.Credenciais);

        var resposta = await PostAsync($"{UrlLogin}?key={Uri.EscapeDataString(_apiKey)}",
            JsonContent.Create(new { email = email.Trim(), password = senha, returnSecureToken = true }), ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode) throw TraduzirErro((int)resposta.StatusCode, texto);

        var json = JsonNode.Parse(texto)!;
        var uid = json["localId"]?.GetValue<string>();
        var refresh = json["refreshToken"]?.GetValue<string>();
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(refresh))
            throw new FalhaLoginException("O Firebase respondeu sem os dados da sessão.", TipoFalhaLogin.Servidor);

        await _trava.WaitAsync(ct);
        try
        {
            GuardarToken(json["idToken"]?.GetValue<string>(), json["expiresIn"]?.GetValue<string>());
            Atual = new SessaoUsuario
            {
                Uid = uid, Email = json["email"]?.GetValue<string>() ?? email.Trim(),
                RefreshTokenProtegido = _protetor.Proteger(refresh), UltimaValidacaoOnline = _agora()
            };
            Offline = false;
            _store.Salvar(Atual);
            return Atual;
        }
        finally { _trava.Release(); }
    }

    /// <summary>Ao abrir o programa. Nulo = precisa entrar de novo. Credencial revogada ou prazo offline vencido limpa a sessão.</summary>
    public async Task<SessaoUsuario?> RestaurarAsync(CancellationToken ct = default)
    {
        var salva = _store.Carregar();
        if (salva is null) return null;

        await _trava.WaitAsync(ct);
        try
        {
            Atual = salva;
            try
            {
                await RenovarAsync(ct);
                Offline = false;
                Atual.UltimaValidacaoOnline = _agora();
                _store.Salvar(Atual);
                return Atual;
            }
            catch (FalhaLoginException ex) when (ex.Tipo is TipoFalhaLogin.Conexao or TipoFalhaLogin.Servidor)
            {
                if (_agora() - salva.UltimaValidacaoOnline <= LimiteOffline) { Offline = true; return Atual; }
                EncerrarLocal();
                throw new FalhaLoginException(
                    $"Sem internet há mais de {(int)LimiteOffline.TotalDays} dias. Conecte-se para entrar de novo.", TipoFalhaLogin.SessaoExpirada, ex);
            }
            catch (FalhaLoginException)
            {
                EncerrarLocal();
                return null;
            }
        }
        finally { _trava.Release(); }
    }

    public void Sair()
    {
        EncerrarLocal();
    }

    private void EncerrarLocal()
    {
        Atual = null;
        Offline = false;
        _idToken = null;
        _vence = DateTime.MinValue;
        _store.Limpar();
    }

    // ---- IProvedorToken (o destino do catálogo pede o token aqui) ----

    public async Task<string> ObterAsync(CancellationToken ct = default)
    {
        await _trava.WaitAsync(ct);
        try
        {
            if (Atual is null) throw new FalhaDestinoException("Entre com seu usuário para sincronizar com o Firebase.");
            if (_idToken is not null && _agora() < _vence - Folga) return _idToken;
            try
            {
                await RenovarAsync(ct);
                Atual.UltimaValidacaoOnline = _agora();
                Offline = false;
                _store.Salvar(Atual);
            }
            catch (FalhaLoginException ex)
            {
                throw new FalhaDestinoException(ex.Message, GravidadeFalha.Interrompe, ex);
            }
            return _idToken!;
        }
        finally { _trava.Release(); }
    }

    public void Invalidar() => _vence = DateTime.MinValue;

    // ---- internos ----

    private async Task RenovarAsync(CancellationToken ct)
    {
        var refresh = _protetor.Desproteger(Atual!.RefreshTokenProtegido ?? "");
        if (string.IsNullOrEmpty(refresh)) throw new FalhaLoginException("Sessão inválida.", TipoFalhaLogin.SessaoExpirada);

        using var corpo = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh
        });
        var resposta = await PostAsync($"{UrlRenovar}?key={Uri.EscapeDataString(_apiKey)}", corpo, ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode)
        {
            if ((int)resposta.StatusCode >= 500) throw new FalhaLoginException("O Firebase está indisponível.", TipoFalhaLogin.Servidor);
            throw new FalhaLoginException("Sua sessão expirou ou foi revogada. Entre novamente.", TipoFalhaLogin.SessaoExpirada);
        }
        var json = JsonNode.Parse(texto)!;
        GuardarToken(json["id_token"]?.GetValue<string>(), json["expires_in"]?.GetValue<string>());
        var novo = json["refresh_token"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(novo)) Atual.RefreshTokenProtegido = _protetor.Proteger(novo);
    }

    private void GuardarToken(string? token, string? expiraEm)
    {
        if (string.IsNullOrEmpty(token))
            throw new FalhaLoginException("O Firebase não devolveu o token de acesso.", TipoFalhaLogin.Servidor);
        _idToken = token;
        _vence = _agora().AddSeconds(int.TryParse(expiraEm, out var s) ? s : 3600);
    }

    private async Task<HttpResponseMessage> PostAsync(string url, HttpContent corpo, CancellationToken ct)
    {
        try { return await _http.PostAsync(url, corpo, ct); }
        catch (HttpRequestException ex)
        {
            throw new FalhaLoginException("Sem conexão com a internet (não foi possível falar com o Firebase).", TipoFalhaLogin.Conexao, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new FalhaLoginException("O Firebase demorou demais para responder.", TipoFalhaLogin.Conexao, ex);
        }
    }

    private static FalhaLoginException TraduzirErro(int status, string corpo)
    {
        var codigo = "";
        try { codigo = JsonNode.Parse(corpo)?["error"]?["message"]?.GetValue<string>() ?? ""; }
        catch (JsonException) { }

        if (status >= 500) return new FalhaLoginException("O Firebase está indisponível. Tente em instantes.", TipoFalhaLogin.Servidor);
        var mensagem =
            codigo.Contains("INVALID_LOGIN_CREDENTIALS") || codigo.Contains("INVALID_PASSWORD") || codigo.Contains("EMAIL_NOT_FOUND")
                ? "E-mail ou senha incorretos."
            : codigo.Contains("USER_DISABLED") ? "Este usuário está desativado."
            : codigo.Contains("TOO_MANY_ATTEMPTS") ? "Muitas tentativas. Aguarde alguns minutos."
            : codigo.Contains("API_KEY") || codigo.Contains("API key not valid") ? "A chave da API do Firebase não é válida para este projeto."
            : codigo.Contains("OPERATION_NOT_ALLOWED") ? "O login por e-mail e senha não está ligado neste projeto do Firebase."
            : $"O Firebase recusou o login (código {status}).";
        return new FalhaLoginException(mensagem, TipoFalhaLogin.Credenciais);
    }
}
