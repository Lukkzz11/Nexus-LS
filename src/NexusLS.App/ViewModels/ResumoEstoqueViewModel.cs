using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma barra de gráfico: nome, texto do valor e a largura em % (0 a 100).</summary>
public record BarraEstoque(string Rotulo, string Valor, double Percentual, bool Alerta = false);

/// <summary>Linha de movimentações já desenhada: caminhos (Path) das entradas e das saídas, numa área de 300 x 100.</summary>
public sealed record LinhaMovimentacao(string CaminhoEntradas, string CaminhoSaidas, string Escala, string PrimeiroRotulo, string UltimoRotulo,
    int TotalEntradas, int TotalSaidas, bool Vazia);

/// <summary>Resumo do estoque: números gerais, gráficos por categoria, tamanho e valor, sobras, o que mais sai e o que está acabando.</summary>
public partial class ResumoEstoqueViewModel : ViewModelBase
{
    public const double LarguraLinha = 300, AlturaLinha = 100;

    private static readonly LinhaMovimentacao LinhaVazia = new("", "", "", "", "", 0, 0, true);

    private readonly ProdutoService _produtos;
    private readonly RelatorioService _relatorio;
    private readonly EstoqueService? _estoque;
    private readonly Func<DateTime> _hoje;

    public ResumoEstoqueViewModel(ProdutoService produtos, RelatorioService relatorio, Func<DateTime>? hoje = null, EstoqueService? estoque = null)
    {
        _produtos = produtos;
        _relatorio = relatorio;
        _estoque = estoque;
        _hoje = hoje ?? (() => DateTime.Today);
        _ = CarregarAsync();
    }

    public ObservableCollection<BarraEstoque> MaisSaem { get; } = new();
    public ObservableCollection<BarraEstoque> PorCategoria { get; } = new();
    public ObservableCollection<BarraEstoque> PorTamanho { get; } = new();
    public ObservableCollection<BarraEstoque> MaioresEstoques { get; } = new();
    public ObservableCollection<BarraEstoque> MaioresValores { get; } = new();
    public ObservableCollection<BarraEstoque> EstoqueBaixo { get; } = new();
    public ObservableCollection<FatiaDesenhada> PizzaCategorias { get; } = new();
    public ObservableCollection<FatiaDesenhada> PizzaSobras { get; } = new();

    [ObservableProperty] private string _produtosAtivos = "0";
    [ObservableProperty] private string _pecasEmEstoque = "0";
    [ObservableProperty] private string _valorCusto = "R$ 0,00";
    [ObservableProperty] private string _valorVenda = "R$ 0,00";
    [ObservableProperty] private string _lucroPotencial = "R$ 0,00";
    [ObservableProperty] private string _qtdAbaixoMinimo = "0";
    [ObservableProperty] private string _qtdBaixo = "0";
    [ObservableProperty] private string _qtdEsgotado = "0";
    [ObservableProperty] private string _periodoTexto = "";
    [ObservableProperty] private int _dias = 30;
    [ObservableProperty] private LinhaMovimentacao _movimentacoes = LinhaVazia;

    partial void OnDiasChanged(int value) => _ = CarregarAsync();

    public IReadOnlyList<int> OpcoesDias { get; } = new[] { 7, 15, 30, 60, 90 };

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var produtos = (await _produtos.ListarComTamanhosAsync(incluirInativos: false)).ToList();
        var controlados = produtos.Where(p => !p.VenderSemBaixa).ToList();

        var t = ResumoEstoqueCalculo.Totais(produtos);
        ProdutosAtivos = t.ProdutosAtivos.ToString();
        PecasEmEstoque = t.Pecas.ToString("N0");
        ValorCusto = Dinheiro.Formatar(t.CustoCentavos);
        ValorVenda = Dinheiro.Formatar(t.VendaCentavos);
        LucroPotencial = Dinheiro.Formatar(t.LucroPotencialCentavos);
        QtdAbaixoMinimo = t.AbaixoDoMinimo.ToString();
        QtdBaixo = t.Baixos.ToString();
        QtdEsgotado = t.Esgotados.ToString();

        var categorias = ResumoEstoqueCalculo.PorCategoria(produtos);
        var maiorCategoria = categorias.Count == 0 ? 0 : categorias.Max(x => x.Pecas);
        Preencher(PorCategoria, categorias.Take(10).Select(c => new BarraEstoque(c.Categoria,
            $"{c.Pecas:N0} peça(s) · custo {Dinheiro.Formatar(c.CustoCentavos)} · venda {Dinheiro.Formatar(c.VendaCentavos)}",
            Escala(c.Pecas, maiorCategoria))));
        Preencher(PizzaCategorias, PizzaGrafico.Desenhar(Participacao.Fatias(categorias.Select(c => (c.Categoria, c.CustoCentavos)))));

        var tamanhos = ResumoEstoqueCalculo.PorTamanho(produtos);
        var maiorTamanho = tamanhos.Count == 0 ? 0 : tamanhos.Max(y => y.Pecas);
        Preencher(PorTamanho, tamanhos.Select(x => new BarraEstoque(x.Tamanho, $"{x.Pecas:N0} peça(s)", Escala(x.Pecas, maiorTamanho))));

