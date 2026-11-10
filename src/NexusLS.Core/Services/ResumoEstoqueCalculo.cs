using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Totais gerais do estoque (só produtos com controle de estoque entram nas peças e nos valores).</summary>
public sealed record TotaisEstoque(int ProdutosAtivos, int Pecas, long CustoCentavos, long VendaCentavos, int Baixos, int Esgotados)
{
    public long LucroPotencialCentavos => VendaCentavos - CustoCentavos;
    public int AbaixoDoMinimo => Baixos + Esgotados;
}

public sealed record GrupoCategoriaEstoque(string Categoria, int Pecas, long CustoCentavos, long VendaCentavos);
public sealed record GrupoTamanhoEstoque(string Tamanho, int Pecas);
public sealed record ItemValorEstoque(string Nome, int Pecas, long CustoCentavos);
public sealed record PartesEstoque(int PecasNormais, long CustoNormalCentavos, int PecasSobras, long CustoSobrasCentavos);

/// <summary>Um ponto da linha de movimentações: o início do dia (ou da semana) e o total que entrou e que saiu.</summary>
public sealed record PontoMovimentacao(DateTime Inicio, string Rotulo, int Entradas, int Saidas);

/// <summary>
/// Contas do Resumo do estoque (função pura: sem banco e sem relógio). Produtos "vender sem baixa" não têm estoque de
/// verdade, então ficam fora das peças, dos valores e dos gráficos; estoque negativo conta como zero nos valores.
/// </summary>
public static class ResumoEstoqueCalculo
{
    public const string SemCategoria = "Sem categoria";

    private static IEnumerable<Produto> Controlados(IEnumerable<Produto> produtos) =>
        produtos.Where(p => p.Ativo && !p.VenderSemBaixa);

    private static int Pecas(Produto p) => Math.Max(0, p.EstoqueAtual);

    public static TotaisEstoque Totais(IReadOnlyCollection<Produto> produtos)
    {
        var ativos = produtos.Where(p => p.Ativo).ToList();
        var c = Controlados(ativos).ToList();
        return new TotaisEstoque(
            ativos.Count,
            c.Sum(Pecas),
            c.Sum(p => p.PrecoCustoCentavos * Pecas(p)),
            c.Sum(p => p.PrecoVendaCentavos * Pecas(p)),
            c.Count(p => p.EstoqueAtual > 0 && p.EstoqueAtual < p.EstoqueMinimo),
            c.Count(p => p.EstoqueAtual <= 0));
    }

    /// <summary>Peças, custo e venda por categoria, da que tem mais peças para a que tem menos (empate: pelo nome).</summary>
    public static List<GrupoCategoriaEstoque> PorCategoria(IReadOnlyCollection<Produto> produtos) =>
        Controlados(produtos)
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Categoria) ? SemCategoria : p.Categoria!.Trim())
            .Select(g => new GrupoCategoriaEstoque(g.Key, g.Sum(Pecas), g.Sum(p => p.PrecoCustoCentavos * Pecas(p)), g.Sum(p => p.PrecoVendaCentavos * Pecas(p))))
            .OrderByDescending(x => x.Pecas).ThenBy(x => x.Categoria, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Peças em cada tamanho: primeiro os das grades conhecidas na ordem delas, depois os outros pelo nome, "Sem tamanho" por último.</summary>
    public static List<GrupoTamanhoEstoque> PorTamanho(IReadOnlyCollection<Produto> produtos)
    {
        var ordem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in GradesDeTamanho.Todas.SelectMany(g => g.Tamanhos))
            ordem.TryAdd(t, ordem.Count);

        return Controlados(produtos).SelectMany(p => p.Tamanhos)
            .Where(t => t.Estoque > 0)
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Tamanho) ? Tamanhos.SemTamanho : t.Tamanho.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new GrupoTamanhoEstoque(g.Key, g.Sum(t => t.Estoque)))
            .OrderBy(x => x.Tamanho == Tamanhos.SemTamanho ? 2 : ordem.ContainsKey(x.Tamanho) ? 0 : 1)
            .ThenBy(x => ordem.TryGetValue(x.Tamanho, out var o) ? o : 0)
            .ThenBy(x => x.Tamanho, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Produtos que mais têm dinheiro parado em estoque (valor de custo), do maior para o menor.</summary>
    public static List<ItemValorEstoque> MaioresPorValor(IReadOnlyCollection<Produto> produtos, int limite = 10) =>
        Controlados(produtos)
            .Select(p => new ItemValorEstoque(p.Nome, Pecas(p), p.PrecoCustoCentavos * Pecas(p)))
            .Where(i => i.CustoCentavos > 0)
            .OrderByDescending(i => i.CustoCentavos).ThenBy(i => i.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Take(limite).ToList();

    /// <summary>Estoque normal x sobras da loja, em peças e em valor de custo.</summary>
    public static PartesEstoque SobrasXNormal(IReadOnlyCollection<Produto> produtos)
    {
        var c = Controlados(produtos).ToList();
        var normal = c.Where(p => !p.Sobra).ToList();
        var sobras = c.Where(p => p.Sobra).ToList();
        return new PartesEstoque(normal.Sum(Pecas), normal.Sum(p => p.PrecoCustoCentavos * Pecas(p)),
            sobras.Sum(Pecas), sobras.Sum(p => p.PrecoCustoCentavos * Pecas(p)));
    }

    /// <summary>
    /// Entradas (variação positiva) x saídas (variação negativa) entre as duas datas, incluídas. Até 31 dias o ponto é o
    /// dia; acima disso, a semana (começando na segunda). Dias sem movimento aparecem com zero, para a linha não pular.
    /// </summary>
    public static List<PontoMovimentacao> Movimentacoes(IEnumerable<MovimentacaoEstoque> movimentos, DateTime de, DateTime ate)
    {
        de = de.Date; ate = ate.Date;
        if (ate < de) return new();
        var porSemana = (ate - de).Days + 1 > 31;
        DateTime Chave(DateTime d) => porSemana ? d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7)) : d.Date;

        var dentro = movimentos.Where(m => m.DataHora.Date >= de && m.DataHora.Date <= ate && m.Quantidade != 0)
            .GroupBy(m => Chave(m.DataHora))
            .ToDictionary(g => g.Key, g => (Ent: g.Where(m => m.Quantidade > 0).Sum(m => m.Quantidade),
                                            Sai: -g.Where(m => m.Quantidade < 0).Sum(m => m.Quantidade)));

        var pontos = new List<PontoMovimentacao>();
        for (var d = Chave(de); d <= ate; d = d.AddDays(porSemana ? 7 : 1))
        {
            dentro.TryGetValue(d, out var v);
            pontos.Add(new PontoMovimentacao(d, d.ToString("dd/MM"), v.Ent, v.Sai));
        }
        return pontos;
    }
}
