using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class PedidosView : UserControl
{
    public PedidosView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not PedidosViewModel vm) return;
            vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(System.Windows.Window.GetWindow(this), "Cancelar pedido", pergunta);
        };
    }

    private void AoDarDoisCliques(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PedidosViewModel vm && vm.AdicionarProdutoCommand.CanExecute(null))
            vm.AdicionarProdutoCommand.Execute(null);
    }
}
