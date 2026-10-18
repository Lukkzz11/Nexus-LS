using System.Windows;
using System.Windows.Controls;
using PdvSimples.App.ViewModels;

namespace PdvSimples.App.Views;

public partial class VendasView : UserControl
{
    public VendasView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VendasViewModel vm)
                vm.Confirmar = mensagem =>
                    MessageBox.Show(Window.GetWindow(this), mensagem, "PDV Simples",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        };
    }
}
