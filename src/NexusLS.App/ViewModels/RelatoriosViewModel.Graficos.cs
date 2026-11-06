using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusLS.Core.Services;
using static NexusLS.Core.Services.DesempenhoVendas;

namespace NexusLS.App.ViewModels;

/// <summary>Uma coluna do gráfico "vendas por dia". As frações vão de 0 a 1 na MESMA escala (a maior barra das duas séries = 1).</summary>
public sealed record ColunaDia(string Rotulo, string Dica, double Fracao, double FracaoAnterior);

/// <summary>Gráficos de Relatórios: vendas por dia (com o período anterior ao fundo), formas de pagamento, categorias, o que mais saiu e o que mais faturou.</summary>
public partial class RelatoriosViewModel
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public ObservableCollection<Indicador> Comparacao { get; } = new();
    public ObservableCollection<ColunaDia> ColunasPorDia { get; } = new();
    public ObservableCollection<BarraGrafico> RankingPecas { get; } = new();
    public ObservableCollection<BarraGrafico> RankingValor { get; } = new();
    public ObservableCollection<FatiaDesenhada> PizzaFormas { get; } = new();
    public ObservableCollection<FatiaDesenhada> PizzaCategorias { get; } = new();

    [ObservableProperty] private bool _temGraficos;
    [ObservableProperty] private bool _temComparacao;
    [ObservableProperty] private string _periodoAnteriorTexto = "";
    [ObservableProperty] private string _granularidadeTexto = "";
    [ObservableProperty] private string _maiorBarraTexto = "";
    [ObservableProperty] private string _conferenciaGraficos = "";
    [ObservableProperty] private string _totalFormasTexto = "";
    [ObservableProperty] private string _totalCategoriasTexto = "";

    private void MontarGraficos(GraficosRelatorio g)
    {
        TemGraficos = g.Atual.Vendas > 0;
        TemComparacao = g.TemAnterior;
        PeriodoAnteriorTexto = $"Comparado com {g.Anterior.Inicio:dd/MM/yyyy} a {g.Anterior.Fim:dd/MM/yyyy}";
        GranularidadeTexto = g.Granularidade switch { Granularidade.Semana => "Por semana", Granularidade.Mes => "Por mês", _ => "Por dia" };
        ConferenciaGraficos = "Os gráficos usam os itens vendidos com o desconto da venda rateado e o acréscimo do cartão, sem as vendas canceladas. " +
                              $"Cada um soma {Dinheiro.Formatar(g.Atual.FaturamentoCentavos)}, o mesmo Total vendido do topo. A tabela Por categoria da aba Vendas mostra os itens antes do desconto da venda, por isso difere.";

        Comparacao.Clear();
        Comparacao.Add(Cartao("Faturamento", Dinheiro.Formatar(g.Atual.FaturamentoCentavos), g.Atual.FaturamentoCentavos, g.Antes.FaturamentoCentavos, g, Dinheiro.Formatar));
        Comparacao.Add(Cartao("Vendas", g.Atual.Vendas.ToString(PtBr), g.Atual.Vendas, g.Antes.Vendas, g, v => v.ToString(PtBr)));
        Comparacao.Add(Cartao("Peças vendidas", g.Atual.Pecas.ToString(PtBr), g.Atual.Pecas, g.Antes.Pecas, g, v => v.ToString(PtBr)));
        Comparacao.Add(Cartao("Ticket médio", Dinheiro.Formatar(g.Atual.TicketMedioCentavos), g.Atual.TicketMedioCentavos, g.Antes.TicketMedioCentavos, g, Dinheiro.Formatar));

        MontarColunas(g);

        Preencher(RankingPecas, g.MaisSairam.Select(p => (p.Nome, (decimal)p.Pecas, $"{p.Pecas} peça(s) · {Dinheiro.Formatar(p.ValorCentavos)}")));
        Preencher(RankingValor, g.MaisFaturaram.Select(p => (p.Nome, (decimal)p.ValorCentavos, $"{Dinheiro.Formatar(p.ValorCentavos)} · {p.Pecas} peça(s)")));

        Pizza(PizzaFormas, g.PorForma.Select(f => (SelecaoPagamento.Nome(f.Forma), f.ValorCentavos)));
        Pizza(PizzaCategorias, g.PorCategoria.Select(c => (c.Categoria, c.ValorCentavos)));
        TotalFormasTexto = Dinheiro.Formatar(g.PorForma.Sum(f => f.ValorCentavos));
        TotalCategoriasTexto = Dinheiro.Formatar(g.PorCategoria.Sum(c => c.ValorCentavos));
    }

    private void LimparGraficos()
    {
        TemGraficos = TemComparacao = false;
        Comparacao.Clear(); ColunasPorDia.Clear(); RankingPecas.Clear(); RankingValor.Clear();
        PizzaFormas.Clear(); PizzaCategorias.Clear();
        PeriodoAnteriorTexto = GranularidadeTexto = MaiorBarraTexto = ConferenciaGraficos = TotalFormasTexto = TotalCategoriasTexto = "";
    }

    private static Indicador Cartao(string titulo, string valor, long atual, long anterior, GraficosRelatorio g, Func<long, string> formatar)
    {
        var v = g.TemAnterior ? Variacao(atual, anterior) : null;
        return v is decimal p
            ? new Indicador(titulo, valor, $"{(p > 0 ? "▲" : p < 0 ? "▼" : "=")} {Math.Abs(p).ToString("0.#", PtBr)}% · antes {formatar(anterior)}", p > 0, p < 0)
            : new Indicador(titulo, valor, g.TemAnterior ? $"antes {formatar(anterior)}" : "sem período anterior para comparar", false, false);
    }

    private void MontarColunas(GraficosRelatorio g)
    {
        ColunasPorDia.Clear();
        var maior = Math.Max(g.Serie.Count == 0 ? 0 : g.Serie.Max(p => p.FaturamentoCentavos),
            g.TemAnterior && g.SerieAnterior.Count > 0 ? g.SerieAnterior.Max(p => p.FaturamentoCentavos) : 0);
        MaiorBarraTexto = maior > 0 ? $"Escala até {Dinheiro.Formatar(maior)}" : "";
        var passo = g.Serie.Count <= 16 ? 1 : (int)Math.Ceiling(g.Serie.Count / 10.0);
        for (var i = 0; i < g.Serie.Count; i++)
        {
            var atual = g.Serie[i];
            var antes = g.TemAnterior && i < g.SerieAnterior.Count ? g.SerieAnterior[i] : null;
            var dica = $"{atual.Rotulo} · {Dinheiro.Formatar(atual.FaturamentoCentavos)} · {atual.Vendas} venda(s)";
            if (antes is not null) dica += $"{Environment.NewLine}Antes ({antes.Rotulo}): {Dinheiro.Formatar(antes.FaturamentoCentavos)} · {antes.Vendas} venda(s)";
            ColunasPorDia.Add(new ColunaDia(i % passo == 0 ? atual.Rotulo : "", dica,
                maior > 0 ? (double)atual.FaturamentoCentavos / maior : 0,
                maior > 0 && antes is not null ? (double)antes.FaturamentoCentavos / maior : 0));
        }
    }

    /// <summary>Barras horizontais: a maior vale 1; as outras, a parte dela.</summary>
    private static void Preencher(ObservableCollection<BarraGrafico> alvo, IEnumerable<(string Rotulo, decimal Valor, string Texto)> pontos)
    {
        alvo.Clear();
        var lista = pontos.ToList();
        var maior = lista.Count == 0 ? 0 : lista.Max(p => p.Valor);
        foreach (var p in lista) alvo.Add(new BarraGrafico(p.Rotulo, p.Texto, maior > 0 ? (double)(p.Valor / maior) : 0));
    }

    private static void Pizza(ObservableCollection<FatiaDesenhada> alvo, IEnumerable<(string Rotulo, long ValorCentavos)> itens)
    {
        alvo.Clear();
        foreach (var f in PizzaGrafico.Desenhar(Participacao.Fatias(itens, 7))) alvo.Add(f);
    }
}
