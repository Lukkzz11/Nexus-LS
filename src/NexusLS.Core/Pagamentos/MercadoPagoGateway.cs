using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NexusLS.Core.Pagamentos;

/// <summary>
/// Pix pelo Mercado Pago (API de pagamentos). O token vem de fora (armazenamento protegido do Windows) e nunca fica no código.
/// A chave de idempotência vai no cabeçalho X-Idempotency-Key: repetir a chamada devolve a mesma cobrança.
/// </summary>
public sealed class MercadoPagoGateway : IGatewayPix
{
    private const string Base = "https://api.mercadopago.com";
    private readonly HttpClient _http;
    private readonly Func<string?> _token;
    private readonly Func<string> _emailPagador;
    private readonly Func<int> _validadeMinutos;

    public MercadoPagoGateway(HttpClient http, Func<string?> token, Func<string> emailPagador, Func<int>? validadeMinutos = null)
    {
        _http = http;
        _token = token;
        _emailPagador = emailPagador;
        _validadeMinutos = validadeMinutos ?? (() => 30);
    }

    public string Nome => "MercadoPago";

    private HttpRequestMessage Pedido(HttpMethod metodo, string caminho, string? chave = null, string? json = null)
    {
        var token = _token();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Cadastre o token do Mercado Pago em Configurações de Pagamento.");
        var pedido = new HttpRequestMessage(metodo, Base + caminho);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (chave is not null) pedido.Headers.Add("X-Idempotency-Key", chave);
        if (json is not null) pedido.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return pedido;
    }

    public async Task<CobrancaPix> CriarAsync(long valorCentavos, string descricao, string chaveIdempotencia, CancellationToken ct = default)
    {
        var corpo = JsonSerializer.Serialize(new
        {
            transaction_amount = valorCentavos / 100m,
            description = descricao,
            payment_method_id = "pix",
            payer = new { email = _emailPagador() },
            date_of_expiration = DateTimeOffset.Now.AddMinutes(Math.Clamp(_validadeMinutos(), 5, 1440)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
        });
        using var pedido = Pedido(HttpMethod.Post, "/v1/payments", chaveIdempotencia, corpo);
        using var resposta = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
        var texto = await resposta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resposta.IsSuccessStatusCode)
            throw new HttpRequestException($"Mercado Pago recusou a cobrança ({(int)resposta.StatusCode}).");

        using var doc = JsonDocument.Parse(texto);
        var raiz = doc.RootElement;
        var dados = raiz.GetProperty("point_of_interaction").GetProperty("transaction_data");
        DateTime? expira = raiz.TryGetProperty("date_of_expiration", out var e) && e.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.LocalDateTime : null;
        return new CobrancaPix(Nome, raiz.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture), chaveIdempotencia, valorCentavos,
            dados.GetProperty("qr_code").GetString() ?? "",
            dados.TryGetProperty("qr_code_base64", out var b) ? b.GetString() : null,
            Traduzir(raiz.GetProperty("status").GetString()), expira);
    }

    public async Task<StatusPix> ConsultarAsync(string idExterno, CancellationToken ct = default)
    {
        using var pedido = Pedido(HttpMethod.Get, "/v1/payments/" + Uri.EscapeDataString(idExterno));
        using var resposta = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
        if (!resposta.IsSuccessStatusCode) throw new HttpRequestException($"Consulta recusada ({(int)resposta.StatusCode}).");
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return Traduzir(doc.RootElement.GetProperty("status").GetString());
    }

    /// <summary>Confere se o token é aceito pelo Mercado Pago (GET /users/me). Devolve a mensagem para a tela.</summary>
    public async Task<(bool Ok, string Mensagem)> TestarAsync(CancellationToken ct = default)
    {
        try
        {
            using var pedido = Pedido(HttpMethod.Get, "/users/me");
            using var resposta = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
            if (resposta.IsSuccessStatusCode) return (true, "Conexão com o Mercado Pago funcionando.");
            return (false, resposta.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "O Mercado Pago não aceitou o token. Confira se é o Access Token da aplicação."
                : $"O Mercado Pago respondeu com erro {(int)resposta.StatusCode}.");
        }
        catch (InvalidOperationException e) { return (false, e.Message); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return (false, "Sem conexão com o Mercado Pago."); }
    }

    /// <summary>Só "approved" é pago. Qualquer status desconhecido continua pendente (nunca aprova por engano).</summary>
    public static StatusPix Traduzir(string? status) => status switch
    {
        "approved" => StatusPix.Aprovado,
        "cancelled" => StatusPix.Cancelado,
        "rejected" => StatusPix.Cancelado,
        "expired" => StatusPix.Expirado,
        _ => StatusPix.Pendente,
    };
}
