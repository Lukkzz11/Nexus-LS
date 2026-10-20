using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class ContasFinanceirasView : UserControl
{
    public ContasFinanceirasView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ContasFinanceirasViewModel vm)
                vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Cancelar transferência", pergunta);
        };
    }
}
