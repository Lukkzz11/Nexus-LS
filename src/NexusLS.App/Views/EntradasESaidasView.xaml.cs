using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class EntradasESaidasView : UserControl
{
    public EntradasESaidasView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not EntradasESaidasViewModel vm) return;
            vm.EscolherArquivoAnexo = DialogosGestao.EscolherAnexo(this);
            vm.Confirmar = DialogosGestao.Confirmar(this);
            vm.AbrirArquivo = DialogosGestao.AbrirArquivo;
        };
    }
}
