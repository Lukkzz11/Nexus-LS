using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Desempenho de vendas (as mesmas regras do ERP): vendas viram linhas (um item vendido por linha, com o desconto/acréscimo
/// da venda rateado), e os indicadores saem dessas linhas. Vendas canceladas nunca entram. O lucro só aparece quando o custo
/// é conhecido em TODAS as peças do recorte; senão fica nulo (melhor sem número do que com um número enganoso).
/// </summary>
public static class DesempenhoVendas
{
    public enum TipoPeriodo { Hoje, Ultimos7Dias, Ultimos30Dias, MesAtual, Personalizado }

    public enum Granularidade { Dia, Semana, Mes }

    /// <summary>Datas inclusas nas duas pontas.</summary>
    public sealed record Intervalo(DateTime Inicio, DateTime Fim)
    {
        public int Dias => (int)(Fim.Date - Inicio.Date).TotalDays + 1;
    }

    /// <summary>Uma linha vendida. <c>CustoUnitarioCentavos</c> nulo = custo desconhecido.</summary>
    public sealed record Linha(int VendaId, DateTime Dia, FormaPagamento Forma, string Operador, bool OperadorConfiavel, string Cliente,
        int? ProdutoId, string Nome, string Categoria, int Quantidade, long ValorCentavos, long? CustoUnitarioCentavos);

    public sealed record Resumo(long FaturamentoCentavos, int Vendas, int Pecas, long TicketMedioCentavos,
        long? LucroCentavos, decimal? MargemPct, decimal CoberturaCusto);

    public sealed record GrupoProduto(int ProdutoId, string Nome, string Categoria, int Pecas, long ValorCentavos, int Vendas);
    public sealed record GrupoCategoria(string Categoria, int Pecas, long ValorCentavos, int Vendas);
    public sealed record GrupoForma(FormaPagamento Forma, int Pecas, long ValorCentavos, int Vendas);
    public sealed record GrupoOperador(string Operador, int Pecas, long ValorCentavos, int Vendas);
    public sealed record PontoSerie(string Rotulo, DateTime Inicio, long FaturamentoCentavos, int Pecas, int Vendas);
    public sealed record ProdutoSemVenda(int ProdutoId, string Nome, string Categoria, int Estoque);

    // ------------------------------------------------------------------ período

    public static Intervalo Resolver(TipoPeriodo tipo, DateTime hoje, DateTime? de = null, DateTime? ate = null)
    {
        var h = hoje.Date;
        return tipo switch
        {
            TipoPeriodo.Ultimos7Dias => new Intervalo(h.AddDays(-6), h),
            TipoPeriodo.Ultimos30Dias => new Intervalo(h.AddDays(-29), h),
            TipoPeriodo.MesAtual => new Intervalo(new DateTime(h.Year, h.Month, 1), h),
            TipoPeriodo.Personalizado when de is DateTime a && ate is DateTime b =>
                a.Date <= b.Date ? new Intervalo(a.Date, b.Date) : new Intervalo(b.Date, a.Date),
            _ => new Intervalo(h, h)
        };
    }

    /// <summary>O período imediatamente anterior, do mesmo tamanho.</summary>
    public static Intervalo Anterior(Intervalo p)
    {
        var fim = p.Inicio.AddDays(-1);
        return new Intervalo(fim.AddDays(-(p.Dias - 1)), fim);
    }

    // ------------------------------------------------------------------ linhas

    private static readonly string[] OperadoresGenericos = { "caixa geral", "operador", "caixa offline", "consumidor final" };

    public static (string Nome, bool Confiavel) IdentidadeDoOperador(string? operador)
    {
        var nome = operador?.Trim() ?? "";
        var generico = nome.Length == 0 || OperadoresGenericos.Contains(nome.ToLowerInvariant());
        return generico ? ("Não identificado", false) : (nome, true);
    }

    /// <summary>
    /// Um item vendido por linha, com o total da venda (desconto e acréscimo inclusos) rateado pelo valor bruto de cada item;
    /// a diferença de centavos fica no último item. Venda sem itens vira uma linha sem produto (conta no faturamento, não nas peças).
    /// </summary>
    public static List<Linha> ExpandirLinhas(IEnumerable<Venda> vendas, IReadOnlyDictionary<int, (string? Categoria, long Custo)> produtos)
    {
        var linhas = new List<Linha>();
        foreach (var v in vendas.Where(v => !v.Cancelada))
        {
            var (operador, confiavel) = IdentidadeDoOperador(v.Operador);
            var cliente = v.Cliente?.Nome ?? "Consumidor Final";
            var itens = v.Itens.Where(i => i.Quantidade > 0).ToList();
            var somaBruto = itens.Sum(i => i.TotalCentavos);
            if (itens.Count == 0 || somaBruto <= 0)
            {
                linhas.Add(new Linha(v.Id, v.DataHora.Date, v.FormaPagamento, operador, confiavel, cliente, null,
                    "Venda sem itens detalhados", RelatorioService.SemCategoria, 0, v.TotalCentavos, null));
                continue;
            }
            long alocado = 0;
            for (var i = 0; i < itens.Count; i++)
            {
                var item = itens[i];
                var valor = i == itens.Count - 1 ? v.TotalCentavos - alocado
                    : (long)Math.Round((decimal)item.TotalCentavos * v.TotalCentavos / somaBruto, MidpointRounding.AwayFromZero);
                alocado += valor;
                produtos.TryGetValue(item.ProdutoId, out var cad);
                long? custo = item.CustoUnitarioCentavos ?? (cad.Custo > 0 ? cad.Custo : null);
                if (custo is <= 0) custo = null;
                linhas.Add(new Linha(v.Id, v.DataHora.Date, v.FormaPagamento, operador, confiavel, cliente, item.ProdutoId,
                    item.NomeProduto, cad.Categoria ?? RelatorioService.SemCategoria, item.Quantidade, valor, custo));
            }
        }
        return linhas;
    }

    // ------------------------------------------------------------------ indicadores

    public static Resumo Resumir(IReadOnlyCollection<Linha> linhas)
    {
        var vendas = linhas.Select(l => l.VendaId).Distinct().Count();
        var valor = linhas.Sum(l => l.ValorCentavos);
        var pecas = linhas.Sum(l => l.Quantidade);
        var comProduto = linhas.Where(l => l.ProdutoId is not null).ToList();
        var valorComProduto = comProduto.Sum(l => l.ValorCentavos);
        var comCusto = comProduto.Where(l => l.CustoUnitarioCentavos is not null).ToList();
        var valorComCusto = comCusto.Sum(l => l.ValorCentavos);
        var custo = comCusto.Sum(l => l.CustoUnitarioCentavos!.Value * l.Quantidade);
        var cobertura = valorComProduto > 0 ? (decimal)valorComCusto / valorComProduto : 0m;
        var lucroConfiavel = linhas.Count > 0 && valorComProduto == valor && cobertura == 1m;
        return new Resumo(valor, vendas, pecas,
            vendas > 0 ? (long)Math.Round((decimal)valor / vendas, MidpointRounding.AwayFromZero) : 0,
            lucroConfiavel ? valor - custo : null,
            lucroConfiavel && valor > 0 ? (decimal)(valor - custo) * 100m / valor : null,
            cobertura);
    }

    /// <summary>Variação em relação ao período anterior, em %; nulo se não há base de comparação.</summary>
    public static decimal? Variacao(decimal atual, decimal anterior) => anterior == 0 ? null : (atual - anterior) * 100m / anterior;

    public static List<GrupoProduto> PorProduto(IEnumerable<Linha> linhas) => linhas
        .Where(l => l.ProdutoId is not null)
        .GroupBy(l => l.ProdutoId!.Value)
        .Select(g => new GrupoProduto(g.Key, g.OrderByDescending(l => l.VendaId).First().Nome, g.First().Categoria,
            g.Sum(l => l.Quantidade), g.Sum(l => l.ValorCentavos), g.Select(l => l.VendaId).Distinct().Count()))
        .ToList();

    public static List<GrupoProduto> MaisVendidos(IEnumerable<Linha> linhas, int limite = 10) => PorProduto(linhas)
        .OrderByDescending(p => p.Pecas).ThenByDescending(p => p.ValorCentavos).ThenBy(p => p.Nome).Take(limite).ToList();

    /// <summary>Entre os que venderam, os de menor saída.</summary>
    public static List<GrupoProduto> MenosVendidos(IEnumerable<Linha> linhas, int limite = 10) => PorProduto(linhas)
        .OrderBy(p => p.Pecas).ThenBy(p => p.ValorCentavos).ThenBy(p => p.Nome).Take(limite).ToList();

    public static List<ProdutoSemVenda> SemVendas(IEnumerable<(int Id, string Nome, string? Categoria, int Estoque, bool Ativo)> produtos, IEnumerable<Linha> linhas)
    {
        var vendidos = linhas.Where(l => l.ProdutoId is not null).Select(l => l.ProdutoId!.Value).ToHashSet();
        return produtos.Where(p => p.Ativo && !vendidos.Contains(p.Id))
            .Select(p => new ProdutoSemVenda(p.Id, p.Nome, p.Categoria ?? RelatorioService.SemCategoria, p.Estoque))
            .OrderBy(p => p.Nome).ToList();
    }

    public static List<GrupoCategoria> PorCategoria(IEnumerable<Linha> linhas) => linhas
        .GroupBy(l => l.Categoria)
        .Select(g => new GrupoCategoria(g.Key, g.Sum(l => l.Quantidade), g.Sum(l => l.ValorCentavos), g.Select(l => l.VendaId).Distinct().Count()))
        .OrderByDescending(g => g.ValorCentavos).ThenBy(g => g.Categoria).ToList();

    public static List<GrupoForma> PorForma(IEnumerable<Linha> linhas) => linhas
        .GroupBy(l => l.Forma)
        .Select(g => new GrupoForma(g.Key, g.Sum(l => l.Quantidade), g.Sum(l => l.ValorCentavos), g.Select(l => l.VendaId).Distinct().Count()))
        .OrderByDescending(g => g.ValorCentavos).ToList();

    /// <summary>Desempenho por quem vendeu; nulo quando ninguém pode ser identificado com segurança.</summary>
    public static List<GrupoOperador>? PorOperador(IReadOnlyCollection<Linha> linhas) =>
        !linhas.Any(l => l.OperadorConfiavel) ? null : linhas
            .GroupBy(l => l.Operador)
            .Select(g => new GrupoOperador(g.Key, g.Sum(l => l.Quantidade), g.Sum(l => l.ValorCentavos), g.Select(l => l.VendaId).Distinct().Count()))
            .OrderByDescending(g => g.ValorCentavos).ToList();

    // ------------------------------------------------------------------ série no tempo

    private static DateTime ChaveDoPeriodo(DateTime dia, Granularidade g) => g switch
    {
        Granularidade.Semana => dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7)), // segunda-feira
        Granularidade.Mes => new DateTime(dia.Year, dia.Month, 1),
        _ => dia.Date
    };

    private static string Rotulo(DateTime chave, Granularidade g) => g switch
    {
        Granularidade.Mes => chave.ToString("MMM/yy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
        Granularidade.Semana => $"Sem. {chave:dd/MM}",
        _ => chave.ToString("dd/MM")
    };

    /// <summary>Faturamento, vendas e peças por dia/semana/mês, incluindo os períodos sem venda (zerados).</summary>
    public static List<PontoSerie> SerieNoTempo(IEnumerable<Linha> linhas, Intervalo periodo, Granularidade gran = Granularidade.Dia)
    {
        var baldes = new SortedDictionary<DateTime, (long Valor, int Pecas, HashSet<int> Vendas)>();
        for (var d = periodo.Inicio.Date; d <= periodo.Fim.Date; d = d.AddDays(1))
            baldes.TryAdd(ChaveDoPeriodo(d, gran), (0, 0, new HashSet<int>()));
        foreach (var l in linhas)
        {
            if (!baldes.TryGetValue(ChaveDoPeriodo(l.Dia, gran), out var b)) continue;
            b.Vendas.Add(l.VendaId);
            baldes[ChaveDoPeriodo(l.Dia, gran)] = (b.Valor + l.ValorCentavos, b.Pecas + l.Quantidade, b.Vendas);
        }
        return baldes.Select(kv => new PontoSerie(Rotulo(kv.Key, gran), kv.Key, kv.Value.Valor, kv.Value.Pecas, kv.Value.Vendas.Count)).ToList();
    }
}

