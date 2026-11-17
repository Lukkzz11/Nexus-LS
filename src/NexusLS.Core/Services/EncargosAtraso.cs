using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Multa e juros de um conjunto de títulos vencidos. É só um cálculo para mostrar o que cobrar: a dívida em si não muda.</summary>
public record EncargosDoAtraso(long MultaCentavos, long JurosCentavos)
{
    public long TotalCentavos => MultaCentavos + JurosCentavos;
}

public static class EncargosAtraso
{
    public const int DiasNoMes = 30;

    /// <summary>
    /// Multa (uma vez, sobre o saldo vencido) e juros simples (por dia de atraso, à taxa mensal dividida por 30).
    /// Título em dia ou quitado não gera nada. O arredondamento é por título, para a soma bater com a conta de cada um.
    /// </summary>
    public static EncargosDoAtraso Calcular(IEnumerable<TituloAReceber> titulos, DateTime agora, decimal multaPercent, decimal jurosMesPercent)
    {
        long multa = 0, juros = 0;
        foreach (var t in titulos.Where(t => t.VencidoEm(agora)))
        {
            var dias = (agora.Date - t.Vencimento.Date).Days;
            multa += (long)Math.Round(t.SaldoCentavos * multaPercent / 100m, MidpointRounding.AwayFromZero);
            juros += (long)Math.Round(t.SaldoCentavos * (jurosMesPercent / 100m) * dias / DiasNoMes, MidpointRounding.AwayFromZero);
        }
        return new EncargosDoAtraso(multa, juros);
    }
}
