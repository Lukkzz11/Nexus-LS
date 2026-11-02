using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Google;

/// <summary>Cria, atualiza e apaga eventos na agenda principal do Google Calendar.</summary>
public class CalendarRest
{
    private const string Eventos = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    private readonly HttpClient _http;
    private readonly GoogleOAuth _auth;

    public CalendarRest(HttpClient http, GoogleOAuth auth)
    {
        _http = http;
        _auth = auth;
    }

    /// <summary>O evento como o Google espera. Dia inteiro usa só a data (o fim é exclusivo); os outros levam o fuso do computador no horário.</summary>
    public static JsonObject CorpoDoEvento(Compromisso c)
    {
        var corpo = new JsonObject { ["summary"] = c.Titulo };
        if (!string.IsNullOrWhiteSpace(c.Descricao)) corpo["description"] = c.Descricao;
        if (c.DiaInteiro)
        {
            corpo["start"] = new JsonObject { ["date"] = c.Inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            corpo["end"] = new JsonObject { ["date"] = c.Inicio.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }
        else
        {
            var fim = c.Fim is { } f && f > c.Inicio ? f : c.Inicio.AddHours(1);
            corpo["start"] = new JsonObject { ["dateTime"] = Momento(c.Inicio) };
            corpo["end"] = new JsonObject { ["dateTime"] = Momento(fim) };
        }
        return corpo;
    }

    private static string Momento(DateTime local)
    {
        var comTipo = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var deslocamento = TimeZoneInfo.Local.GetUtcOffset(comTipo);
        return new DateTimeOffset(comTipo, deslocamento).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    public async Task<string> InserirAsync(Compromisso c, CancellationToken ct = default) =>
        Id(await ChamarAsync(HttpMethod.Post, Eventos, CorpoDoEvento(c), ct));

    /// <summary>Atualiza o evento. Devolve falso se ele não existe mais no Google (foi apagado lá).</summary>
    public async Task<bool> AtualizarAsync(string eventoId, Compromisso c, CancellationToken ct = default)
    {
        try
        {
            await ChamarAsync(HttpMethod.Put, $"{Eventos}/{Uri.EscapeDataString(eventoId)}", CorpoDoEvento(c), ct);
            return true;
        }
        catch (EventoInexistente) { return false; }
    }

    public async Task ExcluirAsync(string eventoId, CancellationToken ct = default)
    {
        try { await ChamarAsync(HttpMethod.Delete, $"{Eventos}/{Uri.EscapeDataString(eventoId)}", null, ct); }
        catch (EventoInexistente) { /* já não estava lá: o resultado é o mesmo */ }
    }

    private sealed class EventoInexistente : Exception { }

    private static string Id(string json) =>
        JsonNode.Parse(json)?["id"]?.GetValue<string>() ?? throw new RegraDeNegocioException("O Google Agenda não devolveu o evento criado.");

    private async Task<string> ChamarAsync(HttpMethod metodo, string url, JsonObject? corpo, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            using var pedido = new HttpRequestMessage(metodo, url);
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.ObterAcessoAsync(ct));
            if (corpo is not null) pedido.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
            try
            {
                using var resposta = await _http.SendAsync(pedido, ct);
                var texto = await resposta.Content.ReadAsStringAsync(ct);
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 1) { _auth.Invalidar(); continue; }
                if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone && metodo != HttpMethod.Post) throw new EventoInexistente();
                if (!resposta.IsSuccessStatusCode) throw Traduzir(resposta.StatusCode, texto);
                return texto;
            }
            catch (HttpRequestException) { throw new RegraDeNegocioException("Sem conexão com a internet (não foi possível falar com o Google Agenda)."); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new RegraDeNegocioException("O Google Agenda demorou demais para responder."); }
        }
    }

    internal static RegraDeNegocioException Traduzir(HttpStatusCode status, string corpo)
    {
        var motivo = "";
        try { motivo = JsonNode.Parse(corpo)?["error"]?["errors"]?[0]?["reason"]?.GetValue<string>() ?? ""; } catch (JsonException) { }
        return status switch
        {
            HttpStatusCode.Unauthorized => new("O Google recusou o acesso. Conecte de novo em Configurações > Backup."),
            HttpStatusCode.Forbidden when motivo is "accessNotConfigured" or "SERVICE_DISABLED" =>
                new("A API do Google Calendar não está ativada no projeto do Google Cloud. Ative \"Google Calendar API\" e tente de novo."),
            HttpStatusCode.Forbidden when motivo is "insufficientPermissions" or "ACCESS_TOKEN_SCOPE_INSUFFICIENT" =>
                new("A conexão atual não permite usar o Google Agenda. Desconecte e conecte de novo em Configurações > Backup, aceitando o acesso à agenda."),
            HttpStatusCode.Forbidden => new("O Google Agenda negou a permissão."),
            HttpStatusCode.TooManyRequests => new("O Google Agenda recusou por excesso de pedidos. Tente mais tarde."),
            _ when (int)status >= 500 => new("O Google Agenda está com problema no momento. Tente mais tarde."),
            _ => new($"O Google Agenda respondeu com um erro inesperado (código {(int)status}).")
        };
    }
}

public record ResultadoEnvioAgenda(int Criados, int Atualizados);

/// <summary>Leva compromissos da agenda do Nexus LS para o Google Agenda, sem duplicar: o id do evento fica guardado no compromisso.</summary>
public class AgendaGoogle
{
    private readonly GoogleStore _store;
    private readonly CalendarRest _calendario;

    public AgendaGoogle(GoogleStore store, CalendarRest calendario)
    {
        _store = store;
        _calendario = calendario;
    }

    public bool Conectado => _store.Carregar().Conectado;

    /// <param name="gravarId">Guarda o id do evento no compromisso (nulo = limpa).</param>
    public async Task<ResultadoEnvioAgenda> EnviarAsync(IEnumerable<Compromisso> compromissos, Func<int, string?, Task> gravarId, CancellationToken ct = default)
    {
        if (!Conectado) throw new RegraDeNegocioException("O Google não está conectado. Conecte em Configurações > Backup.");
        int criados = 0, atualizados = 0;
        foreach (var c in compromissos)
        {
            if (!string.IsNullOrEmpty(c.GoogleEventoId) && await _calendario.AtualizarAsync(c.GoogleEventoId, c, ct)) { atualizados++; continue; }
            var id = await _calendario.InserirAsync(c, ct);
            await gravarId(c.Id, id);
            criados++;
        }
        return new ResultadoEnvioAgenda(criados, atualizados);
    }

    /// <summary>Apaga o evento do Google quando o compromisso é removido. Falhas aqui não impedem a remoção local.</summary>
    public async Task<string?> ExcluirAsync(string? eventoId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(eventoId) || !Conectado) return null;
        try { await _calendario.ExcluirAsync(eventoId, ct); return null; }
        catch (RegraDeNegocioException ex) { return ex.Message; }
    }
}
