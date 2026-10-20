using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class ComprasView : UserControl
{
    public ComprasView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not ComprasViewModel vm) return;
            vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Cancelar compra", pergunta);
        };
        Loaded += (_, _) => CaixaProduto.Focus();
    }

    private void AoDarDoisCliques(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ComprasViewModel vm && vm.AdicionarProdutoCommand.CanExecute(null))
            vm.AdicionarProdutoCommand.Execute(null);
    }
}
