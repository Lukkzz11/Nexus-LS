namespace NexusLS.Core.Services;

/// <summary>
/// Acréscimo do cartão de crédito, igual ao do ERP: 1x 5%, 2x 7%, 3x 9%, 4x 10%, somado ao valor depois do desconto.
/// Só existe no crédito; nas outras formas o acréscimo é zero.
/// </summary>
public static class TaxasCartao
{
    public static decimal Percentual(int parcelas) => parcelas switch
    {
        1 => 5m,
        2 => 7m,
        3 => 9m,
        4 => 10m,
        _ => throw new RegraDeNegocioException("O cartão de crédito vai de 1x a 4x.")
    };

    /// <summary>Acréscimo em centavos para um valor base (já com o desconto), arredondado ao centavo.</summary>
    public static long Acrescimo(long baseCentavos, int parcelas) =>
        (long)Math.Round(baseCentavos * Percentual(parcelas) / 100m, MidpointRounding.AwayFromZero);
}