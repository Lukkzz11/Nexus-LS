using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Google;

/// <summary>
/// Credenciais do cliente OAuth do Google deste cliente (cada loja cria o seu no Google Cloud) e o "refresh token" da conexão.
/// Segredo e token ficam protegidos (DPAPI no Windows) e nunca saem em log ou mensagem de erro.
/// </summary>
public class ConfiguracaoGoogle
{
    public string ClientId { get; set; } = "";
    public string? ClientSecretProtegido { get; set; }
    public string? RefreshTokenProtegido { get; set; }
    public string? NomeDaPasta { get; set; }
    public bool Conectado => !string.IsNullOrEmpty(RefreshTokenProtegido);
    public bool TemCredenciais => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrEmpty(ClientSecretProtegido);
}

public class GoogleStore
{
    private readonly string _caminho;
    private readonly IProtetorSegredo _protetor;

    public GoogleStore(string caminho, IProtetorSegredo protetor)
    {
        _caminho = caminho;
        _protetor = protetor;
    }

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "google.json");

    public ConfiguracaoGoogle Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<ConfiguracaoGoogle>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Salvar(ConfiguracaoGoogle c)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }

    public string? ClientSecret() => Desproteger(Carregar().ClientSecretProtegido);
    public string? RefreshToken() => Desproteger(Carregar().RefreshTokenProtegido);
    public string Proteger(string texto) => _protetor.Proteger(texto);
    private string? Desproteger(string? t) => string.IsNullOrEmpty(t) ? null : _protetor.Desproteger(t);
}

/// <summary>Login no Google pelo navegador (OAuth 2.0 para aplicativo instalado, com PKCE e retorno em 127.0.0.1).</summary>
public class GoogleOAuth
{
    public const string Escopo = "https://www.googleapis.com/auth/drive.file";   // só arquivos criados pelo próprio programa
    public const string EscopoAgenda = "https://www.googleapis.com/auth/calendar.events"; // só os eventos (não lê nem apaga as agendas em si)
    private const string UrlAutorizar = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string UrlToken = "https://oauth2.googleapis.com/token";

    private readonly HttpClient _http;
    private readonly GoogleStore _store;
    private readonly Func<DateTime> _agora;
    private string? _acesso;
    private DateTime _vence;

