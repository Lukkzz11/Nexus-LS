using static NexusLS.Core.Services.DesempenhoVendas;

namespace NexusLS.Core.Services;

/// <summary>
/// Tudo o que a aba "Gráficos" de Relatórios desenha, a partir das MESMAS linhas vendidas do Desempenho: um item por linha,
/// com o total da venda (desconto e acréscimo inclusos) rateado entre os itens e as vendas canceladas fora. Por isso cada
/// gráfico (por dia, por forma, por categoria, por produto) soma exatamente o "Total vendido" do cabeçalho do relatório.
/// </summary>
/// <param name="Antes">Resumo do período imediatamente anterior, do mesmo tamanho.</param>
/// <param name="SerieAnterior">A mesma série do período anterior, na mesma posição (dia 1 contra dia 1...).</param>
public sealed record GraficosRelatorio(
    Intervalo Periodo,
    Intervalo Anterior,
    Granularidade Granularidade,
    Resumo Atual,
    Resumo Antes,
    IReadOnlyList<PontoSerie> Serie,
    IReadOnlyList<PontoSerie> SerieAnterior,
    IReadOnlyList<GrupoProduto> MaisSairam,
    IReadOnlyList<GrupoProduto> MaisFaturaram,
    IReadOnlyList<GrupoCategoria> PorCategoria,
    IReadOnlyList<GrupoForma> PorForma)
{
    /// <summary>Só se compara quando o período anterior teve venda; senão a comparação some da tela.</summary>
    public bool TemAnterior => Antes.Vendas > 0;

    /// <summary>Até 62 dias o gráfico é por dia; até uns 7 meses, por semana; acima disso, por mês.</summary>
    public static Granularidade GranularidadeSugerida(int dias) =>
        dias <= 62 ? Granularidade.Dia : dias <= 200 ? Granularidade.Semana : Granularidade.Mes;

    /// <summary>Monta os gráficos a partir das linhas já lidas (função pura: sem banco, sem relógio).</summary>
    public static GraficosRelatorio Montar(IReadOnlyCollection<Linha> doPeriodo, IReadOnlyCollection<Linha> doAnterior,
        Intervalo periodo, int limiteRanking = 10)
    {
        var anterior = DesempenhoVendas.Anterior(periodo);
        var gran = GranularidadeSugerida(periodo.Dias);
        return new GraficosRelatorio(
            periodo, anterior, gran,
            Resumir(doPeriodo), Resumir(doAnterior),
            SerieNoTempo(doPeriodo, periodo, gran), SerieNoTempo(doAnterior, anterior, gran),
            DesempenhoVendas.MaisVendidos(doPeriodo, limiteRanking), DesempenhoVendas.MaisFaturaram(doPeriodo, limiteRanking),
            DesempenhoVendas.PorCategoria(doPeriodo), DesempenhoVendas.PorForma(doPeriodo));
    }
}
