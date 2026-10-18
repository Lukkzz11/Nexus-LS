using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Uma linha do ranking de mais vendidos.</summary>
/// <param name="Tamanho">Só preenchido quando o ranking é separado por tamanho.</param>
public record ProdutoMaisVendido(
    int Posicao, int ProdutoId, string Nome, string? Tamanho, int Quantidade, long FaturamentoCentavos, int NumeroDeVendas);

/// <summary>Quanto entrou por forma de pagamento (vendas canceladas não contam).</summary>
public record ResumoForma(FormaPagamento Forma, int Vendas, long TotalCentavos);

/// <summary>Quanto foi vendido num dia (vendas canceladas não contam).</summary>
public record ResumoDia(DateTime Dia, int Vendas, long TotalCentavos);

/// <summary>Tudo que o relatório de vendas por período mostra.</summary>
/// <param name="Vendas">Todas as vendas do período, inclusive as canceladas (marcadas), da mais antiga para a mais nova.</param>
public record RelatorioVendas(
    DateTime De,
    DateTime Ate,
    IReadOnlyList<Venda> Vendas,
    int QuantidadeVendas,
    int QuantidadeCanceladas,
    long TotalCentavos,
    long DescontosCentavos,
    long TicketMedioCentavos,
    IReadOnlyList<ResumoForma> PorForma,
    IReadOnlyList<ResumoDia> PorDia);

public class RelatorioService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public RelatorioService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>
    /// Ranking dos produtos mais vendidos entre duas datas (as duas incluídas), do que mais saiu para o que menos saiu.
    /// Vendas canceladas ficam de fora. Com <paramref name="porTamanho"/>, cada tamanho é uma linha.
    /// </summary>
    public async Task<List<ProdutoMaisVendido>> MaisVendidosAsync(
        DateTime de, DateTime ate, bool porTamanho = false, int? limite = null)
    {
        var (inicio, fim) = Periodo(de, ate);

        await using var db = await _fabrica.CreateDbContextAsync();
        var itens = await db.Vendas.AsNoTracking()
            .Where(v => !v.Cancelada && v.DataHora >= inicio && v.DataHora < fim)
            .SelectMany(v => v.Itens)
            .Select(i => new { i.Id, i.VendaId, i.ProdutoId, i.NomeProduto, i.Tamanho, i.Quantidade, i.TotalCentavos })
            .ToListAsync();

        var ranking = itens
            .GroupBy(i => (i.ProdutoId, Tamanho: porTamanho ? i.Tamanho : null))
            .Select(g => new
            {
                g.Key.ProdutoId,
                g.Key.Tamanho,
                Nome = g.OrderByDescending(i => i.Id).First().NomeProduto, // o nome mais recente do produto
                Quantidade = g.Sum(i => i.Quantidade),
                Faturamento = g.Sum(i => i.TotalCentavos),
                Vendas = g.Select(i => i.VendaId).Distinct().Count()
            })
            .OrderByDescending(x => x.Quantidade).ThenByDescending(x => x.Faturamento).ThenBy(x => x.Nome).ThenBy(x => x.Tamanho)
            .ToList();

        if (limite is int max) ranking = ranking.Take(max).ToList();

        return ranking
            .Select((x, i) => new ProdutoMaisVendido(i + 1, x.ProdutoId, x.Nome, x.Tamanho, x.Quantidade, x.Faturamento, x.Vendas))
            .ToList();
    }

    /// <summary>
    /// Relatório de vendas entre duas datas (as duas incluídas): totais, ticket médio, por forma de pagamento
    /// e por dia. Vendas canceladas aparecem na lista, mas não entram em nenhuma soma.
    /// </summary>
    public async Task<RelatorioVendas> VendasPorPeriodoAsync(DateTime de, DateTime ate)
    {
        var (inicio, fim) = Periodo(de, ate);

        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking()
            .Include(v => v.Itens).Include(v => v.Cliente)
            .Where(v => v.DataHora >= inicio && v.DataHora < fim)
            .OrderBy(v => v.DataHora).ThenBy(v => v.Id)
            .ToListAsync();

        var validas = vendas.Where(v => !v.Cancelada).ToList();
        var total = validas.Sum(v => v.TotalCentavos);
        var ticket = validas.Count == 0
            ? 0
            : (long)Math.Round(total / (decimal)validas.Count, MidpointRounding.AwayFromZero);

        var porDia = validas
            .GroupBy(v => v.DataHora.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ResumoDia(g.Key, g.Count(), g.Sum(v => v.TotalCentavos)))
            .ToList();

        return new RelatorioVendas(
            inicio, fim.AddDays(-1), vendas,
            validas.Count, vendas.Count - validas.Count,
            total, validas.Sum(v => v.DescontoCentavos), ticket,
            ResumoPorForma(vendas), porDia);
    }

    /// <summary>Total por forma de pagamento, na ordem de sempre (dinheiro, Pix, débito...). Só formas que tiveram venda.</summary>
    public static IReadOnlyList<ResumoForma> ResumoPorForma(IEnumerable<Venda> vendas) => vendas
        .Where(v => !v.Cancelada)
        .GroupBy(v => v.FormaPagamento)
        .OrderBy(g => Array.IndexOf(SelecaoPagamento.Formas, g.Key))
        .Select(g => new ResumoForma(g.Key, g.Count(), g.Sum(v => v.TotalCentavos)))
        .ToList();

    private static (DateTime inicio, DateTime fim) Periodo(DateTime de, DateTime ate)
    {
        var inicio = de.Date;
        var fim = ate.Date.AddDays(1);
        if (fim <= inicio)
            throw new RegraDeNegocioException("A data final não pode ser anterior à data inicial.");
        return (inicio, fim);
    }
}
