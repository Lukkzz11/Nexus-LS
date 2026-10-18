using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Uma venda a prazo vista como "título a receber".</summary>
public record TituloAReceber(int VendaId, DateTime DataVenda, DateTime Vencimento, long TotalCentavos, long PagoCentavos)
{
    public long SaldoCentavos => TotalCentavos - PagoCentavos;
    public bool Quitado => SaldoCentavos <= 0;
    public bool VencidoEm(DateTime agora) => !Quitado && agora.Date > Vencimento.Date;
}

/// <summary>A dívida de um cliente: títulos (mais antigo primeiro) e o quanto já foi pago.</summary>
public record ContaCliente(IReadOnlyList<TituloAReceber> Titulos, long TotalPagoCentavos)
{
    public long SaldoDevedorCentavos => Titulos.Sum(t => t.SaldoCentavos);
}

/// <summary>
/// Cálculo da dívida. Cada pagamento abate primeiro a venda a prazo mais antiga (FIFO) e o que sobra vai para a seguinte.
/// Não há tabela de "baixas": o resultado é sempre recalculado das vendas e dos pagamentos, então nunca fica fora de sintonia.
/// </summary>
public static class ContaCorrente
{
    public const int PrazoEmDias = 30;

    public static ContaCliente Calcular(IEnumerable<Venda> vendas, IEnumerable<PagamentoCliente> pagamentos)
    {
        var restante = pagamentos.Sum(p => p.ValorCentavos);
        var titulos = new List<TituloAReceber>();

        foreach (var v in vendas
                     .Where(v => !v.Cancelada && v.FormaPagamento == FormaPagamento.APrazo)
                     .OrderBy(v => v.DataHora).ThenBy(v => v.Id))
        {
            var pago = Math.Min(restante, v.TotalCentavos);
            restante -= pago;
            titulos.Add(new TituloAReceber(v.Id, v.DataHora, v.DataHora.AddDays(PrazoEmDias), v.TotalCentavos, pago));
        }
        return new ContaCliente(titulos, pagamentos.Sum(p => p.ValorCentavos));
    }
}
