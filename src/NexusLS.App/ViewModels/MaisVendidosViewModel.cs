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
        try
        {
            lista = await _relatorio.MaisVendidosAsync(de, ate, PorTamanho);
        }
        catch (RegraDeNegocioException)
        {
            if (versao == _versao) { Ranking.Clear(); Resumo = ""; }
            throw; // a mensagem (ex.: data final antes da inicial) aparece na tela
        }
        if (versao != _versao) return;

        Ranking.Clear();
        foreach (var p in lista) Ranking.Add(p);

        Mensagem = "";
        Resumo = lista.Count == 0
            ? "Nenhuma venda neste período."
            : $"{lista.Count} {(PorTamanho ? "linha(s)" : "produto(s)")} · {lista.Sum(p => p.Quantidade)} unidade(s) · " +
              $"Faturamento {Dinheiro.Formatar(lista.Sum(p => p.FaturamentoCentavos))}";
    });

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
