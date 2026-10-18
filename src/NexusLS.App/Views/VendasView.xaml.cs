using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class VendasView : UserControl
{
    public VendasView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VendasViewModel vm)
            {
                vm.Confirmar = mensagem =>
                    MessageBox.Show(Window.GetWindow(this), mensagem, "Nexus LS",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                vm.EscolherArquivoParaSalvar = SalvarComo;
            }
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
