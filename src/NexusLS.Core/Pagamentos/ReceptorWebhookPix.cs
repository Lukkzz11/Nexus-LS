using System.Net;
using System.Text.Json;

namespace NexusLS.Core.Pagamentos;

/// <summary>
/// Ouve os avisos do Mercado Pago ("o pagamento X mudou"). Só atende a rota /webhook/mercadopago, em 127.0.0.1: para o aviso
/// chegar, um túnel (Cloudflare) aponta a URL pública para esta porta. O corpo do aviso só diz QUAL pagamento mudou; a situação
/// real é sempre consultada na API do gateway com o token (por isso um aviso falso não consegue marcar nada como pago).
/// </summary>
public sealed class ReceptorWebhookPix : IDisposable
{
    public const string Rota = "/webhook/mercadopago";
    private const int LimiteCorpo = 16 * 1024;

    private readonly ServicoPix _pix;
    private readonly HttpListener _escuta = new();
    private CancellationTokenSource? _parar;

    public ReceptorWebhookPix(ServicoPix pix, int porta)
    {
        _pix = pix;
        Porta = porta;
        _escuta.Prefixes.Add($"http://127.0.0.1:{porta}/");
    }

    public int Porta { get; }
    public bool Ativo => _escuta.IsListening;

    /// <summary>Começa a ouvir. Devolve falso (sem lançar) se a porta já está em uso: o Pix continua funcionando pela consulta periódica.</summary>
    public bool Iniciar()
    {
        if (Ativo) return true;
        try { _escuta.Start(); }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { return false; }
        _parar = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_parar.Token));
        return true;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _escuta.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _escuta.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) { return; }
            _ = Task.Run(() => AtenderAsync(ctx, ct), ct);
        }
    }

    private async Task AtenderAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var req = ctx.Request;
            if (!string.Equals(req.Url?.AbsolutePath.TrimEnd('/'), Rota, StringComparison.OrdinalIgnoreCase)) { Responder(ctx, 404); return; }
            if (req.HttpMethod == "GET") { Responder(ctx, 200); return; }          // teste de alcance do túnel
            if (req.HttpMethod != "POST") { Responder(ctx, 405); return; }
            if (req.ContentLength64 > LimiteCorpo) { Responder(ctx, 413); return; }

            var corpo = await LerCorpoAsync(req.InputStream, ct).ConfigureAwait(false);
            var id = ExtrairId(corpo, req.QueryString["data.id"] ?? req.QueryString["id"]);
            Responder(ctx, 200);                                                    // responde logo: o gateway reenvia se demorar
            if (id is not null) await _pix.ConfirmarPorAvisoAsync("MercadoPago", id, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            try { Responder(ctx, 500); } catch (Exception) { /* conexão já fechada */ }
        }
    }

    private static async Task<string> LerCorpoAsync(Stream s, CancellationToken ct)
    {
        var buf = new byte[LimiteCorpo];
        var lido = 0;
        int n;
        while (lido < buf.Length && (n = await s.ReadAsync(buf.AsMemory(lido), ct).ConfigureAwait(false)) > 0) lido += n;
        return System.Text.Encoding.UTF8.GetString(buf, 0, lido);
    }

    /// <summary>Pega o id do pagamento do aviso ({"type":"payment","data":{"id":"123"}}) ou da URL. Só aceita dígitos.</summary>
    public static string? ExtrairId(string corpo, string? daUrl = null)
    {
        string? id = null;
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() != "payment") return null;
                if (doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var i))
                    id = i.ValueKind == JsonValueKind.Number ? i.GetRawText() : i.GetString();
            }
        }
        catch (JsonException) { /* sem corpo JSON: tenta pela URL */ }
        id ??= daUrl;
        return !string.IsNullOrEmpty(id) && id.Length <= 20 && id.All(char.IsAsciiDigit) ? id : null;
    }

    private static void Responder(HttpListenerContext ctx, int codigo)
    {
        ctx.Response.StatusCode = codigo;
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _parar?.Cancel();
        try { _escuta.Close(); } catch (Exception) { /* já fechado */ }
    }
}
