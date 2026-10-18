using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Um "azulejo" selecionável dos modais do PDV (um tamanho, uma forma de pagamento, uma parcela...).
/// Só guarda o texto e se está selecionado; quem decide a seleção é o fluxo, pelo teclado.
/// </summary>
public partial class OpcaoViewModel : ObservableObject
{
    public OpcaoViewModel(string texto, string? detalhe = null, bool esgotada = false)
    {
        Texto = texto;
        Detalhe = detalhe;
        Esgotada = esgotada;
    }

    public string Texto { get; }
    public string? Detalhe { get; }
    public bool Esgotada { get; }

    [ObservableProperty] private bool _selecionada;
}
