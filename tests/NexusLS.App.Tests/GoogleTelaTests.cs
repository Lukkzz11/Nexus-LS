using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Backup;
using NexusLS.Core.Google;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("OperadorAtual")]
public sealed class GoogleTelaTests : IDisposable
{
    private sealed class ProtetorSimples : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + t;
        public string? Desproteger(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    private sealed class Servidor : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        public Queue<(HttpStatusCode, string)> Respostas { get; } = new();
        public string? Local { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            var (status, corpo) = Respostas.Dequeue();
            var r = new HttpResponseMessage(status) { Content = new StringContent(corpo) };
            if (request.RequestUri.Query.Contains("resumable")) r.Headers.Location = new Uri("https://upload.exemplo/s1");
            return Task.FromResult(r);
        }
    }

    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-gtela-" + Guid.NewGuid().ToString("N"));

    public GoogleTelaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        _banco.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private (ConfiguracoesViewModel Vm, GoogleStore Store, Servidor Http) Criar()
    {
        var servidor = new Servidor();
        var http = new HttpClient(servidor);
        var store = new GoogleStore(Path.Combine(_pasta, "google.json"), new ProtetorSimples());
        var auth = new GoogleOAuth(http, store);
        var google = new GoogleDriveBackup(store, auth, new DriveRest(http, auth));

        var banco = Path.Combine(_pasta, "nexus.db");
        var opcoes = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<NexusLS.Core.Data.PdvDbContext>().UseSqlite($"Data Source={banco}").Options;
        using (var db = new NexusLS.Core.Data.PdvDbContext(opcoes)) db.Database.Migrate();
        var bk = new ConfiguracaoBackupStore(Path.Combine(_pasta, "backup.json"));
        bk.Salvar(new ConfiguracaoBackup { Pasta = Path.Combine(_pasta, "bk"), IntervaloHoras = 24, Manter = 5 });
        var gestor = new GestorBackup(new BackupService(banco), bk);

        var config = new ConfiguracaoService(_banco.Fabrica);
        var vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()), gestor);
        vm.UsarGoogle(google, _ => { });
        return (vm, store, servidor);
    }

    [Fact]
    public async Task Salvar_credenciais_valida_e_envio_sem_conexao_ou_sem_backup_explica()
    {
        var (vm, store, _) = Criar();
        Assert.True(vm.TemGoogle);
        Assert.Equal("Ainda não configurado.", vm.GoogleSituacao);

        vm.GoogleClientId = "x";
        await vm.SalvarGoogleCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemGoogleEhErro);
        Assert.Contains("Client ID", vm.MensagemGoogle);

        vm.GoogleClientId = "123456-abc.apps.googleusercontent.com";
        vm.GoogleSegredoNovo = "segredo";
        await vm.SalvarGoogleCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemGoogleEhErro, vm.MensagemGoogle);
        Assert.Equal("", vm.GoogleSegredoNovo);                                   // o segredo some da tela
        Assert.Equal("Credenciais salvas. Falta conectar.", vm.GoogleSituacao);

        await vm.EnviarBackupAoDriveCommand.ExecuteAsync(null);                    // ainda sem backup
        Assert.True(vm.MensagemGoogleEhErro);
        Assert.Contains("Ainda não há backup", vm.MensagemGoogle);
        Assert.Equal("segredo", store.ClientSecret());
    }

    [Fact]
    public async Task Envia_o_backup_mais_recente_ao_drive_e_avisa_a_pasta()
    {
        var (vm, store, http) = Criar();
        store.Salvar(new ConfiguracaoGoogle
        {
            ClientId = "123456-abc.apps.googleusercontent.com", ClientSecretProtegido = store.Proteger("s"), RefreshTokenProtegido = store.Proteger("r")
        });

        await vm.FazerBackupAgoraCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Backups);

        http.Respostas.Enqueue((HttpStatusCode.OK, "{\"access_token\":\"a\",\"expires_in\":3599}"));
        http.Respostas.Enqueue((HttpStatusCode.OK, "{\"files\":[{\"id\":\"pasta-1\"}]}"));
        http.Respostas.Enqueue((HttpStatusCode.OK, "{}"));
        http.Respostas.Enqueue((HttpStatusCode.OK, "{\"id\":\"arq-1\"}"));
        await vm.EnviarBackupAoDriveCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemGoogleEhErro, vm.MensagemGoogle);
        Assert.Contains("Nexus LS Backups", vm.MensagemGoogle);
        Assert.Equal("Conectado ao Google Drive.", vm.GoogleSituacao);

        vm.DesconectarGoogleCommand.Execute(null);
        Assert.Equal("Credenciais salvas. Falta conectar.", vm.GoogleSituacao);
    }
}
