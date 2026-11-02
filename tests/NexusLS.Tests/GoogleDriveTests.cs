using System.Net;
using NexusLS.Core.Google;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Tests;

public sealed class GoogleDriveTests : IDisposable
{
    private sealed class ProtetorSimples : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + t;
        public string? Desproteger(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "google-" + Guid.NewGuid().ToString("N"));
    private readonly GoogleStore _store;

    public GoogleDriveTests()
    {
        Directory.CreateDirectory(_pasta);
        _store = new GoogleStore(Path.Combine(_pasta, "google.json"), new ProtetorSimples());
    }

    public void Dispose() => Directory.Delete(_pasta, true);

    private GoogleDriveBackup Fachada(ServidorFalso servidor, Func<DateTime>? agora = null)
    {
        var http = new HttpClient(servidor);
        var auth = new GoogleOAuth(http, _store, agora);
        return new GoogleDriveBackup(_store, auth, new DriveRest(http, auth));
    }

    private static string IdValido => "123456-abcdef.apps.googleusercontent.com";

    [Fact]
    public void Credenciais_ficam_protegidas_e_trocar_desfaz_a_conexao()
    {
        var g = Fachada(new ServidorFalso());
        Assert.Equal("Ainda não configurado.", g.Situacao());
        Assert.Throws<RegraDeNegocioException>(() => g.SalvarCredenciais("curto", "segredo"));
        Assert.Throws<RegraDeNegocioException>(() => g.SalvarCredenciais(IdValido, null));      // sem segredo ainda

        g.SalvarCredenciais(IdValido, "segredo-1");
        Assert.Equal("Credenciais salvas. Falta conectar.", g.Situacao());
        Assert.DoesNotContain("segredo-1", File.ReadAllText(Path.Combine(_pasta, "google.json")).Replace("P:segredo-1", ""));
        Assert.Equal("segredo-1", _store.ClientSecret());

        var c = _store.Carregar();
        c.RefreshTokenProtegido = _store.Proteger("refresh");
        _store.Salvar(c);
        Assert.Equal("Conectado ao Google Drive.", g.Situacao());
        g.SalvarCredenciais(IdValido, null);                                                     // nada mudou: continua conectado
        Assert.Equal("Conectado ao Google Drive.", g.Situacao());
        g.SalvarCredenciais(IdValido, "segredo-2");                                              // segredo novo: precisa reconectar
        Assert.Equal("Credenciais salvas. Falta conectar.", g.Situacao());
    }

    [Fact]
    public async Task Conectar_abre_o_navegador_valida_o_retorno_e_guarda_o_refresh_token()
    {
        var servidor = new ServidorFalso().Responde(HttpStatusCode.OK, "{\"access_token\":\"acesso-1\",\"expires_in\":3599,\"refresh_token\":\"refresh-1\"}");
        var g = Fachada(servidor);
        g.SalvarCredenciais(IdValido, "segredo-1");

        string? urlAberta = null;
        await g.ConectarAsync(url =>
        {
            urlAberta = url;
            var u = new Uri(url);
            var q = System.Web.HttpUtility.ParseQueryString(u.Query);
            var redirect = q["redirect_uri"]!;
            _ = Task.Run(async () =>
            {
                using var navegador = new HttpClient();
                await navegador.GetAsync(redirect + "?code=codigo-x&state=errado");                        // retorno falso é ignorado
                await navegador.GetAsync(redirect + "?code=codigo-x&state=" + q["state"]);
            });
        });

        Assert.Contains("code_challenge_method=S256", urlAberta);
        Assert.Contains("scope=" + Uri.EscapeDataString("https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/calendar.events"), urlAberta);
        Assert.Equal("Conectado ao Google Drive.", g.Situacao());
        Assert.Equal("refresh-1", _store.RefreshToken());
        var troca = Assert.Single(servidor.Pedidos);
        Assert.Contains("grant_type=authorization_code", troca.Corpo);
        Assert.Contains("code=codigo-x", troca.Corpo);
        Assert.Contains("code_verifier=", troca.Corpo);
    }

    [Fact]
    public async Task Conectar_recusado_pelo_usuario_ou_sem_credenciais_explica_o_que_fazer()
    {
        var g = Fachada(new ServidorFalso());
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => g.ConectarAsync(_ => { }));        // sem credenciais

