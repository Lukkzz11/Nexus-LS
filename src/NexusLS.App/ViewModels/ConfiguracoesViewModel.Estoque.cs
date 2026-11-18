using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Página "Estoque e produtos": aviso de estoque baixo ao abrir e a situação atual.</summary>
public partial class ConfiguracoesViewModel
{
    private EstoqueService? _estoqueAviso;

    public bool TemEstoqueConfig => _estoqueAviso is not null && _seguranca is not null;

    [ObservableProperty] private bool _avisarEstoqueBaixo = true;
    [ObservableProperty] private string _mensagemEstoque = "";
    [ObservableProperty] private bool _mensagemEstoqueEhErro;
    [ObservableProperty] private string _resumoEstoqueBaixo = "";

    public ObservableCollection<string> ListaEstoqueBaixo { get; } = new();

    public void UsarEstoque(EstoqueService estoque)
    {
        _estoqueAviso = estoque;
        if (_seguranca is not null) AvisarEstoqueBaixo = _seguranca.Carregar().AvisarEstoqueBaixoAoAbrir;
        OnPropertyChanged(nameof(TemEstoqueConfig));
        _ = AtualizarEstoqueBaixoAsync();
    }

    [RelayCommand]
    private void SalvarEstoque() => SalvarRegras(
        c => c.AvisarEstoqueBaixoAoAbrir = AvisarEstoqueBaixo,
        (m, erro) => { MensagemEstoqueEhErro = erro; MensagemEstoque = m; },
        () => AvisarEstoqueBaixo ? "Salvo. O Nexus avisa ao abrir se houver produto abaixo do mínimo." : "Salvo. O aviso ao abrir está desligado.");

    [RelayCommand]
    private async Task AtualizarEstoqueBaixoAsync()
    {
        if (_estoqueAviso is null) return;
        try
        {
            var baixos = await _estoqueAviso.ListarAbaixoDoMinimoAsync();
            ListaEstoqueBaixo.Clear();
            foreach (var p in baixos.Take(15))
                ListaEstoqueBaixo.Add($"{p.Nome} - estoque {p.EstoqueAtual}, mínimo {p.EstoqueMinimo}");
            ResumoEstoqueBaixo = baixos.Count == 0
                ? "Nenhum produto abaixo do mínimo."
                : baixos.Count <= 15
                    ? $"{baixos.Count} produto(s) abaixo do mínimo:"
                    : $"{baixos.Count} produtos abaixo do mínimo. Os 15 mais críticos:";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ResumoEstoqueBaixo = "Não foi possível consultar o estoque: " + ex.Message;
        }
    }
}