        Preencher(MaioresEstoques, Barras(controlados.OrderByDescending(p => p.EstoqueAtual).Take(10)
            .Select(p => (Nome: p.Nome, Valor: p.EstoqueAtual)), "peça(s)"));
        var valores = ResumoEstoqueCalculo.MaioresPorValor(produtos);
        var maiorValor = valores.Count == 0 ? 0 : valores.Max(x => x.CustoCentavos);
        Preencher(MaioresValores, valores.Select(v => new BarraEstoque(v.Nome,
            $"{Dinheiro.Formatar(v.CustoCentavos)} · {v.Pecas:N0} peça(s)", Escala(v.CustoCentavos, maiorValor))));

        var partes = ResumoEstoqueCalculo.SobrasXNormal(produtos);
        Preencher(PizzaSobras, PizzaGrafico.Desenhar(Participacao.Fatias(new[]
        {
            ("Estoque normal", partes.CustoNormalCentavos), ("Sobras da loja", partes.CustoSobrasCentavos)
        })));

        // A barra mostra o estoque como % do mínimo (vermelha: zerado ou abaixo do mínimo).
        var baixos = controlados.Where(p => p.EstoqueAtual > 0 && p.EstoqueAtual < p.EstoqueMinimo).ToList();
        var esgotados = controlados.Where(p => p.EstoqueAtual <= 0).ToList();
        EstoqueBaixo.Clear();
        foreach (var p in esgotados.Concat(baixos)
                     .OrderBy(p => p.EstoqueMinimo <= 0 ? 0 : (double)Math.Max(0, p.EstoqueAtual) / p.EstoqueMinimo).Take(12))
            EstoqueBaixo.Add(new BarraEstoque(p.Nome, $"{Math.Max(0, p.EstoqueAtual)} de {p.EstoqueMinimo} (mínimo)",
                p.EstoqueMinimo <= 0 ? 3 : Math.Clamp(100.0 * Math.Max(0, p.EstoqueAtual) / p.EstoqueMinimo, 3, 100), Alerta: true));

        var ate = _hoje().Date;
        var de = ate.AddDays(-(Dias - 1));
        PeriodoTexto = $"{de:dd/MM} a {ate:dd/MM} (últimos {Dias} dias, sem vendas canceladas)";
        var vendidos = await _relatorio.MaisVendidosAsync(de, ate, porTamanho: false, limite: 10);
        Preencher(MaisSaem, Barras(vendidos.Select(v => (Nome: v.Nome, Valor: v.Quantidade)), "vendida(s)"));

        var movs = _estoque is null ? new List<MovimentacaoEstoque>() : await _estoque.ListarMovimentacoesPeriodoAsync(de, ate);
        Movimentacoes = DesenharLinha(ResumoEstoqueCalculo.Movimentacoes(movs, de, ate));
    });

    /// <summary>Largura da barra em % (mínimo 2 para sempre aparecer um tracinho).</summary>
    public static double Escala(long valor, long maior) => maior <= 0 ? 2 : Math.Max(2, 100.0 * Math.Max(0, valor) / maior);

    /// <summary>Converte os pontos em dois caminhos (entradas e saídas) numa área de 300 x 100, com a mesma escala nos dois.</summary>
    public static LinhaMovimentacao DesenharLinha(IReadOnlyList<PontoMovimentacao> pontos)
    {
        var entradas = pontos.Sum(p => p.Entradas);
        var saidas = pontos.Sum(p => p.Saidas);
        if (pontos.Count == 0 || (entradas == 0 && saidas == 0)) return LinhaVazia;

        var maior = Math.Max(1, pontos.Max(p => Math.Max(p.Entradas, p.Saidas)));
        static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        string Caminho(Func<PontoMovimentacao, int> valor)
        {
            var partes = new List<string>();
            for (var i = 0; i < pontos.Count; i++)
            {
                var x = pontos.Count == 1 ? LarguraLinha / 2 : LarguraLinha * i / (pontos.Count - 1);
                var y = AlturaLinha - AlturaLinha * valor(pontos[i]) / maior;
                partes.Add($"{(i == 0 ? "M" : "L")} {N(x)},{N(y)}");
            }
            if (pontos.Count == 1) partes.Add($"L {N(LarguraLinha / 2 + 0.01)},{N(AlturaLinha - AlturaLinha * valor(pontos[0]) / maior)}");
            return string.Join(" ", partes);
        }
        return new LinhaMovimentacao(Caminho(p => p.Entradas), Caminho(p => p.Saidas), $"Escala até {maior:N0} peça(s)",
            pontos[0].Rotulo, pontos[^1].Rotulo, entradas, saidas, false);
    }

    private static IEnumerable<BarraEstoque> Barras(IEnumerable<(string Nome, int Valor)> itens, string unidade)
    {
        var lista = itens.ToList();
        var maior = lista.Count == 0 ? 1 : lista.Max(i => i.Valor);
        return lista.Select(i => new BarraEstoque(i.Nome, $"{i.Valor:N0} {unidade}", Escala(i.Valor, maior)));
    }

    private static void Preencher<T>(ObservableCollection<T> alvo, IEnumerable<T> novos)
    {
        alvo.Clear();
        foreach (var b in novos) alvo.Add(b);
    }
}
