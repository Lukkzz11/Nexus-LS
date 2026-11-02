using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class DesempenhoVendasView : UserControl
{
    public DesempenhoVendasView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is DesempenhoVendasViewModel vm) vm.EscolherArquivoParaSalvar = SalvarComo;
        };
    }

    private string? SalvarComo(string nomeSugerido)
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            FileName = nomeSugerido,
            DefaultExt = ".csv",
            Filter = "Planilha CSV (Excel)|*.csv"
        };
        return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
    }
}
