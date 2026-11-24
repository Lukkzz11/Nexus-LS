using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class OrcamentosView : UserControl
{
    public OrcamentosView() => InitializeComponent();

    private void AoDarDoisCliques(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrcamentosViewModel vm) vm.AdicionarProdutoCommand.Execute(null);
    }
}
