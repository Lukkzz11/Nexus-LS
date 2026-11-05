using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Financeiro;

namespace NexusLS.Core.Services;

/// <summary>
/// DRE do período para os Relatórios. Os números vêm do <see cref="PosicaoFinanceiraService"/> (que alimenta o
/// <see cref="MotorFinanceiro"/>); daqui só se acrescentam dados descritivos das vendas (quantidade, descontos, formas).
/// </summary>
public class DemonstrativoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public DemonstrativoService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<DemonstrativoFinanceiro> MontarAsync(DateTime de, DateTime ate)
    {
        if (ate.Date < de.Date) throw new RegraDeNegocioException("A data final não pode ser antes da inicial.");

        var posicao = await new PosicaoFinanceiraService(_fabrica, _agora).CalcularAsync(de, ate);
        var vendas = await new RelatorioService(_fabrica).VendasPorPeriodoAsync(de, ate);

        // Peça vendida sem custo (nem guardado na venda, nem no produto): o resultado sai antes do custo dela.
        var inicio = de.Date;
        var fim = ate.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var itens = await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => !v.Cancelada && v.DataHora >= inicio && v.DataHora < fim)
            .SelectMany(v => v.Itens).ToListAsync();
        var custosAtuais = await db.Produtos.AsNoTracking().Select(p => new { p.Id, p.PrecoCustoCentavos })
            .ToDictionaryAsync(p => p.Id, p => p.PrecoCustoCentavos);
        var semCusto = itens.Where(i => (i.CustoUnitarioCentavos ?? custosAtuais.GetValueOrDefault(i.ProdutoId)) <= 0).Sum(i => i.Quantidade);

        var porForma = vendas.PorForma;
        return Demonstrativo.Montar(posicao.Periodo, posicao.Avisos, vendas.QuantidadeVendas, vendas.DescontosCentavos, semCusto, porForma);
    }
}
