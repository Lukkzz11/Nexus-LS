using NexusLS.Core.Vitrine;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Editor do catálogo e pré-visualização do catálogo: telas em JSX (pasta <c>web\catalogo</c>) dentro do WebView2.
/// Este ViewModel só entrega o serviço à ponte; a lógica de dados fica em <see cref="ServicoCatalogoWeb"/>.
/// </summary>
public class CatalogoWebViewModel : ViewModelBase
{
    public CatalogoWebViewModel(ServicoCatalogoWeb servico) => Servico = servico;
    public ServicoCatalogoWeb Servico { get; }
}
