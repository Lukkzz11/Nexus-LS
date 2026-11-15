using System.Windows;
using System.Windows.Controls;
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
                vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Cancelar troca", pergunta);
            CaixaNumero.Focus();
        };
    }
}
