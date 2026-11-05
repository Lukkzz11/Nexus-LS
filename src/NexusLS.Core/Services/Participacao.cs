namespace NexusLS.Core.Services;

/// <summary>Uma fatia de gráfico de pizza: o rótulo, o valor em centavos e a parte do total (0 a 1).</summary>
public record FatiaPizza(string Rotulo, long ValorCentavos, double Fracao)
{
    public double Percentual => Fracao * 100;
}

/// <summary>
/// Participação de cada item no total, para os gráficos de pizza do Mais vendidos. Como no ERP: os maiores aparecem
/// separados e o resto vira "Outros". Só entra valor positivo; sem nada para mostrar, a lista vem vazia.
/// </summary>
public static class Participacao
{
    public const string RotuloOutros = "Outros";

    public static List<FatiaPizza> Fatias(IEnumerable<(string Rotulo, long ValorCentavos)> itens, int maximoSeparadas = 6)
    {
        if (maximoSeparadas < 1) throw new ArgumentOutOfRangeException(nameof(maximoSeparadas));

        // mesmo rótulo soma; maior primeiro, desempate pelo nome (resultado sempre igual)
        var ordenados = itens.Where(i => i.ValorCentavos > 0)
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Rotulo) ? "Sem nome" : i.Rotulo.Trim())
            .Select(g => (Rotulo: g.Key, Valor: g.Sum(i => i.ValorCentavos)))
            .OrderByDescending(i => i.Valor).ThenBy(i => i.Rotulo, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var total = ordenados.Sum(i => i.Valor);
        if (total <= 0) return new();

        var fatias = ordenados.Take(maximoSeparadas).ToList();
        var resto = ordenados.Skip(maximoSeparadas).Sum(i => i.Valor);
        if (resto > 0) fatias.Add((RotuloOutros, resto));

        return fatias.Select(f => new FatiaPizza(f.Rotulo, f.Valor, (double)f.Valor / total)).ToList();
    }

    /// <summary>
    /// Desconto da venda rateado entre os itens, proporcional ao valor de cada um (o resto dos centavos vai para o maior item),
    /// para a soma dos itens líquidos ser exatamente (soma dos itens − desconto). Devolve o valor líquido de cada item.
    /// </summary>
    public static long[] LiquidarItens(IReadOnlyList<long> totaisCentavos, long descontoCentavos)
    {
        var liquidos = totaisCentavos.ToArray();
        var soma = totaisCentavos.Sum();
        if (descontoCentavos <= 0 || soma <= 0 || liquidos.Length == 0) return liquidos;

        var desconto = Math.Min(descontoCentavos, soma);
        var partes = totaisCentavos.Select(t => desconto * t / soma).ToArray();   // arredonda para baixo
        var maior = Array.IndexOf(totaisCentavos.ToArray(), totaisCentavos.Max());
        partes[maior] += desconto - partes.Sum();
        for (var i = 0; i < liquidos.Length; i++) liquidos[i] -= partes[i];
        return liquidos;
    }
}
