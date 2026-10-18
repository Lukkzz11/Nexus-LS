using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class VendasView : UserControl
{
    public VendasView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VendasViewModel vm)
                vm.Confirmar = mensagem =>
                    MessageBox.Show(Window.GetWindow(this), mensagem, "Nexus LS",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        };
    }
}
