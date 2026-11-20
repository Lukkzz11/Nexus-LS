using System.Globalization;
using System.Text;
using System.Text.Json;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Comunicacao;

/// <summary>Um produto na mensagem de ofertas: nome, preço normal e (opcional) preço promocional.</summary>
public sealed record OfertaWhatsApp(string Nome, long PrecoCentavos, long? PrecoPromocionalCentavos = null, string? Tamanhos = null);

/// <summary>Preferências do WhatsApp (arquivo JSON ao lado do banco, por isso entra no backup completo).</summary>
public class ConfiguracaoWhatsApp
{
    /// <summary>Link público do catálogo, que vai no fim da mensagem de ofertas.</summary>
    public string LinkCatalogo { get; set; } = "";

    /// <summary>Nomes dos grupos para os quais a loja manda ofertas (exatamente como aparecem no WhatsApp).</summary>
    public List<string> Grupos { get; set; } = new();

    /// <summary>Tenta apertar "Enviar" sozinho. Desligado: o programa só prepara a mensagem e a pessoa clica.</summary>
    public bool EnviarAutomatico { get; set; } = true;

    /// <summary>Intervalo aleatório entre uma mensagem e outra num envio em lote (segundos), para não parecer robô.</summary>
    public int IntervaloMinimoSegundos { get; set; } = 8;
    public int IntervaloMaximoSegundos { get; set; } = 20;
}

public class ConfiguracaoWhatsAppStore
{
    private readonly string _caminho;

    public ConfiguracaoWhatsAppStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "whatsapp.json");

    public ConfiguracaoWhatsApp Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<ConfiguracaoWhatsApp>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Salvar(ConfiguracaoWhatsApp c)
    {
        c.Grupos = c.Grupos.Select(g => g.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        c.IntervaloMinimoSegundos = Math.Clamp(c.IntervaloMinimoSegundos, 3, 600);
        c.IntervaloMaximoSegundos = Math.Clamp(c.IntervaloMaximoSegundos, c.IntervaloMinimoSegundos, 900);
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}

/// <summary>Regras puras do WhatsApp: telefone, link de conversa e texto das mensagens. Sem custo e sem API: usa o WhatsApp Web.</summary>
public static class RegrasWhatsApp
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Só os números, com o 55 do Brasil na frente. Nulo se não parece um celular ou telefone válido.</summary>
    public static string? NormalizarTelefone(string? texto)
    {
        var d = new string((texto ?? "").Where(char.IsAsciiDigit).ToArray());
        if (d.StartsWith("00")) d = d[2..];
        if (d.Length is 10 or 11) d = "55" + d;          // DDD + número
        if (!d.StartsWith("55") || d.Length is < 12 or > 13) return null;
        return d;
    }

    /// <summary>Endereço que abre a conversa no WhatsApp Web já com o texto escrito.</summary>
    public static string LinkConversa(string telefoneNormalizado, string? texto = null) =>
        $"https://web.whatsapp.com/send?phone={telefoneNormalizado}" +
        (string.IsNullOrEmpty(texto) ? "" : "&text=" + Uri.EscapeDataString(texto));

    /// <summary>Acima disso a mensagem não cabe com segurança no endereço: o programa cola o texto na conversa.</summary>
    public const int LimiteTextoNoLink = 1500;

    public static string MensagemOfertas(IEnumerable<OfertaWhatsApp> ofertas, string nomeLoja, string? linkCatalogo, string? abertura = null)
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(abertura) ? $"🔥 *Ofertas {nomeLoja}* 🔥" : abertura!.Trim()).Append("\n\n");
        foreach (var o in ofertas)
        {
            sb.Append("• *").Append(o.Nome).Append('*');
            if (!string.IsNullOrWhiteSpace(o.Tamanhos)) sb.Append(" (").Append(o.Tamanhos).Append(')');
            sb.Append('\n');
            if (o.PrecoPromocionalCentavos is > 0 && o.PrecoPromocionalCentavos < o.PrecoCentavos)
                sb.Append("   de ~").Append(Dinheiro.Formatar(o.PrecoCentavos)).Append("~ por *").Append(Dinheiro.Formatar(o.PrecoPromocionalCentavos.Value)).Append("*\n");
            else
                sb.Append("   ").Append(Dinheiro.Formatar(o.PrecoCentavos)).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(linkCatalogo)) sb.Append("\n🛍️ Veja o catálogo completo: ").Append(linkCatalogo!.Trim()).Append('\n');
        sb.Append("\nPeças com pronta-entrega, enquanto durar o estoque!");
        return sb.ToString();
    }

    public static string MensagemComprovante(Venda venda, string nomeLoja)
    {
        var nome = venda.Cliente?.Nome;
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(nome) ? "Olá!" : $"Olá, {nome.Trim().Split(' ')[0]}!").Append('\n');
        sb.Append($"Segue o comprovante da sua compra nº {venda.Id} na *{nomeLoja}*, em {venda.DataHora:dd/MM/yyyy}.\n");
        sb.Append("Total: *").Append(Dinheiro.Formatar(venda.TotalCentavos)).Append("*\n\nObrigado pela preferência! 💙");
        return sb.ToString();
    }

    public static string MensagemDocumento(string nomeArquivo, string nomeLoja) =>
        $"Olá! Segue o documento *{nomeArquivo}* da *{nomeLoja}*.";
}
