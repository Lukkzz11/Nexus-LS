using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class CalculadoraPrecosView : UserControl
{
    public CalculadoraPrecosView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is CalculadoraPrecosViewModel vm)
                vm.Confirmar = texto =>
                    MessageBox.Show(Window.GetWindow(this), texto, "Calculadora de preços", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        };
    }
}
