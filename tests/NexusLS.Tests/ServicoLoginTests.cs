using System.Net;
using System.Text;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Tests;

public sealed class ServicoLoginTests : IDisposable
{
    private sealed class ProtetorFalso : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + new string(t.Reverse().ToArray());
        public string? Desproteger(string t) => t.StartsWith("P:") ? new string(t[2..].Reverse().ToArray()) : null;
    }

    private sealed class Manipulador : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string, HttpResponseMessage> Resposta { get; set; } = (_, _) => new(HttpStatusCode.OK);
        public List<string> Pedidos { get; } = new();
        public List<string> Corpos { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var corpo = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            Pedidos.Add(r.RequestUri!.AbsolutePath);
            Corpos.Add(corpo);
            return Resposta(r, corpo);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode s, string json) =>
        new(s) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string LoginOk = "{\"localId\":\"uid-ana\",\"email\":\"ana@loja.com\",\"idToken\":\"id1\",\"refreshToken\":\"ref1\",\"expiresIn\":\"3600\"}";
    private const string RenovaOk = "{\"id_token\":\"id2\",\"refresh_token\":\"ref2\",\"expires_in\":\"3600\"}";

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-login-" + Guid.NewGuid().ToString("N"));
    private readonly Manipulador _http = new();
    private DateTime _agora = new(2026, 10, 22, 9, 0, 0);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private string Arquivo => Path.Combine(_pasta, "sessao.json");
    private ServicoLogin Criar() => new(new HttpClient(_http), "chave", new SessaoStore(Arquivo), new ProtetorFalso(), () => _agora);

    [Fact]
    public async Task Login_correto_guarda_sessao_sem_senha_e_com_token_protegido()
    {
        _http.Resposta = (_, _) => Json(HttpStatusCode.OK, LoginOk);
        var s = await Criar().EntrarAsync("ana@loja.com", "segredo123");

        Assert.Equal("uid-ana", s.Uid);
        var disco = File.ReadAllText(Arquivo);
        Assert.DoesNotContain("segredo123", disco);
        Assert.DoesNotContain("ref1", disco); // o refresh token não fica em texto puro
        Assert.Contains("P:", disco);
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"INVALID_LOGIN_CREDENTIALS\"}}", "incorretos")]
    [InlineData("{\"error\":{\"message\":\"USER_DISABLED\"}}", "desativado")]
    [InlineData("{\"error\":{\"message\":\"TOO_MANY_ATTEMPTS_TRY_LATER\"}}", "tentativas")]
    public async Task Erros_de_credencial_viram_mensagem_clara_e_nao_criam_sessao(string corpo, string trecho)
    {
        _http.Resposta = (_, _) => Json(HttpStatusCode.BadRequest, corpo);
        var ex = await Assert.ThrowsAsync<FalhaLoginException>(() => Criar().EntrarAsync("a@b.com", "x"));
        Assert.Contains(trecho, ex.Message);
        Assert.Equal(TipoFalhaLogin.Credenciais, ex.Tipo);
        Assert.False(File.Exists(Arquivo));
    }

    [Fact]
    public async Task Login_sem_internet_diz_que_e_problema_de_conexao()
    {
        _http.Resposta = (_, _) => throw new HttpRequestException("sem rede");
        var ex = await Assert.ThrowsAsync<FalhaLoginException>(() => Criar().EntrarAsync("a@b.com", "x"));
        Assert.Equal(TipoFalhaLogin.Conexao, ex.Tipo);
    }

    [Fact]
    public async Task Restaurar_com_internet_renova_o_token_e_atualiza_a_validacao()
    {
        _http.Resposta = (_, _) => Json(HttpStatusCode.OK, LoginOk);
        await Criar().EntrarAsync("ana@loja.com", "segredo123");

        _agora = _agora.AddDays(3);
        _http.Resposta = (_, corpo) => { Assert.Contains("ref1", corpo); return Json(HttpStatusCode.OK, RenovaOk); };
        var novo = Criar();
        var s = await novo.RestaurarAsync();

        Assert.Equal("uid-ana", s!.Uid);
        Assert.False(novo.Offline);
        Assert.Equal(_agora, s.UltimaValidacaoOnline);
        Assert.Equal("id2", await novo.ObterAsync());
    }

    [Fact]
    public async Task Restaurar_sem_internet_dentro_do_prazo_mantem_o_usuario_offline()
    {
        _http.Resposta = (_, _) => Json(HttpStatusCode.OK, LoginOk);
        await Criar().EntrarAsync("ana@loja.com", "segredo123");

        _agora = _agora.AddDays(5);
        _http.Resposta = (_, _) => throw new HttpRequestException("sem rede");
        var novo = Criar();
        var s = await novo.RestaurarAsync();

        Assert.NotNull(s);
        Assert.True(novo.Offline);
        Assert.Equal("uid-ana", novo.Uid);
        // E o sincronizador recebe uma falha de conexão (reagenda), não uma exceção desconhecida.
        await Assert.ThrowsAsync<FalhaDestinoException>(() => novo.ObterAsync());
    }

    [Fact]
    public async Task Restaurar_sem_internet_depois_do_prazo_exige_novo_login()
    {
        _http.Resposta = (_, _) => Json(HttpStatusCode.OK, LoginOk);
        await Criar().EntrarAsync("ana@loja.com", "segredo123");

        _agora = _agora.AddDays(30);
        _http.Resposta = (_, _) => throw new HttpRequestException("sem rede");
        var ex = await Assert.ThrowsAsync<FalhaLoginException>(() => Criar().RestaurarAsync());
        Assert.Equal(TipoFalhaLogin.SessaoExpirada, ex.Tipo);
        Assert.False(File.Exists(Arquivo));
    }

    [Fact]
    public async Task Sessao_revogada_pelo_servidor_limpa_o_login_local()
    {
        _http.Resposta = (_, _) => Json(HttpStatusCode.OK, LoginOk);
        await Criar().EntrarAsync("ana@loja.com", "segredo123");

        _http.Resposta = (_, _) => Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"TOKEN_EXPIRED\"}}");
        Assert.Null(await Criar().RestaurarAsync());
        Assert.False(File.Exists(Arquivo));
    }

    [Fact]
    public async Task Sair_apaga_a_sessao_e_o_token_deixa_de_ser_entregue()
    {
        _http.Resposta = (_, _) => Json(HttpStatusCode.OK, LoginOk);
        var login = Criar();
        await login.EntrarAsync("ana@loja.com", "segredo123");

        login.Sair();

        Assert.Null(login.Uid);
        Assert.False(File.Exists(Arquivo));
        await Assert.ThrowsAsync<FalhaDestinoException>(() => login.ObterAsync());
    }
}