        g.SalvarCredenciais(IdValido, "segredo-1");
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => g.ConectarAsync(url =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
            _ = Task.Run(async () => await new HttpClient().GetAsync(q["redirect_uri"] + "?error=access_denied&state=" + q["state"]));
        }));
        Assert.Contains("não autorizou", ex.Message);
        Assert.Equal("Credenciais salvas. Falta conectar.", g.Situacao());
    }

    [Fact]
    public async Task Envia_o_backup_cria_a_pasta_uma_vez_e_renova_o_token_vencido()
    {
        var c = new ConfiguracaoGoogle { ClientId = IdValido, ClientSecretProtegido = _store.Proteger("s"), RefreshTokenProtegido = _store.Proteger("refresh-1") };
        _store.Salvar(c);
        var arquivo = Path.Combine(_pasta, "backup.db");
        File.WriteAllBytes(arquivo, new byte[] { 1, 2, 3, 4, 5 });

        var servidor = new ServidorFalso();
        servidor.Responde(HttpStatusCode.OK, "{\"access_token\":\"acesso-1\",\"expires_in\":3599}");          // renovação
        servidor.Responde(HttpStatusCode.OK, "{\"files\":[]}");                                                // procura a pasta: não há
        servidor.Responde(HttpStatusCode.OK, "{\"id\":\"pasta-1\"}");                                          // cria a pasta
        servidor.Regra = p =>                                                                                  // sessão de upload
            p.Url.Contains("uploadType=resumable") ? Resposta(HttpStatusCode.OK, "{}", "https://upload.exemplo/sessao-1") : null;
        servidor.Responde(HttpStatusCode.OK, "{\"id\":\"arquivo-1\"}");                                        // o arquivo
        var g = Fachada(servidor);

        var pasta = await g.EnviarBackupAsync(arquivo);
        Assert.Equal("Nexus LS Backups", pasta);

        var pedidos = servidor.Pedidos;
        Assert.Contains("grant_type=refresh_token", pedidos[0].Corpo);
        Assert.Equal("Bearer acesso-1", pedidos[1].Autorizacao);
        Assert.Contains("mimeType", pedidos[2].Corpo);
        Assert.Contains("pasta-1", pedidos[3].Corpo);                                                          // o arquivo vai para a pasta criada
        Assert.Contains("backup.db", pedidos[3].Corpo);
        Assert.Equal("PUT", pedidos[4].Metodo.Method);
        Assert.Equal("https://upload.exemplo/sessao-1", pedidos[4].Url);
    }

    private static HttpResponseMessage Resposta(HttpStatusCode status, string corpo, string local)
    {
        var r = ServidorFalso.Json(status, corpo);
        r.Headers.Location = new Uri(local);
        return r;
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"errors\":[{\"reason\":\"accessNotConfigured\"}]}}", "não está ativada")]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"errors\":[{\"reason\":\"storageQuotaExceeded\"}]}}", "sem espaço")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", "excesso de pedidos")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "problema no momento")]
    public async Task Erros_do_drive_viram_mensagens_em_portugues(HttpStatusCode status, string corpo, string trecho)
    {
        _store.Salvar(new ConfiguracaoGoogle { ClientId = IdValido, ClientSecretProtegido = _store.Proteger("s"), RefreshTokenProtegido = _store.Proteger("r") });
        var servidor = new ServidorFalso().Responde(HttpStatusCode.OK, "{\"access_token\":\"a\",\"expires_in\":3599}").Responde(status, corpo);
        var arquivo = Path.Combine(_pasta, "b.db");
        File.WriteAllBytes(arquivo, new byte[] { 1 });
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => Fachada(servidor).EnviarBackupAsync(arquivo));
        Assert.Contains(trecho, ex.Message);
    }

    [Fact]
    public async Task Permissao_vencida_pede_para_conectar_de_novo_e_sem_conexao_nao_tenta()
    {
        var g = Fachada(new ServidorFalso());
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => g.EnviarBackupAsync("x.db"));       // nem conectado

        _store.Salvar(new ConfiguracaoGoogle { ClientId = IdValido, ClientSecretProtegido = _store.Proteger("s"), RefreshTokenProtegido = _store.Proteger("r") });
        var servidor = new ServidorFalso().Responde(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => Fachada(servidor).EnviarBackupAsync(Path.Combine(_pasta, "b.db")));
        Assert.Contains("Conecte de novo", ex.Message);
    }

    [Fact]
    public void Pkce_gera_desafio_s256_do_verificador()
    {
        var (verificador, desafio) = GoogleOAuth.NovoPkce();
        Assert.True(verificador.Length >= 43);
        var esperado = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verificador)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(esperado, desafio);
    }
}
