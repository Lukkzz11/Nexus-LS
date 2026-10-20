using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class ChequesView : UserControl
{
    public ChequesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ChequesViewModel vm)
                vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Cheque", pergunta);
        };
    }
}
