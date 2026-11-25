using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NexusLS.BarTender.Views.Designer;

public partial class RibbonDesigner : UserControl
{
    public RibbonDesigner()
    {
        InitializeComponent();
    }

    private void AbrirMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } botao) return;
        menu.PlacementTarget = botao;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
