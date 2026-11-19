using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Relatório de produtos: mais vendidos (com curva ABC), por categoria e por tamanho, e estoque parado. Funções puras
/// (testáveis); a leitura do banco fica em <see cref="RelatorioProdutosService"/>. Vendas canceladas ficam fora. Valores já
/// com o desconto/acréscimo da venda rateado. Margem só aparece quando o custo é conhecido em TODAS as peças do grupo
/// (melhor "sem dados" do que um número enganoso).
/// </summary>
public static class RelatorioProdutos
{
    public const int DiasParadoPadrao = 60;

    public sealed record ProdutoVendido(int Posicao, int ProdutoId, string Nome, string? Referencia, string Categoria, int Pecas,
        long ValorCentavos, int Vendas, decimal PercentualDoFaturamento, long? LucroCentavos, decimal? MargemPct, char ClasseAbc);

    public sealed record CategoriaVendida(string Categoria, int Pecas, long ValorCentavos, decimal PercentualDoFaturamento,
        long? LucroCentavos, decimal? MargemPct);

    /// <summary>Tamanho vendido: de um produto (<c>ProdutoId</c> preenchido) ou de todos (<c>ProdutoId</c> nulo).</summary>
    public sealed record TamanhoVendido(int? ProdutoId, string Produto, string Tamanho, int Pecas, long ValorCentavos);

    public sealed record ProdutoParado(int ProdutoId, string Nome, string? Referencia, string Categoria, int Estoque,
        DateTime? UltimaVenda, int? DiasSemVenda, long? CustoParadoCentavos);

    public sealed record Resultado(
        DesempenhoVendas.Intervalo Periodo, long FaturamentoCentavos, int Pecas,
        long? LucroCentavos, decimal? MargemPct,
        IReadOnlyList<ProdutoVendido> MaisVendidos,
        IReadOnlyList<CategoriaVendida> PorCategoria,
        IReadOnlyList<TamanhoVendido> PorTamanho,
        IReadOnlyList<TamanhoVendido> PorProdutoETamanho);

    private sealed record Linha(int ProdutoId, string Nome, string Tamanho, int Quantidade, long Valor, long? Custo, int VendaId);

