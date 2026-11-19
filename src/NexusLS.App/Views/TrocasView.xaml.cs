using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class TrocasView : UserControl
{
    public TrocasView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is TrocasViewModel vm)
            {
                vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Cancelar troca", pergunta);
                vm.MostrarDetalhe = detalhe => VendaDetalheWindow.Mostrar(Window.GetWindow(this), detalhe);
            }
            CaixaNumero.Focus();
        };
    }

    private void GradeVendas_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // só abre quando o duplo clique foi numa linha (não no cabeçalho nem na barra de rolagem)
        if (e.OriginalSource is DependencyObject origem && ItemsControl.ContainerFromElement(GradeVendas, origem) is DataGridRow
            && DataContext is TrocasViewModel vm && vm.AbrirVendaCommand.CanExecute(null))
            vm.AbrirVendaCommand.Execute(null);
    }
}
