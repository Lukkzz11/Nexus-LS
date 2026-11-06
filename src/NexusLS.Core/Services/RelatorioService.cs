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

/// <summary>Faturamento, custo e lucro de uma categoria de produto (itens vendidos, antes do desconto da venda).</summary>
public record ResumoCategoria(string Categoria, int Quantidade, long FaturamentoCentavos, long CustoCentavos)
{
    public long LucroCentavos => FaturamentoCentavos - CustoCentavos;
}

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
    IReadOnlyList<ResumoDia> PorDia,
    long CustoTotalCentavos = 0,
    bool CustoEstimado = false,
    IReadOnlyList<ResumoCategoria>? PorCategoria = null)
{
    /// <summary>Total vendido (já com desconto) menos o custo das mercadorias. Não desconta taxas, impostos nem despesas.</summary>
    public long LucroBrutoCentavos => TotalCentavos - CustoTotalCentavos;
}

/// <summary>Valores líquidos por produto, categoria e forma de pagamento (cada lista soma <see cref="TotalLiquidoCentavos"/>).</summary>
public record ParticipacoesVendas(
    IReadOnlyList<(string Rotulo, long ValorCentavos)> Produtos,
    IReadOnlyList<(string Rotulo, long ValorCentavos)> Categorias,
    IReadOnlyList<(string Rotulo, long ValorCentavos)> Formas,
    long ItensBrutoCentavos, long DescontosCentavos)
{
    public long TotalLiquidoCentavos => ItensBrutoCentavos - DescontosCentavos;
}

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

        // Custo: o guardado na venda; em vendas antigas (sem custo guardado) usa o custo de hoje do produto, e marca como estimativa.
        var idsProdutos = validas.SelectMany(v => v.Itens).Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.AsNoTracking().Where(p => idsProdutos.Contains(p.Id))
            .Select(p => new { p.Id, p.PrecoCustoCentavos, p.Categoria }).ToDictionaryAsync(p => p.Id);
        var estimado = false;
        var linhasCusto = new List<(string Categoria, int Quantidade, long Faturamento, long Custo)>();
        foreach (var item in validas.SelectMany(v => v.Itens))
        {
            produtos.TryGetValue(item.ProdutoId, out var produto);
            long unitario;
            if (item.CustoUnitarioCentavos is long guardado) unitario = guardado;
            else { unitario = produto?.PrecoCustoCentavos ?? 0; estimado = true; }
            linhasCusto.Add((produto?.Categoria ?? SemCategoria, item.Quantidade, item.TotalCentavos, unitario * item.Quantidade));
        }
        var porCategoria = linhasCusto
            .GroupBy(l => l.Categoria)
            .Select(g => new ResumoCategoria(g.Key, g.Sum(l => l.Quantidade), g.Sum(l => l.Faturamento), g.Sum(l => l.Custo)))
            .OrderByDescending(c => c.FaturamentoCentavos).ThenBy(c => c.Categoria)
            .ToList();

        return new RelatorioVendas(
            inicio, fim.AddDays(-1), vendas,
            validas.Count, vendas.Count - validas.Count,
            total, validas.Sum(v => v.DescontoCentavos), ticket,
            ResumoPorForma(vendas), porDia,
            linhasCusto.Sum(l => l.Custo), estimado, porCategoria);
    }

    /// <summary>
    /// Base das três pizzas do Mais vendidos: o faturamento LÍQUIDO dos itens (preço × quantidade, com o desconto da venda
    /// rateado). O acréscimo do cartão parcelado não entra (não é faturamento de produto). Vendas canceladas ficam de fora.
    /// As três listas somam o mesmo total.
    /// </summary>
    public async Task<ParticipacoesVendas> ParticipacoesAsync(DateTime de, DateTime ate)
    {
        var (inicio, fim) = Periodo(de, ate);
        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => !v.Cancelada && v.DataHora >= inicio && v.DataHora < fim)
            .OrderBy(v => v.Id).ToListAsync();
        var categorias = await db.Produtos.AsNoTracking().Select(p => new { p.Id, p.Categoria }).ToDictionaryAsync(p => p.Id, p => p.Categoria);

        var produtos = new Dictionary<int, (string Nome, long Valor)>();
        var porCategoria = new Dictionary<string, long>();
        var porForma = new Dictionary<FormaPagamento, long>();
        long bruto = 0, descontos = 0;
        foreach (var v in vendas)
        {
            var itens = v.Itens.OrderBy(i => i.Id).ToList();
            var liquidos = Participacao.LiquidarItens(itens.Select(i => i.TotalCentavos).ToList(), v.DescontoCentavos);
            bruto += itens.Sum(i => i.TotalCentavos);
            descontos += itens.Sum(i => i.TotalCentavos) - liquidos.Sum();
            for (var k = 0; k < itens.Count; k++)
            {
                var item = itens[k];
                produtos[item.ProdutoId] = (item.NomeProduto, produtos.GetValueOrDefault(item.ProdutoId).Valor + liquidos[k]);
                var categoria = categorias.GetValueOrDefault(item.ProdutoId) is { Length: > 0 } c ? c : SemCategoria;
                porCategoria[categoria] = porCategoria.GetValueOrDefault(categoria) + liquidos[k];
                porForma[v.FormaPagamento] = porForma.GetValueOrDefault(v.FormaPagamento) + liquidos[k];
            }
        }
        return new ParticipacoesVendas(
            produtos.Values.Select(p => (p.Nome, p.Valor)).ToList(),
            porCategoria.Select(c => (c.Key, c.Value)).ToList(),
            porForma.Select(f => (SelecaoPagamento.Nome(f.Key), f.Value)).ToList(),
            bruto, descontos);
    }

    /// <summary>
    /// Dados dos gráficos de Relatórios entre duas datas (as duas incluídas), com o período anterior do mesmo tamanho para
    /// comparar. Mesma base do Desempenho: itens líquidos (desconto e acréscimo rateados), canceladas fora; a soma de cada
    /// gráfico é o "Total vendido" de <see cref="VendasPorPeriodoAsync"/>.
    /// </summary>
    public async Task<GraficosRelatorio> GraficosAsync(DateTime de, DateTime ate)
    {
        var (inicio, fim) = Periodo(de, ate);
        var periodo = new DesempenhoVendas.Intervalo(inicio, fim.AddDays(-1));
        var servico = new DesempenhoService(_fabrica);
        var doPeriodo = await servico.LinhasAsync(periodo);
        var doAnterior = await servico.LinhasAsync(DesempenhoVendas.Anterior(periodo));
        return GraficosRelatorio.Montar(doPeriodo, doAnterior, periodo);
    }

    public const string SemCategoria = "Sem categoria";

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