    public GoogleOAuth(HttpClient http, GoogleStore store, Func<DateTime>? agora = null)
    {
        _http = http;
        _store = store;
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    public static (string Verificador, string Desafio) NovoPkce()
    {
        var verificador = Base64Url(RandomNumberGenerator.GetBytes(48));
        return (verificador, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificador))));
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string MontarUrlAutorizacao(string clientId, string redirect, string desafio, string estado) =>
        $"{UrlAutorizar}?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}&response_type=code" +
        $"&scope={Uri.EscapeDataString(Escopo + " " + EscopoAgenda)}&code_challenge={desafio}&code_challenge_method=S256&state={estado}&access_type=offline&prompt=consent";

    /// <summary>
    /// Abre o navegador para o usuário autorizar e espera a volta. <paramref name="abrirNavegador"/> recebe a URL.
    /// Guarda o refresh token protegido. Falhas viram <see cref="RegraDeNegocioException"/> em português.
    /// </summary>
    public async Task ConectarAsync(Action<string> abrirNavegador, TimeSpan? espera = null, CancellationToken ct = default)
    {
        var cfg = _store.Carregar();
        var segredo = _store.ClientSecret();
        if (!cfg.TemCredenciais || segredo is null)
            throw new RegraDeNegocioException("Informe e salve o Client ID e o Client Secret do Google antes de conectar.");

        var porta = PortaLivre();
        var redirect = $"http://127.0.0.1:{porta}/";
        var (verificador, desafio) = NovoPkce();
        var estado = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var ouvinte = new HttpListener();
        ouvinte.Prefixes.Add(redirect);
        try { ouvinte.Start(); }
        catch (HttpListenerException) { throw new RegraDeNegocioException("Não foi possível abrir a porta local para receber a resposta do Google."); }

        abrirNavegador(MontarUrlAutorizacao(cfg.ClientId.Trim(), redirect, desafio, estado));

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(espera ?? TimeSpan.FromMinutes(3));
        string? codigo = null, erro = null;
        try
        {
            while (codigo is null && erro is null)
            {
                var ctx = await ouvinte.GetContextAsync().WaitAsync(limite.Token);
                var q = ctx.Request.QueryString;
                if (q["state"] != estado) { await Responder(ctx, "Pedido inválido.", 400); continue; }   // não é a nossa volta: ignora
                erro = q["error"];
                codigo = q["code"];
                await Responder(ctx, erro is null ? "Tudo certo! Pode fechar esta janela e voltar ao Nexus LS." : "A conexão foi cancelada. Pode fechar esta janela.", 200);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RegraDeNegocioException("O tempo para autorizar acabou. Clique em Conectar e tente de novo.");
        }
        if (erro is not null) throw new RegraDeNegocioException("O Google não autorizou a conexão (" + erro + ").");

        var json = await PedirTokenAsync(new Dictionary<string, string>
        {
            ["code"] = codigo!, ["client_id"] = cfg.ClientId.Trim(), ["client_secret"] = segredo,
            ["code_verifier"] = verificador, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code"
        }, ct);
        var refresh = json["refresh_token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(refresh))
            throw new RegraDeNegocioException("O Google não devolveu a permissão de uso contínuo. Remova o acesso do Nexus LS em myaccount.google.com/permissions e conecte de novo.");
        cfg.RefreshTokenProtegido = _store.Proteger(refresh);
        _store.Salvar(cfg);
        Guardar(json);
    }

    public void Desconectar()
    {
        var cfg = _store.Carregar();
        cfg.RefreshTokenProtegido = null;
        _store.Salvar(cfg);
        _acesso = null;
    }

    /// <summary>O token de acesso válido (renova com o refresh token quando vence).</summary>
    public async Task<string> ObterAcessoAsync(CancellationToken ct = default)
    {
        if (_acesso is not null && _agora() < _vence) return _acesso;
        var cfg = _store.Carregar();
        var refresh = _store.RefreshToken();
        var segredo = _store.ClientSecret();
        if (refresh is null || segredo is null || !cfg.TemCredenciais)
            throw new RegraDeNegocioException("O Google não está conectado. Conecte em Configurações > Backup.");
        var json = await PedirTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId.Trim(), ["client_secret"] = segredo, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token"
        }, ct);
        Guardar(json);
        return _acesso!;
    }

    public void Invalidar() => _acesso = null;

    private void Guardar(JsonNode json)
    {
        _acesso = json["access_token"]?.GetValue<string>() ?? throw new RegraDeNegocioException("O Google não devolveu o acesso.");
        var segundos = json["expires_in"]?.GetValue<int>() ?? 3000;
        _vence = _agora().AddSeconds(Math.Max(60, segundos - 120));
    }

    private async Task<JsonNode> PedirTokenAsync(Dictionary<string, string> campos, CancellationToken ct)
    {
        try
        {
            using var resposta = await _http.PostAsync(UrlToken, new FormUrlEncodedContent(campos), ct);
            var texto = await resposta.Content.ReadAsStringAsync(ct);
            if (resposta.IsSuccessStatusCode) return JsonNode.Parse(texto) ?? throw new RegraDeNegocioException("Resposta vazia do Google.");
            var erro = "";
            try { erro = JsonNode.Parse(texto)?["error"]?.GetValue<string>() ?? ""; } catch (JsonException) { }
            throw erro switch
            {
                "invalid_grant" => new RegraDeNegocioException("A permissão do Google venceu ou foi retirada. Conecte de novo."),
                "invalid_client" => new RegraDeNegocioException("O Google recusou o Client ID ou o Client Secret. Confira os dois."),
                "access_denied" => new RegraDeNegocioException("O Google negou o acesso."),
                _ => new RegraDeNegocioException($"O Google recusou o pedido (código {(int)resposta.StatusCode}).")
            };
        }
        catch (HttpRequestException) { throw new RegraDeNegocioException("Sem conexão com a internet (não foi possível falar com o Google)."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new RegraDeNegocioException("O Google demorou demais para responder."); }
    }

    private static async Task Responder(HttpListenerContext ctx, string texto, int status)
    {
        var corpo = Encoding.UTF8.GetBytes($"<html><head><meta charset=\"utf-8\"><title>Nexus LS</title></head><body style=\"font-family:Segoe UI,sans-serif;padding:40px\"><h2>{WebUtility.HtmlEncode(texto)}</h2></body></html>");
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = corpo.Length;
        await ctx.Response.OutputStream.WriteAsync(corpo);
        ctx.Response.Close();
    }

    private static int PortaLivre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var porta = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return porta;
    }
}

