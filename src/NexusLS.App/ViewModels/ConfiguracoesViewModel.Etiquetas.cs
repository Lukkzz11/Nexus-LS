using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Etiquetas;

namespace NexusLS.App.ViewModels;

/// <summary>Página "Etiquetas": o formato de papel e o tipo de preço com que a tela de Etiquetas já abre.</summary>
public partial class ConfiguracoesViewModel
{
    public ObservableCollection<string> FormatosEtiqueta { get; } = new();

    [ObservableProperty] private string? _formatoEtiquetaPadrao;
    [ObservableProperty] private bool _etiquetaPrecoAtacado;
    [ObservableProperty] private string _mensagemEtiquetas = "";
    [ObservableProperty] private bool _mensagemEtiquetasEhErro;

    public void UsarEtiquetas(ModeloEtiquetaStore modelos)
    {
        FormatosEtiqueta.Clear();
        foreach (var f in FormatoEtiqueta.Todos) FormatosEtiqueta.Add(f.Nome);
        try { foreach (var f in modelos.Carregar()) if (!FormatosEtiqueta.Contains(f.Nome)) FormatosEtiqueta.Add(f.Nome); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { /* sem os modelos próprios, ficam só os de fábrica */ }

        if (_seguranca is null) return;
        var c = _seguranca.Carregar();
        FormatoEtiquetaPadrao = !string.IsNullOrWhiteSpace(c.EtiquetaFormatoPadrao) && FormatosEtiqueta.Contains(c.EtiquetaFormatoPadrao)
            ? c.EtiquetaFormatoPadrao : FormatoEtiqueta.Bobina50x30.Nome;
        EtiquetaPrecoAtacado = c.EtiquetaPrecoAtacadoPadrao;
    }

    [RelayCommand]
    private void SalvarEtiquetas() => SalvarRegras(
        c =>
        {
            c.EtiquetaFormatoPadrao = FormatoEtiquetaPadrao;
            c.EtiquetaPrecoAtacadoPadrao = EtiquetaPrecoAtacado;
        },
        (m, erro) => { MensagemEtiquetasEhErro = erro; MensagemEtiquetas = m; },
        () => "Salvo. A tela de Etiquetas já abre assim na próxima vez.");
}
