using System.Net;
using System.Text;
using NexusLS.Core.Models;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Tests;

public sealed class GestorLoginTests : IDisposable
{
    private sealed class Protetor : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + t;
        public string? Desproteger(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    private sealed class Http : HttpMessageHandler
    {
        public List<(string Url, string Auth)> Pedidos { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Pedidos.Add((r.RequestUri!.ToString(), r.Headers.Authorization?.Parameter ?? ""));
            var url = r.RequestUri!.AbsoluteUri;
            var corpo = url.Contains("signInWithPassword")
                ? "{\"localId\":\"uid-ana\",\"email\":\"ana@loja.com\",\"idToken\":\"TOKEN-ANA\",\"refreshToken\":\"r1\",\"expiresIn\":\"3600\"}"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
        }
    }

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-gestor-" + Guid.NewGuid().ToString("N"));
    private readonly Http _handler = new();

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private (GestorLogin Login, ConfiguracaoFirebaseStore Config) Criar(bool configurar = true)
    {
        var protetor = new Protetor();
        var config = new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), protetor);
        if (configurar) config.Salvar(new ConfiguracaoFirebase { ProjectId = "minha-loja-1a2b3", ApiKey = "chave", Email = "licenca@loja.com" }, "senha-licenca");
        var login = new GestorLogin(config, new HttpClient(_handler), new SessaoStore(Path.Combine(_pasta, "sessao.json")), protetor);
        return (login, config);
    }

    [Fact]
    public async Task Sem_configuracao_nao_ha_login_e_restaurar_devolve_nulo()
    {
        var (login, _) = Criar(configurar: false);
        Assert.False(login.Configurado);
        Assert.Null(await login.RestaurarAsync());
        await Assert.ThrowsAsync<FalhaLoginException>(() => login.EntrarAsync("a@b.com", "x"));
    }

    [Fact]
    public async Task Entrar_define_o_uid_e_o_token_da_sessao_vai_para_o_envio()
    {
        var (login, _) = Criar();
        Assert.Equal("licenca@loja.com", login.EmailSugerido);

        await login.EntrarAsync("ana@loja.com", "segredo");

        Assert.Equal("uid-ana", login.Uid);
        Assert.Equal("TOKEN-ANA", await login.ObterAsync());
    }

    [Fact]
    public async Task Destino_da_fila_usa_a_licenca_sem_usuario_e_o_token_do_usuario_depois_de_entrar()
    {
        var (login, config) = Criar();
        var legado = new DestinoFalso();
        var destino = new DestinoFilaFirestore(login, config, new HttpClient(_handler), legado);
        Assert.True(destino.PodeEnviar); // licença completa

        legado.FalhaNaVerificacao = new FalhaDestinoException("VIA-LEGADO");
        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.VerificarAsync());
        Assert.Equal("VIA-LEGADO", ex.Message); // sem usuário, a chamada foi para o destino da licença

        await login.EntrarAsync("ana@loja.com", "segredo");
        _handler.Pedidos.Clear();
        try { await destino.VerificarAsync(); } catch (FalhaDestinoException) { /* a resposta de mentira não é um Firestore */ }

        Assert.Contains(_handler.Pedidos, p => p.Auth == "TOKEN-ANA" && p.Url.Contains("minha-loja-1a2b3"));
    }

    [Fact]
    public void Sem_licenca_e_sem_usuario_nao_pode_enviar()
    {
        var protetor = new Protetor();
        var config = new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), protetor);
        config.Salvar(new ConfiguracaoFirebase { ProjectId = "minha-loja-1a2b3", ApiKey = "chave", Email = "x@loja.com" }, null);
        var login = new GestorLogin(config, new HttpClient(_handler), new SessaoStore(Path.Combine(_pasta, "sessao.json")), protetor);

        Assert.True(login.Configurado);
        Assert.False(new DestinoFilaFirestore(login, config, new HttpClient(_handler), new DestinoFalso()).PodeEnviar);
    }
}
