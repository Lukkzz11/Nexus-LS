using System.IO;
using System.Net;
using System.Net.Http;
using NexusLS.App.ViewModels;
using NexusLS.Core.Google;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class AgendaGoogleTelaTests : IDisposable
{
    private sealed class ProtetorSimples : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + t;
        public string? Desproteger(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    private sealed class Servidor : HttpMessageHandler
    {
        public List<string> Metodos { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Metodos.Add(request.Method.Method + " " + request.RequestUri!.AbsolutePath);
            var corpo = request.RequestUri.Host.Contains("oauth2") ? "{\"access_token\":\"a\",\"expires_in\":3599}" : "{\"id\":\"ev-1\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpo) });
        }
    }

    private static readonly DateTime Hoje = new(2026, 11, 2);
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-agg-" + Guid.NewGuid().ToString("N"));

    public AgendaGoogleTelaTests() => Directory.CreateDirectory(_pasta);
    public void Dispose() { _banco.Dispose(); try { Directory.Delete(_pasta, true); } catch (IOException) { } }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    [Fact]
    public async Task Envia_o_dia_guarda_o_id_do_evento_e_reenviar_atualiza_sem_duplicar()
    {
        var store = new GoogleStore(Path.Combine(_pasta, "google.json"), new ProtetorSimples());
        store.Salvar(new ConfiguracaoGoogle { ClientId = "123456-abc.apps.googleusercontent.com", ClientSecretProtegido = store.Proteger("s"), RefreshTokenProtegido = store.Proteger("r") });
        var servidor = new Servidor();
        var http = new HttpClient(servidor);
        var google = new AgendaGoogle(store, new CalendarRest(http, new GoogleOAuth(http, store)));
        var agenda = new AgendaService(_banco.Fabrica, () => Hoje.AddHours(8));
        var criado = await agenda.CriarAsync(new DadosCompromisso("Reunião", Hoje.AddHours(14)));

        var vm = new AgendaViewModel(agenda, () => Hoje, google);
        await Esperar(() => vm.TituloDoDia.Contains("(hoje)"));
        Assert.True(vm.TemGoogle);

        await vm.EnviarDiaAoGoogleCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("1 criado", vm.Mensagem);
        Assert.Equal("ev-1", (await agenda.DoDiaAsync(Hoje)).Single().GoogleEventoId);

        await Esperar(() => vm.Compromissos.Count == 1 && vm.Compromissos[0].Origem.GoogleEventoId == "ev-1");
        await vm.EnviarDiaAoGoogleCommand.ExecuteAsync(null);
        Assert.Contains("1 atualizado", vm.Mensagem);
        Assert.Contains(servidor.Metodos, m => m.StartsWith("PUT") && m.EndsWith("/events/ev-1"));

        vm.Confirmar = _ => true;
        await vm.RemoverCommand.ExecuteAsync(vm.Compromissos[0]);
        Assert.Contains(servidor.Metodos, m => m.StartsWith("DELETE") && m.EndsWith("/events/ev-1"));
        Assert.Empty(await agenda.DoDiaAsync(Hoje));
    }

    [Fact]
    public async Task Sem_google_a_tela_funciona_e_o_botao_fica_oculto()
    {
        var vm = new AgendaViewModel(new AgendaService(_banco.Fabrica, () => Hoje.AddHours(8)), () => Hoje);
        await Esperar(() => vm.TituloDoDia.Contains("(hoje)"));
        Assert.False(vm.TemGoogle);
        await vm.EnviarDiaAoGoogleCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
    }
}
