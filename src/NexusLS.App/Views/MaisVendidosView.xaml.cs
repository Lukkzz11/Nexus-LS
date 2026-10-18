using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class MaisVendidosView : UserControl
{
    public MaisVendidosView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is MaisVendidosViewModel vm) vm.EscolherArquivoParaSalvar = SalvarComo;
        };
    }

    /// <summary>A caixa "Salvar como" do Windows. Devolve o caminho escolhido ou nulo se cancelou.</summary>
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
