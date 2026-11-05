using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class ChequesEmitidosView : UserControl
{
    public ChequesEmitidosView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ChequesEmitidosViewModel vm)
                vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Cheque emitido", pergunta);
        };
    }
}
