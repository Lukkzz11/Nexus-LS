using System.Text;

namespace NexusLS.Core.Services;

/// <summary>Mensagens prontas para o cliente (cobrança por WhatsApp). Só monta texto e link: não envia nada.</summary>
public static class MensagensCliente
{
    public const int MaximoCaracteres = 600;

    public const string CobrancaPadrao =
        "Olá, {cliente}! Aqui é da {loja}. Passando para lembrar que há {vencido} em aberto e vencido na sua conta (vencimento em {vencimento}). Qualquer dúvida é só responder aqui. Obrigado!";

    public static readonly string[] Campos = { "{cliente}", "{loja}", "{valor}", "{vencido}", "{vencimento}" };

    /// <summary>Troca os campos do modelo pelos dados. Modelo vazio usa o padrão. {cliente} vira só o primeiro nome.</summary>
    public static string Montar(string? modelo, string cliente, string loja, long devidoCentavos, long vencidoCentavos, DateTime? vencimento)
    {
        var texto = string.IsNullOrWhiteSpace(modelo) ? CobrancaPadrao : modelo;
        var primeiro = (cliente ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return texto
            .Replace("{cliente}", primeiro, StringComparison.OrdinalIgnoreCase)
            .Replace("{loja}", loja, StringComparison.OrdinalIgnoreCase)
            .Replace("{valor}", Dinheiro.Formatar(devidoCentavos), StringComparison.OrdinalIgnoreCase)
            .Replace("{vencido}", Dinheiro.Formatar(vencidoCentavos), StringComparison.OrdinalIgnoreCase)
            .Replace("{vencimento}", vencimento is DateTime d ? d.ToString("dd/MM/yyyy") : "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Link do WhatsApp com a mensagem pronta (o cliente do WhatsApp só abre; quem envia é você). Nulo se o telefone não serve.</summary>
    public static string? LinkWhatsApp(string? telefone, string mensagem)
    {
        var digitos = new string((telefone ?? "").Where(char.IsDigit).ToArray()).TrimStart('0');
        if (digitos.Length is 10 or 11) digitos = "55" + digitos;
        if (!(digitos.Length is 12 or 13 && digitos.StartsWith("55"))) return null;
        return $"https://wa.me/{digitos}?text={Uri.EscapeDataString(mensagem)}";
    }
}