/// <summary>Tudo o que a tela de desempenho mostra para um período, já com a comparação com o período anterior.</summary>
public sealed record RelatorioDesempenho(
    DesempenhoVendas.Intervalo Periodo,
    DesempenhoVendas.Intervalo Anterior,
    DesempenhoVendas.Resumo Atual,
    DesempenhoVendas.Resumo Antes,
    IReadOnlyList<DesempenhoVendas.PontoSerie> Serie,
    IReadOnlyList<DesempenhoVendas.GrupoProduto> MaisVendidos,
    IReadOnlyList<DesempenhoVendas.GrupoProduto> MenosVendidos,
    IReadOnlyList<DesempenhoVendas.ProdutoSemVenda> SemVendas,
    IReadOnlyList<DesempenhoVendas.GrupoCategoria> PorCategoria,
    IReadOnlyList<DesempenhoVendas.GrupoForma> PorForma,
    IReadOnlyList<DesempenhoVendas.GrupoOperador>? PorOperador,
    IReadOnlyList<string> Categorias,
    IReadOnlyList<string> Operadores);

/// <summary>Filtros opcionais da tela de desempenho. Campo nulo/vazio = sem filtro daquele tipo.</summary>
public sealed record FiltroDesempenho(string? Categoria = null, string? Produto = null, FormaPagamento? Forma = null, string? Operador = null)
{
    public bool Vazio => string.IsNullOrWhiteSpace(Categoria) && string.IsNullOrWhiteSpace(Produto) && Forma is null && string.IsNullOrWhiteSpace(Operador);

