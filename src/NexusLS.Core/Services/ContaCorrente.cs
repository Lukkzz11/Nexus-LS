using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Uma venda a prazo vista como "título a receber". <see cref="TotalCentavos"/> é o valor DEVIDO (o da venda, ou o do último
/// ajuste); <see cref="ValorOriginalCentavos"/> é o total da venda, que nunca muda.
/// </summary>
public record TituloAReceber(int VendaId, DateTime DataVenda, DateTime Vencimento, long TotalCentavos, long PagoCentavos,
    long ValorOriginalCentavos = 0, bool Ajustado = false)
{
    public long SaldoCentavos => TotalCentavos - PagoCentavos;
    public bool Quitado => SaldoCentavos <= 0;
    public bool VencidoEm(DateTime agora) => !Quitado && agora.Date > Vencimento.Date;

    /// <summary>Quanto o ajuste mexeu no valor (negativo = abatimento, positivo = acréscimo).</summary>
    public long AjusteCentavos => ValorOriginalCentavos == 0 ? 0 : TotalCentavos - ValorOriginalCentavos;
}

/// <summary>A dívida de um cliente: títulos (mais antigo primeiro) e o quanto já foi pago.</summary>
public record ContaCliente(IReadOnlyList<TituloAReceber> Titulos, long TotalPagoCentavos)
{
    public long SaldoDevedorCentavos => Titulos.Sum(t => t.SaldoCentavos);
}

/// <summary>
/// Cálculo da dívida. Cada pagamento abate primeiro a venda a prazo mais antiga (FIFO) e o que sobra vai para a seguinte.
/// Não há tabela de "baixas": o resultado é sempre recalculado das vendas, dos ajustes de título e dos pagamentos, então
/// nunca fica fora de sintonia. Toda tela e serviço que fala em dívida usa este cálculo (e só ele).
/// </summary>
public static class ContaCorrente
{
    public const int PrazoEmDias = 30;

    /// <param name="ajustes">Os ajustes de título das vendas (o mais recente de cada venda vale). Obrigatório de propósito: quem
    /// calcula a dívida sem olhar os ajustes mostraria um número diferente do resto do programa.</param>
    public static ContaCliente Calcular(IEnumerable<Venda> vendas, IEnumerable<PagamentoCliente> pagamentos,
        IEnumerable<AjusteTituloPrazo> ajustes)
    {
        var restante = pagamentos.Sum(p => p.ValorCentavos);
        var vigentes = UltimoPorVenda(ajustes);
        var titulos = new List<TituloAReceber>();

        foreach (var v in vendas
                     .Where(v => !v.Cancelada && v.FormaPagamento == FormaPagamento.APrazo)
                     .OrderBy(v => v.DataHora).ThenBy(v => v.Id))
        {
            var devido = v.TotalCentavos;
            var vencimento = v.DataHora.AddDays(PrazoEmDias);
            var ajustado = false;
            if (vigentes.TryGetValue(v.Id, out var ajuste))
            {
                devido = ajuste.ValorNovoCentavos;
                vencimento = ajuste.VencimentoNovo;
                ajustado = true;
            }
            var pago = Math.Min(restante, devido);
            restante -= pago;
            titulos.Add(new TituloAReceber(v.Id, v.DataHora, vencimento, devido, pago, v.TotalCentavos, ajustado));
        }
        return new ContaCliente(titulos, pagamentos.Sum(p => p.ValorCentavos));
    }

    /// <summary>O ajuste mais recente de cada venda (o que vale agora).</summary>
    public static Dictionary<int, AjusteTituloPrazo> UltimoPorVenda(IEnumerable<AjusteTituloPrazo> ajustes) => ajustes
        .GroupBy(a => a.VendaId)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CriadoEm).ThenByDescending(a => a.Id).First());

    /// <summary>
    /// Quanto os ajustes de título mexeram na receita das vendas dadas (negativo = abatimento, positivo = acréscimo):
    /// valor devido atual menos o total da venda. Só conta venda a prazo não cancelada.
    /// </summary>
    public static long AjustesNaReceita(IEnumerable<Venda> vendas, IEnumerable<AjusteTituloPrazo> ajustes)
    {
        var vigentes = UltimoPorVenda(ajustes);
        return vendas.Where(v => !v.Cancelada && v.FormaPagamento == FormaPagamento.APrazo)
            .Sum(v => vigentes.TryGetValue(v.Id, out var a) ? a.ValorNovoCentavos - v.TotalCentavos : 0);
    }
}
