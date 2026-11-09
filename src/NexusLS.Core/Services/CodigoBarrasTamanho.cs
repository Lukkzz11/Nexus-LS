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
        return ordem < 1 ? baseCodigo : baseCodigo + ordem.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>Uma referência nova montada a partir de uma já existente: os dados copiados e o próximo número livre do mesmo modelo.</summary>
public sealed record ModeloDeReferencia(NexusLS.Core.Models.Produto Base, IReadOnlyList<string> Tamanhos, string NovaReferencia);