/// <summary>Envia arquivos ao Google Drive (pasta própria do programa, escopo drive.file).</summary>
public class DriveRest
{
    private const string Api = "https://www.googleapis.com/drive/v3";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3/files";
    private readonly HttpClient _http;
    private readonly GoogleOAuth _auth;

    public DriveRest(HttpClient http, GoogleOAuth auth)
    {
        _http = http;
        _auth = auth;
    }

    public async Task<string> ObterOuCriarPastaAsync(string nome, CancellationToken ct = default)
    {
        var q = $"name='{nome.Replace("\\", "\\\\").Replace("'", "\\'")}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
        var achou = await ChamarAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files?q={Uri.EscapeDataString(q)}&fields=files(id)&spaces=drive"), ct);
        var existente = JsonNode.Parse(achou)?["files"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
        if (existente is not null) return existente;

        var criado = await ChamarAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/files?fields=id")
        {
            Content = new StringContent(new JsonObject { ["name"] = nome, ["mimeType"] = "application/vnd.google-apps.folder" }.ToJsonString(), Encoding.UTF8, "application/json")
        }, ct);
        return JsonNode.Parse(criado)?["id"]?.GetValue<string>() ?? throw new RegraDeNegocioException("O Google Drive não devolveu a pasta criada.");
    }

    /// <summary>Envia o arquivo (upload retomável, de uma vez só) e devolve o id no Drive.</summary>
    public async Task<string> EnviarArquivoAsync(string pastaId, string caminho, CancellationToken ct = default)
    {
        var info = new FileInfo(caminho);
        if (!info.Exists) throw new RegraDeNegocioException("O arquivo de backup não foi encontrado.");

        var metadados = new JsonObject { ["name"] = info.Name, ["parents"] = new JsonArray(pastaId) }.ToJsonString();
        string? local = null;
        await ChamarAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Upload}?uploadType=resumable&fields=id")
        {
            Content = ConteudoJson(metadados)
        }, ct, resposta => local = resposta.Headers.Location?.ToString());
        if (local is null) throw new RegraDeNegocioException("O Google Drive não aceitou iniciar o envio.");

        await using var fluxo = File.OpenRead(caminho);
        var corpo = await ChamarAsync(() =>
        {
            fluxo.Position = 0;
            var p = new HttpRequestMessage(HttpMethod.Put, local) { Content = new StreamContent(fluxo) };
            p.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            p.Content.Headers.ContentLength = info.Length;
            return p;
        }, ct);
        return JsonNode.Parse(corpo)?["id"]?.GetValue<string>() ?? "";
    }

    private static StringContent ConteudoJson(string json)
    {
        var c = new StringContent(json, Encoding.UTF8, "application/json");
        return c;
    }

    private async Task<string> ChamarAsync(Func<HttpRequestMessage> criar, CancellationToken ct,
        Action<HttpResponseMessage>? aoResponder = null)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            using var pedido = criar();
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.ObterAcessoAsync(ct));
            if (pedido.RequestUri!.AbsolutePath.StartsWith("/upload/") && pedido.Method == HttpMethod.Post)
                pedido.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "application/octet-stream");
            try
            {
                using var resposta = await _http.SendAsync(pedido, ct);
                var texto = await resposta.Content.ReadAsStringAsync(ct);
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 1) { _auth.Invalidar(); continue; }
                if (!resposta.IsSuccessStatusCode) throw Traduzir(resposta.StatusCode, texto);
                aoResponder?.Invoke(resposta);
                return texto;
            }
            catch (HttpRequestException) { throw new RegraDeNegocioException("Sem conexão com a internet (não foi possível falar com o Google Drive)."); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new RegraDeNegocioException("O Google Drive demorou demais para responder."); }
        }
    }

    internal static RegraDeNegocioException Traduzir(HttpStatusCode status, string corpo)
    {
        var motivo = "";
        try { motivo = JsonNode.Parse(corpo)?["error"]?["errors"]?[0]?["reason"]?.GetValue<string>() ?? ""; } catch (JsonException) { }
        return status switch
        {
            HttpStatusCode.Unauthorized => new("O Google recusou o acesso. Conecte de novo."),
            HttpStatusCode.Forbidden when motivo is "accessNotConfigured" or "SERVICE_DISABLED" =>
                new("A API do Google Drive não está ativada no projeto do Google Cloud. Ative \"Google Drive API\" e tente de novo."),
            HttpStatusCode.Forbidden when motivo is "storageQuotaExceeded" => new("O Google Drive está sem espaço."),
            HttpStatusCode.Forbidden => new("O Google Drive negou a permissão."),
            HttpStatusCode.TooManyRequests => new("O Google Drive recusou por excesso de pedidos. Tente mais tarde."),
            _ when (int)status >= 500 => new("O Google Drive está com problema no momento. Tente mais tarde."),
            _ => new($"O Google Drive respondeu com um erro inesperado (código {(int)status}).")
        };
    }
}