    public static Resultado Gerar(IEnumerable<Venda> vendas, IEnumerable<Produto> produtos, DesempenhoVendas.Intervalo periodo)
    {
        var ini = periodo.Inicio.Date;
        var fim = periodo.Fim.Date;
        var cad = produtos.ToDictionary(p => p.Id);

        var linhas = new List<Linha>();
        foreach (var v in vendas.Where(v => !v.Cancelada && v.DataHora.Date >= ini && v.DataHora.Date <= fim))
        {
            var itens = v.Itens.Where(i => i.Quantidade > 0).ToList();
            var bruto = itens.Sum(i => i.TotalCentavos);
            if (itens.Count == 0 || bruto <= 0) continue;
            foreach (var (item, valor) in DesempenhoVendas.ValoresLiquidos(v, itens, bruto))
            {
                cad.TryGetValue(item.ProdutoId, out var p);
                long? custo = item.CustoUnitarioCentavos ?? (p is { PrecoCustoCentavos: > 0 } ? p.PrecoCustoCentavos : null);
                if (custo is <= 0) custo = null;
                linhas.Add(new Linha(item.ProdutoId, item.NomeProduto, item.Tamanho, item.Quantidade, valor, custo, v.Id));
            }
        }

        long total = linhas.Sum(l => l.Valor);
        var (lucroTotal, margemTotal) = Margem(linhas, total);

        // Mais vendidos, por quantidade e depois por valor; a classe ABC segue o valor acumulado (A até 80%, B até 95%).
        var grupos = linhas.GroupBy(l => l.ProdutoId).Select(g =>
        {
            cad.TryGetValue(g.Key, out var p);
            var valor = g.Sum(l => l.Valor);
            var (lucro, margem) = Margem(g, valor);
            return new
            {
                Id = g.Key,
                Nome = g.OrderByDescending(l => l.VendaId).First().Nome,
                Ref = p?.Referencia,
                Cat = CategoriaDe(p),
                Pecas = g.Sum(l => l.Quantidade),
                Valor = valor,
                Vendas = g.Select(l => l.VendaId).Distinct().Count(),
                Lucro = lucro,
                Margem = margem
            };
        }).ToList();

        var classes = ClassesAbc(grupos.Select(g => (g.Id, g.Valor)).ToList(), total);
        var mais = grupos.OrderByDescending(g => g.Pecas).ThenByDescending(g => g.Valor).ThenBy(g => g.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select((g, i) => new ProdutoVendido(i + 1, g.Id, g.Nome, g.Ref, g.Cat, g.Pecas, g.Valor, g.Vendas, Pct(g.Valor, total), g.Lucro, g.Margem, classes[g.Id]))
            .ToList();

        var categorias = linhas.GroupBy(l => CategoriaDe(cad.GetValueOrDefault(l.ProdutoId))).Select(g =>
        {
            var valor = g.Sum(l => l.Valor);
            var (lucro, margem) = Margem(g, valor);
            return new CategoriaVendida(g.Key, g.Sum(l => l.Quantidade), valor, Pct(valor, total), lucro, margem);
        }).OrderByDescending(c => c.ValorCentavos).ThenBy(c => c.Categoria, StringComparer.CurrentCultureIgnoreCase).ToList();

        string Tam(string t) => t == Tamanhos.SemTamanho ? "" : t;
        var tamanhos = linhas.GroupBy(l => Tam(l.Tamanho))
            .Select(g => new TamanhoVendido(null, "(todos)", g.Key, g.Sum(l => l.Quantidade), g.Sum(l => l.Valor)))
            .OrderByDescending(t => t.Pecas).ThenBy(t => t.Tamanho, StringComparer.CurrentCultureIgnoreCase).ToList();

        var porProdTam = linhas.GroupBy(l => (l.ProdutoId, Tamanho: Tam(l.Tamanho)))
            .Select(g => new TamanhoVendido(g.Key.ProdutoId, g.OrderByDescending(l => l.VendaId).First().Nome, g.Key.Tamanho, g.Sum(l => l.Quantidade), g.Sum(l => l.Valor)))
            .OrderBy(t => t.Produto, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(t => t.Pecas).ThenBy(t => t.Tamanho).ToList();

        return new Resultado(periodo, total, linhas.Sum(l => l.Quantidade), lucroTotal, margemTotal, mais, categorias, tamanhos, porProdTam);
    }

    /// <summary>
    /// Estoque parado: produtos ATIVOS, com controle de estoque e estoque maior que zero, sem venda há <paramref name="dias"/> dias
    /// ou mais (ou que nunca venderam). Ordem: maior custo parado primeiro (os sem custo conhecido por último).
    /// </summary>
    /// <param name="ultimaVenda">Data da última venda não cancelada de cada produto (ProdutoId → data). Ausente = nunca vendeu.</param>
    public static List<ProdutoParado> Parados(IEnumerable<Produto> produtos, IReadOnlyDictionary<int, DateTime> ultimaVenda,
        DateTime hoje, int dias = DiasParadoPadrao)
    {
        var lista = new List<ProdutoParado>();
        foreach (var p in produtos.Where(p => p.Ativo && !p.VenderSemBaixa && p.EstoqueAtual > 0))
        {
            DateTime? ultima = ultimaVenda.TryGetValue(p.Id, out var u) ? u.Date : null;
            int? parado = ultima is DateTime d ? (int)(hoje.Date - d).TotalDays : null;
            if (parado is int n && n < dias) continue;
            long? custo = p.PrecoCustoCentavos > 0 ? p.PrecoCustoCentavos * p.EstoqueAtual : null;
            lista.Add(new ProdutoParado(p.Id, p.Nome, p.Referencia, CategoriaDe(p), p.EstoqueAtual, ultima, parado, custo));
        }
        return lista.OrderByDescending(x => x.CustoParadoCentavos ?? -1).ThenByDescending(x => x.Estoque)
            .ThenBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Soma do custo parado; nulo se nenhum item tem custo conhecido.</summary>
    public static long? CustoTotalParado(IEnumerable<ProdutoParado> parados)
    {
        var com = parados.Where(p => p.CustoParadoCentavos is not null).ToList();
        return com.Count == 0 ? null : com.Sum(p => p.CustoParadoCentavos!.Value);
    }

    // ------------------------------------------------------------------ CSV

    public static string MaisVendidosCsv(Resultado r)
    {
        var c = new CsvRelatorios();
        c.Linha("Posição", "Produto", "Referência", "Categoria", "Quantidade", "Faturamento", "% do faturamento", "Vendas", "Lucro", "Margem (%)", "Classe ABC");
        foreach (var p in r.MaisVendidos)
            c.Linha(CsvRelatorios.N(p.Posicao), p.Nome, p.Referencia, p.Categoria, CsvRelatorios.N(p.Pecas), CsvRelatorios.Valor(p.ValorCentavos),
                CsvRelatorios.Pct(p.PercentualDoFaturamento), CsvRelatorios.N(p.Vendas), CsvRelatorios.Valor(p.LucroCentavos),
                p.MargemPct is null ? "sem dados" : CsvRelatorios.Pct(p.MargemPct), p.ClasseAbc.ToString());
        return c.ToString();
    }

    public static string PorCategoriaCsv(Resultado r)
    {
        var c = new CsvRelatorios();
        c.Linha("Categoria", "Quantidade", "Faturamento", "% do faturamento", "Lucro", "Margem (%)");
        foreach (var x in r.PorCategoria)
            c.Linha(x.Categoria, CsvRelatorios.N(x.Pecas), CsvRelatorios.Valor(x.ValorCentavos), CsvRelatorios.Pct(x.PercentualDoFaturamento),
                CsvRelatorios.Valor(x.LucroCentavos), x.MargemPct is null ? "sem dados" : CsvRelatorios.Pct(x.MargemPct));
        return c.ToString();
    }

    public static string PorTamanhoCsv(Resultado r)
    {
        var c = new CsvRelatorios();
        c.Linha("Produto", "Tamanho", "Quantidade", "Faturamento");
        foreach (var t in r.PorProdutoETamanho) c.Linha(t.Produto, t.Tamanho, CsvRelatorios.N(t.Pecas), CsvRelatorios.Valor(t.ValorCentavos));
        return c.ToString();
    }

    public static string ParadosCsv(IEnumerable<ProdutoParado> parados)
    {
        var c = new CsvRelatorios();
        c.Linha("Produto", "Referência", "Categoria", "Estoque", "Última venda", "Dias sem venda", "Custo parado");
        foreach (var p in parados)
            c.Linha(p.Nome, p.Referencia, p.Categoria, CsvRelatorios.N(p.Estoque), p.UltimaVenda is DateTime d ? CsvRelatorios.Data(d) : "nunca vendeu",
                p.DiasSemVenda is int n ? CsvRelatorios.N(n) : "", p.CustoParadoCentavos is long v ? CsvRelatorios.Valor(v) : "sem custo");
        return c.ToString();
    }

    // ------------------------------------------------------------------ auxiliares

    private static string CategoriaDe(Produto? p) => string.IsNullOrWhiteSpace(p?.Categoria) ? RelatorioService.SemCategoria : p!.Categoria!.Trim();

    private static decimal Pct(long parte, long total) => total <= 0 ? 0 : Math.Round(parte * 100m / total, 2, MidpointRounding.AwayFromZero);

    private static (long? Lucro, decimal? Margem) Margem(IEnumerable<Linha> linhas, long valor)
    {
        var lista = linhas as IReadOnlyCollection<Linha> ?? linhas.ToList();
        if (lista.Count == 0 || lista.Any(l => l.Custo is null) || valor <= 0) return (null, null);
        var lucro = valor - lista.Sum(l => l.Custo!.Value * l.Quantidade);
        return (lucro, Math.Round(lucro * 100m / valor, 2, MidpointRounding.AwayFromZero));
    }

    private static Dictionary<int, char> ClassesAbc(List<(int Id, long Valor)> grupos, long total)
    {
        var r = new Dictionary<int, char>();
        long acumulado = 0;
        foreach (var g in grupos.OrderByDescending(x => x.Valor).ThenBy(x => x.Id))
        {
            // A classe vale pelo acumulado ANTES do produto: o que cruza os 80% ainda é A.
            var antes = total <= 0 ? 100m : acumulado * 100m / total;
            r[g.Id] = antes < 80m ? 'A' : antes < 95m ? 'B' : 'C';
            acumulado += g.Valor;
        }
        return r;
    }
}

/// <summary>Lê vendas e produtos do banco e entrega o relatório de produtos e o estoque parado.</summary>
public class RelatorioProdutosService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public RelatorioProdutosService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    public async Task<RelatorioProdutos.Resultado> GerarAsync(DesempenhoVendas.Intervalo periodo)
    {
        var de = periodo.Inicio.Date;
        var ate = periodo.Fim.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens).Where(v => v.DataHora >= de && v.DataHora < ate && !v.Cancelada).ToListAsync().ConfigureAwait(false);
        var produtos = await db.Produtos.AsNoTracking().ToListAsync().ConfigureAwait(false);
        return RelatorioProdutos.Gerar(vendas, produtos, periodo);
    }

    public async Task<List<RelatorioProdutos.ProdutoParado>> ParadosAsync(DateTime hoje, int dias = RelatorioProdutos.DiasParadoPadrao)
    {
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Tamanhos).ToListAsync().ConfigureAwait(false);
        // Só duas colunas por item vendido; o máximo por produto sai em memória (o SQLite não traduz bem Max de data em grupo).
        var pares = await (from i in db.ItensVenda.AsNoTracking()
                           join v in db.Vendas.AsNoTracking() on i.VendaId equals v.Id
                           where !v.Cancelada && i.Quantidade > 0
                           select new { i.ProdutoId, v.DataHora }).ToListAsync().ConfigureAwait(false);
        var ultimas = pares.GroupBy(x => x.ProdutoId).ToDictionary(g => g.Key, g => g.Max(x => x.DataHora));
        return RelatorioProdutos.Parados(produtos, ultimas, hoje, dias);
    }
}
