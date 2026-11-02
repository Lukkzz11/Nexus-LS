using System.Net;
using System.Text.Json.Nodes;
using NexusLS.Core.Google;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Tests;

public sealed class GoogleCalendarTests : IDisposable
{
    private sealed class ProtetorSimples : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + t;
        public string? Desproteger(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "gcal-" + Guid.NewGuid().ToString("N"));
    private readonly GoogleStore _store;

    public GoogleCalendarTests()
    {
        Directory.CreateDirectory(_pasta);
        _store = new GoogleStore(Path.Combine(_pasta, "google.json"), new ProtetorSimples());
    }

    public void Dispose() => Directory.Delete(_pasta, true);

    private AgendaGoogle Criar(ServidorFalso servidor, bool conectado = true)
    {
        if (conectado)
            _store.Salvar(new ConfiguracaoGoogle { ClientId = "123456-abc.apps.googleusercontent.com", ClientSecretProtegido = _store.Proteger("s"), RefreshTokenProtegido = _store.Proteger("r") });
        var http = new HttpClient(servidor);
        return new AgendaGoogle(_store, new CalendarRest(http, new GoogleOAuth(http, _store)));
    }

    private static ServidorFalso ComToken() => new ServidorFalso().Responde(HttpStatusCode.OK, "{\"access_token\":\"a\",\"expires_in\":3599}");

    private static Compromisso Reuniao(int id = 1, string? evento = null) => new()
    {
        Id = id, Titulo = "Reunião", Descricao = "Sala 2", Inicio = new DateTime(2026, 11, 5, 14, 30, 0), Fim = new DateTime(2026, 11, 5, 15, 30, 0), GoogleEventoId = evento
    };

    [Fact]
    public void Corpo_do_evento_usa_data_para_dia_inteiro_e_horario_com_fuso_nos_outros()
    {
        var inteiro = CalendarRest.CorpoDoEvento(new Compromisso { Titulo = "Feriado", Inicio = new DateTime(2026, 11, 15), DiaInteiro = true });
        Assert.Equal("2026-11-15", inteiro["start"]!["date"]!.GetValue<string>());
        Assert.Equal("2026-11-16", inteiro["end"]!["date"]!.GetValue<string>());      // o fim é exclusivo
        Assert.Null(inteiro["description"]);

        var comHora = CalendarRest.CorpoDoEvento(Reuniao());
        Assert.Matches(@"^2026-11-05T14:30:00[+-]\d\d:\d\d$", comHora["start"]!["dateTime"]!.GetValue<string>());
        Assert.Matches(@"^2026-11-05T15:30:00[+-]\d\d:\d\d$", comHora["end"]!["dateTime"]!.GetValue<string>());
        Assert.Equal("Sala 2", comHora["description"]!.GetValue<string>());

        var semFim = CalendarRest.CorpoDoEvento(new Compromisso { Titulo = "X", Inicio = new DateTime(2026, 11, 5, 9, 0, 0) });
        Assert.StartsWith("2026-11-05T10:00:00", semFim["end"]!["dateTime"]!.GetValue<string>());   // sem fim: 1 hora
    }

    [Fact]
    public async Task Cria_o_que_nunca_foi_enviado_e_atualiza_o_que_ja_tem_evento()
    {
        var servidor = ComToken();
        servidor.Responde(HttpStatusCode.OK, "{\"id\":\"ev-novo\"}");     // POST
        servidor.Responde(HttpStatusCode.OK, "{\"id\":\"ev-velho\"}");    // PUT
        var gravados = new Dictionary<int, string?>();

        var r = await Criar(servidor).EnviarAsync(new[] { Reuniao(1), Reuniao(2, "ev-velho") }, (id, e) => { gravados[id] = e; return Task.CompletedTask; });

        Assert.Equal(1, r.Criados);
        Assert.Equal(1, r.Atualizados);
        Assert.Equal("ev-novo", gravados[1]);
        Assert.False(gravados.ContainsKey(2));
        Assert.Equal("POST", servidor.Pedidos[1].Metodo.Method);
        Assert.Equal("Bearer a", servidor.Pedidos[1].Autorizacao);
        Assert.Equal("PUT", servidor.Pedidos[2].Metodo.Method);
        Assert.EndsWith("/events/ev-velho", servidor.Pedidos[2].Url);
        Assert.Equal("Reunião", JsonNode.Parse(servidor.Pedidos[1].Corpo)!["summary"]!.GetValue<string>());
    }

    [Fact]
    public async Task Evento_apagado_no_google_e_recriado_em_vez_de_falhar()
    {
        var servidor = ComToken();
        servidor.Responde(HttpStatusCode.NotFound, "{}");                  // PUT: não existe mais
        servidor.Responde(HttpStatusCode.OK, "{\"id\":\"ev-2\"}");         // POST
        string? gravado = null;

        var r = await Criar(servidor).EnviarAsync(new[] { Reuniao(1, "sumiu") }, (_, e) => { gravado = e; return Task.CompletedTask; });

        Assert.Equal(1, r.Criados);
        Assert.Equal("ev-2", gravado);
    }

    [Fact]
    public async Task Sem_conexao_ou_com_erro_do_google_explica_em_portugues()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            Criar(new ServidorFalso(), conectado: false).EnviarAsync(new[] { Reuniao() }, (_, _) => Task.CompletedTask));

        var desativada = ComToken();
        desativada.Responde(HttpStatusCode.Forbidden, "{\"error\":{\"errors\":[{\"reason\":\"accessNotConfigured\"}]}}");
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            Criar(desativada).EnviarAsync(new[] { Reuniao() }, (_, _) => Task.CompletedTask));
        Assert.Contains("Google Calendar API", ex.Message);

        var semEscopo = ComToken();
        semEscopo.Responde(HttpStatusCode.Forbidden, "{\"error\":{\"errors\":[{\"reason\":\"insufficientPermissions\"}]}}");
        ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            Criar(semEscopo).EnviarAsync(new[] { Reuniao() }, (_, _) => Task.CompletedTask));
        Assert.Contains("conecte de novo", ex.Message);
        Assert.DoesNotContain("Bearer", ex.Message);
    }

    [Fact]
    public async Task Excluir_apaga_o_evento_e_nunca_estoura_a_remocao_local()
    {
        var servidor = ComToken();
        servidor.Responde(HttpStatusCode.NoContent, "");
        Assert.Null(await Criar(servidor).ExcluirAsync("ev-1"));
        Assert.Equal("DELETE", servidor.Pedidos[1].Metodo.Method);

        Assert.Null(await Criar(new ServidorFalso()).ExcluirAsync(null));          // nunca enviado: nada a fazer

        var falha = ComToken();
        falha.Responde(HttpStatusCode.InternalServerError, "{}");
        var aviso = await Criar(falha).ExcluirAsync("ev-1");
        Assert.Contains("problema", aviso);                                        // vira aviso, não exceção
    }
}
