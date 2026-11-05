using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Ranking dos produtos mais vendidos num período (vendas canceladas não contam).</summary>
public partial class MaisVendidosViewModel : ViewModelBase
{
    private readonly RelatorioService _relatorio;
    private int _versao;

    public MaisVendidosViewModel(RelatorioService relatorio, DateTime? hoje = null)
    {
        _relatorio = relatorio;
        var dia = (hoje ?? DateTime.Today).Date;
        _de = new DateTime(dia.Year, dia.Month, 1); // campos diretos: não disparam o carregamento duas vezes
        _ate = dia;
        _ = CarregarAsync();
    }

    /// <summary>A tela define a caixa "Salvar como" (ver VendasViewModel).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    public ObservableCollection<ProdutoMaisVendido> Ranking { get; } = new();

    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;
    [ObservableProperty] private bool _porTamanho;
    [ObservableProperty] private string _resumo = "";

    // Pizzas (como no ERP): os 6 que mais faturaram + "Outros", por categoria e por forma de pagamento. Sem canceladas.
    public ObservableCollection<FatiaDesenhada> PizzaProdutos { get; } = new();
    public ObservableCollection<FatiaDesenhada> PizzaCategorias { get; } = new();
    public ObservableCollection<FatiaDesenhada> PizzaFormas { get; } = new();
    [ObservableProperty] private bool _temPizzas;

    private bool _definindoPeriodo; // true enquanto um atalho troca as duas datas de uma vez (carrega só no fim)

    partial void OnDeChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnAteChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnPorTamanhoChanged(bool value) => _ = CarregarAsync();

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");

        List<ProdutoMaisVendido> lista;
        RelatorioVendas vendas;
        try
        {
            lista = await _relatorio.MaisVendidosAsync(de, ate, PorTamanho);
            vendas = await _relatorio.VendasPorPeriodoAsync(de, ate);
        }
        catch (RegraDeNegocioException)
        {
            if (versao == _versao)
            {
                Ranking.Clear(); Resumo = "";
                PizzaProdutos.Clear(); PizzaCategorias.Clear(); PizzaFormas.Clear(); TemPizzas = false;
            }
            throw; // a mensagem (ex.: data final antes da inicial) aparece na tela
        }
        if (versao != _versao) return;

        Ranking.Clear();
        foreach (var p in lista) Ranking.Add(p);

        Preencher(PizzaProdutos, lista.GroupBy(p => p.ProdutoId).Select(g => (g.OrderBy(p => p.Posicao).First().Nome, g.Sum(p => p.FaturamentoCentavos))));
        Preencher(PizzaCategorias, (vendas.PorCategoria ?? Array.Empty<ResumoCategoria>()).Select(c => (c.Categoria, c.FaturamentoCentavos)));
        Preencher(PizzaFormas, vendas.PorForma.Select(f => (SelecaoPagamento.Nome(f.Forma), f.TotalCentavos)));
        TemPizzas = PizzaProdutos.Count > 0;

        Mensagem = "";
        Resumo = lista.Count == 0
            ? "Nenhuma venda neste período."
            : $"{lista.Count} {(PorTamanho ? "linha(s)" : "produto(s)")} · {lista.Sum(p => p.Quantidade)} unidade(s) · " +
              $"Faturamento {Dinheiro.Formatar(lista.Sum(p => p.FaturamentoCentavos))}";
    });

    private static void Preencher(ObservableCollection<FatiaDesenhada> destino, IEnumerable<(string Rotulo, long Valor)> itens)
    {
        destino.Clear();
        foreach (var f in PizzaGrafico.Desenhar(Participacao.Fatias(itens))) destino.Add(f);
    }

    // Atalhos de período
    [RelayCommand] private void Hoje() => Definir(DateTime.Today, DateTime.Today);
    [RelayCommand] private void UltimosSeteDias() => Definir(DateTime.Today.AddDays(-6), DateTime.Today);
    [RelayCommand] private void MesAtual() => Definir(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);


    private void Definir(DateTime de, DateTime ate)
    {
        _definindoPeriodo = true;
        De = de;
        Ate = ate;
        _definindoPeriodo = false;
        _ = CarregarAsync();
    }

    /// <summary>Salva o ranking mostrado num CSV para abrir no Excel.</summary>
    [RelayCommand]
    private Task ExportarAsync() => ExecutarAsync(() =>
    {
        if (Ranking.Count == 0)
            throw new RegraDeNegocioException("Não há nada para exportar neste período.");

        var periodo = $"{De:yyyy-MM-dd}_a_{Ate:yyyy-MM-dd}";
        var caminho = EscolherArquivoParaSalvar($"mais-vendidos-{periodo}.csv");
        if (caminho is null) return Task.CompletedTask;

        ExportadorCsv.Salvar(caminho, ExportadorCsv.MaisVendidos(Ranking.ToList(), PorTamanho));
        MostrarSucesso($"Arquivo salvo: {caminho}");
        return Task.CompletedTask;
    });
}
