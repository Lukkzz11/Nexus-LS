using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class FinanceiroView : UserControl
{
    public FinanceiroView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not FinanceiroViewModel vm) return;
            // Caixas de janela ficam aqui na View, não no ViewModel.
            vm.EscolherArquivoParaSalvar = nome =>
            {
                var dialogo = new Microsoft.Win32.SaveFileDialog { FileName = nome, DefaultExt = ".csv", Filter = "Planilha CSV (Excel)|*.csv" };
                return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
            };
            vm.Confirmar = texto =>
                MessageBox.Show(Window.GetWindow(this), texto, "Financeiro", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(Window.GetWindow(this), "Cancelar conta", pergunta);
        };
    }
}
