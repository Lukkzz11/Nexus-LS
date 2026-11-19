using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class RelatorioProdutosView : UserControl
{
    public RelatorioProdutosView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is RelatorioProdutosViewModel vm) vm.EscolherArquivoParaSalvar = DialogosGestao.SalvarCsv(this);
        };
    }
}
