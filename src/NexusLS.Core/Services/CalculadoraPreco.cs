namespace NexusLS.Core.Services;

/// <summary>
/// Contas de preço. Margem = lucro sobre o PREÇO de venda (preço R$ 100, custo R$ 60 → margem 40%).
/// Markup = lucro sobre o CUSTO (mesmo exemplo → markup 66,67%). São coisas diferentes, e misturar as duas é erro comum.
/// </summary>
public static class CalculadoraPreco
{
    /// <summary>Preço que dá a margem desejada (0 a menos de 100%) sobre o custo.</summary>
    public static long PrecoPorMargem(long custoCentavos, decimal margemPercentual)
    {
        if (margemPercentual < 0 || margemPercentual >= 100)
            throw new RegraDeNegocioException("A margem deve ficar entre 0% e 99,99%.");
        return Arredondar(custoCentavos / (1 - margemPercentual / 100m));
    }

    /// <summary>Preço que dá o markup desejado sobre o custo.</summary>
    public static long PrecoPorMarkup(long custoCentavos, decimal markupPercentual)
    {
        if (markupPercentual < 0)
            throw new RegraDeNegocioException("O markup não pode ser negativo.");
        return Arredondar(custoCentavos * (1 + markupPercentual / 100m));
    }

    /// <summary>Margem em % do preço de venda; nulo se o preço é zero.</summary>
    public static decimal? Margem(long precoCentavos, long custoCentavos) =>
        precoCentavos <= 0 ? null : Math.Round((precoCentavos - custoCentavos) * 100m / precoCentavos, 2);

    /// <summary>Markup em % do custo; nulo se o custo é zero.</summary>
    public static decimal? Markup(long precoCentavos, long custoCentavos) =>
        custoCentavos <= 0 ? null : Math.Round((precoCentavos - custoCentavos) * 100m / custoCentavos, 2);

    private static long Arredondar(decimal centavos) => (long)Math.Round(centavos, MidpointRounding.AwayFromZero);
}