    public bool SoCategoria => string.IsNullOrWhiteSpace(Produto) && Forma is null && string.IsNullOrWhiteSpace(Operador);

    public List<DesempenhoVendas.Linha> Aplicar(IEnumerable<DesempenhoVendas.Linha> linhas) => linhas
        .Where(l => string.IsNullOrWhiteSpace(Categoria) || string.Equals(l.Categoria, Categoria, StringComparison.CurrentCultureIgnoreCase))
        .Where(l => string.IsNullOrWhiteSpace(Produto) || l.Nome.Contains(Produto.Trim(), StringComparison.CurrentCultureIgnoreCase))
        .Where(l => Forma is null || l.Forma == Forma)
        .Where(l => string.IsNullOrWhiteSpace(Operador) || string.Equals(l.Operador, Operador, StringComparison.CurrentCultureIgnoreCase))
        .ToList();
}

/// <summary>Lê as vendas do período (e do anterior) uma vez e monta o relatório de desempenho.</summary>
public class DesempenhoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public DesempenhoService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    public async Task<RelatorioDesempenho> GerarAsync(DesempenhoVendas.Intervalo periodo, DesempenhoVendas.Granularidade gran, FiltroDesempenho? filtro = null)
    {
        var anterior = DesempenhoVendas.Anterior(periodo);
        await using var db = await _fabrica.CreateDbContextAsync();

        var de = anterior.Inicio.Date;
        var ate = periodo.Fim.Date.AddDays(1);
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens).Include(v => v.Cliente)
            .Where(v => !v.Cancelada && v.DataHora >= de && v.DataHora < ate).ToListAsync();

        var produtos = await db.Produtos.AsNoTracking()
            .Select(p => new { p.Id, p.Nome, p.Categoria, p.PrecoCustoCentavos, p.Ativo, Estoque = p.Tamanhos.Sum(t => t.Estoque) })
            .ToListAsync();
        var cadastro = produtos.ToDictionary(p => p.Id, p => (p.Categoria, Custo: p.PrecoCustoCentavos));

        var linhas = DesempenhoVendas.ExpandirLinhas(vendas, cadastro);
        var semFiltro = linhas.Where(l => l.Dia >= periodo.Inicio.Date && l.Dia <= periodo.Fim.Date).ToList();
        filtro ??= new FiltroDesempenho();
        var doPeriodo = filtro.Aplicar(semFiltro);
        var doAnterior = filtro.Aplicar(linhas.Where(l => l.Dia >= anterior.Inicio.Date && l.Dia <= anterior.Fim.Date));
        var categorias = semFiltro.Select(l => l.Categoria).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Order().ToList();
        var operadores = semFiltro.Where(l => l.OperadorConfiavel).Select(l => l.Operador).Distinct().Order().ToList();
        // "Sem venda" só faz sentido sem filtro ou filtrando categoria; com os outros filtros listaria tudo.
        var candidatos = produtos.Where(p => string.IsNullOrWhiteSpace(filtro.Categoria) || string.Equals(p.Categoria, filtro.Categoria, StringComparison.CurrentCultureIgnoreCase));

        return new RelatorioDesempenho(periodo, anterior,
            DesempenhoVendas.Resumir(doPeriodo), DesempenhoVendas.Resumir(doAnterior),
            DesempenhoVendas.SerieNoTempo(doPeriodo, periodo, gran),
            DesempenhoVendas.MaisVendidos(doPeriodo), DesempenhoVendas.MenosVendidos(doPeriodo),
            filtro.SoCategoria ? DesempenhoVendas.SemVendas(candidatos.Select(p => (p.Id, p.Nome, p.Categoria, p.Estoque, p.Ativo)), doPeriodo) : new List<DesempenhoVendas.ProdutoSemVenda>(),
            DesempenhoVendas.PorCategoria(doPeriodo), DesempenhoVendas.PorForma(doPeriodo), DesempenhoVendas.PorOperador(doPeriodo), categorias, operadores);
    }

    /// <summary>As linhas vendidas do período (já filtradas), para exportar.</summary>
    public async Task<List<DesempenhoVendas.Linha>> LinhasAsync(DesempenhoVendas.Intervalo periodo, FiltroDesempenho? filtro = null)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var ate = periodo.Fim.Date.AddDays(1);
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens).Include(v => v.Cliente)
            .Where(v => !v.Cancelada && v.DataHora >= periodo.Inicio.Date && v.DataHora < ate).ToListAsync();
        var cadastro = (await db.Produtos.AsNoTracking().Select(p => new { p.Id, p.Categoria, p.PrecoCustoCentavos }).ToListAsync())
            .ToDictionary(p => p.Id, p => (p.Categoria, Custo: p.PrecoCustoCentavos));
        return (filtro ?? new FiltroDesempenho()).Aplicar(DesempenhoVendas.ExpandirLinhas(vendas, cadastro));
    }
}
