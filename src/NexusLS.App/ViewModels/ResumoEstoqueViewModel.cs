using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma barra de gráfico: nome, texto do valor e a largura em % (0 a 100).</summary>
public record BarraEstoque(string Rotulo, string Valor, double Percentual, bool Alerta = false);

/// <summary>Resumo do estoque: números gerais, o que mais sai, onde está o estoque e o que está acabando.</summary>
public partial class ResumoEstoqueViewModel : ViewModelBase
{
    private readonly ProdutoService _produtos;
    private readonly RelatorioService _relatorio;
    private readonly Func<DateTime> _hoje;

    public ResumoEstoqueViewModel(ProdutoService produtos, RelatorioService relatorio, Func<DateTime>? hoje = null)
    {
        _produtos = produtos;
        _relatorio = relatorio;
        _hoje = hoje ?? (() => DateTime.Today);
        _ = CarregarAsync();
    }

    public ObservableCollection<BarraEstoque> MaisSaem { get; } = new();
    public ObservableCollection<BarraEstoque> PorCategoria { get; } = new();
    public ObservableCollection<BarraEstoque> MaioresEstoques { get; } = new();
    public ObservableCollection<BarraEstoque> EstoqueBaixo { get; } = new();

    [ObservableProperty] private string _produtosAtivos = "0";
    [ObservableProperty] private string _pecasEmEstoque = "0";
    [ObservableProperty] private string _valorCusto = "R$ 0,00";
    [ObservableProperty] private string _valorVenda = "R$ 0,00";
    [ObservableProperty] private string _qtdBaixo = "0";
    [ObservableProperty] private string _qtdEsgotado = "0";
    [ObservableProperty] private string _periodoTexto = "";
    [ObservableProperty] private int _dias = 30;

    partial void OnDiasChanged(int value) => _ = CarregarAsync();

    public IReadOnlyList<int> OpcoesDias { get; } = new[] { 7, 15, 30, 60, 90 };

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var produtos = (await _produtos.ListarComTamanhosAsync(incluirInativos: false)).ToList();
        var controlados = produtos.Where(p => !p.VenderSemBaixa).ToList();

        ProdutosAtivos = produtos.Count.ToString();
        PecasEmEstoque = controlados.Sum(p => p.EstoqueAtual).ToString("N0");
        ValorCusto = Dinheiro.Formatar(controlados.Sum(p => p.PrecoCustoCentavos * Math.Max(0, p.EstoqueAtual)));
        ValorVenda = Dinheiro.Formatar(controlados.Sum(p => p.PrecoVendaCentavos * Math.Max(0, p.EstoqueAtual)));
        var baixos = controlados.Where(p => p.EstoqueAtual > 0 && p.EstoqueAtual < p.EstoqueMinimo).ToList();
        var esgotados = controlados.Where(p => p.EstoqueAtual <= 0).ToList();
        QtdBaixo = baixos.Count.ToString();
        QtdEsgotado = esgotados.Count.ToString();

        Preencher(PorCategoria, Barras(controlados
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Categoria) ? "Sem categoria" : p.Categoria!)
            .Select(g => (Nome: g.Key, Valor: g.Sum(p => p.EstoqueAtual)))
            .OrderByDescending(x => x.Valor).Take(10), "peça(s)"));
        Preencher(MaioresEstoques, Barras(controlados.OrderByDescending(p => p.EstoqueAtual).Take(10)
            .Select(p => (Nome: p.Nome, Valor: p.EstoqueAtual)), "peça(s)"));

        // A barra mostra o estoque como % do mínimo (vermelha: zerado ou abaixo do mínimo).
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
    });

    private static IEnumerable<BarraEstoque> Barras(IEnumerable<(string Nome, int Valor)> itens, string unidade)
    {
        var lista = itens.ToList();
        var maior = Math.Max(1, lista.Count == 0 ? 1 : lista.Max(i => i.Valor));
        return lista.Select(i => new BarraEstoque(i.Nome, $"{i.Valor:N0} {unidade}", Math.Max(2, 100.0 * Math.Max(0, i.Valor) / maior)));
    }

    private static void Preencher(ObservableCollection<BarraEstoque> alvo, IEnumerable<BarraEstoque> novos)
    {
        alvo.Clear();
        foreach (var b in novos) alvo.Add(b);
    }
}
