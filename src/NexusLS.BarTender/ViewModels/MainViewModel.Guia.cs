using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel
{
    /// <summary>O passo a passo de impressão aparece sozinho na primeira vez e depois só pelo botão "Como imprimir".</summary>
    [ObservableProperty]
    private bool _mostrarGuiaImpressao = !PreferenciasBarTender.Carregar().GuiaImpressaoVisto;

    [RelayCommand]
    private void AlternarGuiaImpressao()
    {
        if (MostrarGuiaImpressao) FecharGuiaImpressao();
        else MostrarGuiaImpressao = true;
    }

    [RelayCommand]
    private void FecharGuiaImpressao()
    {
        MostrarGuiaImpressao = false;

        var preferencias = PreferenciasBarTender.Carregar();
        if (preferencias.GuiaImpressaoVisto) return;
        preferencias.GuiaImpressaoVisto = true;
        preferencias.Salvar();
    }
}
