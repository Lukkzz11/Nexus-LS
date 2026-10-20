using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class CartoesView : UserControl
{
    public CartoesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is CartoesViewModel vm)
                vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Estornar liquidação", pergunta);
        };
    }
}
