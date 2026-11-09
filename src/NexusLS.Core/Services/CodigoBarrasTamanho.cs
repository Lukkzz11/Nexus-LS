using System.Globalization;

namespace NexusLS.Core.Services;

/// <summary>
/// Código de barras de um tamanho = código da referência + número do tamanho (ref 5555, tamanho 1 → 55551).
/// O produto sem tamanho (ordem 0) usa só o código da referência.
/// </summary>
public static class CodigoBarrasTamanho
{
    public static string? Montar(string? codigoDoProduto, int ordem)
    {
        var baseCodigo = codigoDoProduto?.Trim();
        if (string.IsNullOrEmpty(baseCodigo)) return null;
        return ordem < 1 || !UsaNumeroDoTamanho(baseCodigo) ? baseCodigo : baseCodigo + ordem.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Um EAN-13 (13 dígitos, de fabricante ou interno do Nexus) identifica o produto inteiro e não leva número de tamanho:
    /// colar um dígito nele geraria um código inválido. A regra vale para os códigos curtos de referência (5555 → 55551).
    /// </summary>
    public static bool UsaNumeroDoTamanho(string? codigoDoProduto)
    {
        var c = codigoDoProduto?.Trim();
        return !string.IsNullOrEmpty(c) && !(c.Length == 13 && c.All(char.IsAsciiDigit));
    }
}

/// <summary>Uma referência nova montada a partir de uma já existente: os dados copiados e o próximo número livre do mesmo modelo.</summary>
public sealed record ModeloDeReferencia(NexusLS.Core.Models.Produto Base, IReadOnlyList<string> Tamanhos, string NovaReferencia);
