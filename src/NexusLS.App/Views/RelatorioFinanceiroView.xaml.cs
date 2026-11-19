using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class RelatorioFinanceiroView : UserControl
{
    public RelatorioFinanceiroView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is RelatorioFinanceiroViewModel vm) vm.EscolherArquivoParaSalvar = DialogosGestao.SalvarCsv(this);
        };
    }
}
