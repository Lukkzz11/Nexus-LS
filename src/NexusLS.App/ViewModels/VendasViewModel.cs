using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public partial class VendasViewModel : ViewModelBase
{
    private readonly VendaService _vendas;
    private readonly CupomService _cupons;
    private int _versao;

    public VendasViewModel(VendaService vendas, CupomService cupons)
    {
        _vendas = vendas;
        _cupons = cupons;
        _data = DateTime.Today; // campo direto: não dispara o carregamento duas vezes
        _ = CarregarAsync();
    }

    /// <summary>A tela define como perguntar "tem certeza?" (ver PdvViewModel).</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    public ObservableCollection<Venda> Vendas { get; } = new();

    [ObservableProperty] private DateTime? _data;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeCancelar))]
    [NotifyPropertyChangedFor(nameof(TemSelecao))]
    [NotifyCanExecuteChangedFor(nameof(CancelarVendaCommand))]
    private Venda? _selecionada;

    [ObservableProperty] private string _resumo = "";

    public bool TemSelecao => Selecionada is not null;
    public bool PodeCancelar => Selecionada is { Cancelada: false };

    partial void OnDataChanged(DateTime? value) => _ = CarregarAsync();

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        var idSelecionado = Selecionada?.Id;

        var lista = await _vendas.ListarDoDiaAsync(Data ?? DateTime.Today);
        if (versao != _versao) return;

        Vendas.Clear();
        foreach (var v in lista) Vendas.Add(v);

        var validas = lista.Count(v => !v.Cancelada);
        var canceladas = lista.Count - validas;
        Resumo = $"{validas} venda(s) · Total: {Dinheiro.Formatar(VendaService.TotalValido(lista))}"
                 + (canceladas > 0 ? $" · {canceladas} cancelada(s) (não somadas)" : "");

        if (idSelecionado is int id) Selecionada = Vendas.FirstOrDefault(v => v.Id == id);
    });

    /// <summary>Imprime de novo o cupom da venda selecionada (se a venda foi cancelada, o cupom sai marcado como cancelado).</summary>
    [RelayCommand]
    private Task ReimprimirCupomAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { } venda) return;
        await _cupons.ImprimirAsync(venda.Id);
        MostrarSucesso($"Cupom da venda #{venda.Id} enviado para a impressora.");
    });

    [RelayCommand(CanExecute = nameof(PodeCancelar))]
    private Task CancelarVendaAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { Cancelada: false } venda) return;

        if (!Confirmar($"Cancelar a venda #{venda.Id} de {Dinheiro.Formatar(venda.TotalCentavos)}?\nO estoque dos itens será devolvido."))
            return;

        await _vendas.CancelarAsync(venda.Id);
        await CarregarAsync();
        MostrarSucesso($"Venda #{venda.Id} cancelada e estoque devolvido.");
    });
}
