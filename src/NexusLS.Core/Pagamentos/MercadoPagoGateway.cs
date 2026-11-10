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

    public MercadoPagoGateway(HttpClient http, Func<string?> token, Func<string> emailPagador)
    {
        _http = http;
        _token = token;
        _emailPagador = emailPagador;
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