/// <summary>O que a tela usa: credenciais, conexão e envio do backup para a pasta "Nexus LS Backups" do Drive.</summary>
public class GoogleDriveBackup
{
    public const string PastaPadrao = "Nexus LS Backups";
    private readonly GoogleStore _store;
    private readonly GoogleOAuth _auth;
    private readonly DriveRest _drive;

    public GoogleDriveBackup(GoogleStore store, GoogleOAuth auth, DriveRest drive)
    {
        _store = store;
        _auth = auth;
        _drive = drive;
    }

    public ConfiguracaoGoogle Configuracao => _store.Carregar();

    public string Situacao()
    {
        var c = _store.Carregar();
        return c.Conectado ? "Conectado ao Google Drive." : c.TemCredenciais ? "Credenciais salvas. Falta conectar." : "Ainda não configurado.";
    }

    /// <summary>Guarda o Client ID e (se informado) o Client Secret. Trocar as credenciais desfaz a conexão.</summary>
    public void SalvarCredenciais(string clientId, string? segredoNovo)
    {
        clientId = (clientId ?? "").Trim();
        if (clientId.Length < 10 || !clientId.Contains('.'))
            throw new RegraDeNegocioException("O Client ID parece errado. Ele termina com .apps.googleusercontent.com.");
        var c = _store.Carregar();
        var mudou = c.ClientId != clientId || !string.IsNullOrEmpty(segredoNovo);
        c.ClientId = clientId;
        if (!string.IsNullOrEmpty(segredoNovo)) c.ClientSecretProtegido = _store.Proteger(segredoNovo.Trim());
        if (string.IsNullOrEmpty(c.ClientSecretProtegido)) throw new RegraDeNegocioException("Informe o Client Secret do Google.");
        if (mudou) c.RefreshTokenProtegido = null;
        _store.Salvar(c);
        _auth.Invalidar();
    }

    public Task ConectarAsync(Action<string> abrirNavegador, CancellationToken ct = default) => _auth.ConectarAsync(abrirNavegador, ct: ct);
    public void Desconectar() => _auth.Desconectar();

    /// <summary>Envia o arquivo de backup e devolve o nome da pasta onde ficou.</summary>
    public async Task<string> EnviarBackupAsync(string caminho, CancellationToken ct = default)
    {
        if (!_store.Carregar().Conectado) throw new RegraDeNegocioException("O Google não está conectado. Conecte primeiro.");
        var pasta = string.IsNullOrWhiteSpace(_store.Carregar().NomeDaPasta) ? PastaPadrao : _store.Carregar().NomeDaPasta!;
        var id = await _drive.ObterOuCriarPastaAsync(pasta, ct);
        await _drive.EnviarArquivoAsync(id, caminho, ct);
        return pasta;
    }
}
