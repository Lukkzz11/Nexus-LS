using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class VendaClienteView : UserControl
{
    public VendaClienteView() => InitializeComponent();

    private void CampoBusca_Loaded(object sender, RoutedEventArgs e) => CampoBusca.Focus();

    /// <summary>Enter escolhe o cliente destacado; ↑ ↓ andam pela lista sem sair da busca.</summary>
    private void CampoBusca_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not VendaClienteViewModel vm) return;
        switch (e.Key)
        {
            case Key.Enter when vm.VenderCommand.CanExecute(null):
                vm.VenderCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down or Key.Up:
                var i = vm.Selecionado is null ? -1 : vm.Clientes.IndexOf(vm.Selecionado);
                var novo = Math.Clamp(i + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, vm.Clientes.Count - 1));
                if (vm.Clientes.Count > 0) vm.Selecionado = vm.Clientes[novo];
                e.Handled = true;
                break;
        }
    }

    private void Lista_DuploClique(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is VendaClienteViewModel vm && vm.Selecionado is not null && vm.VenderCommand.CanExecute(null))
            vm.VenderCommand.Execute(null);
    }
}
