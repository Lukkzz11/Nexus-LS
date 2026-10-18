using System.Globalization;
using System.Text.RegularExpressions;

namespace NexusLS.Core.Services;

/// <summary>
/// Converte entre centavos (long, o que vai para o banco) e texto em reais (o que o operador vê e digita).
/// Fica no Core, e não na tela, para poder ser testado sem abrir janela.
/// </summary>
public static class Dinheiro
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>1250 -> "R$ 12,50"</summary>
    public static string Formatar(long centavos) => (centavos / 100m).ToString("C", PtBr);

    /// <summary>1250 -> "12,50" (sem símbolo, para preencher um campo de edição)</summary>
    public static string FormatarParaEdicao(long centavos) => (centavos / 100m).ToString("N2", PtBr);

    /// <summary>
    /// Aceita "12,50", "12.50", "12", "R$ 1.250,00". Recusa negativos, texto inválido
    /// e mais de 2 casas decimais (não existe meio centavo).
    /// </summary>
    public static bool TentarConverter(string? texto, out long centavos)
    {
        centavos = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var s = texto.Replace("R$", "").Replace(' ', ' ').Replace(" ", "");

        // "12.5" ou "12.50" (ponto com 1 ou 2 dígitos no fim) é decimal; "1.250" é milhar.
        if (!s.Contains(',') && Regex.IsMatch(s, @"^\d+\.\d{1,2}$"))
            s = s.Replace('.', ',');

        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, PtBr, out var valor))
            return false;
        if (valor > 9_999_999.99m) return false;

        var emCentavos = valor * 100m;
        if (emCentavos != decimal.Truncate(emCentavos)) return false;

        centavos = (long)emCentavos;
        return true;
    }
}
