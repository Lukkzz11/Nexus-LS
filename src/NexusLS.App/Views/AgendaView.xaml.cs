using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class AgendaView : UserControl
{
    public AgendaView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is AgendaViewModel vm)
                vm.Confirmar = texto =>
                    MessageBox.Show(Window.GetWindow(this), texto, "Agenda", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        };
    }
}
