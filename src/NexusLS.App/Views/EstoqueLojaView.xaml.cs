using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class EstoqueLojaView : UserControl
{
    public EstoqueLojaView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not EstoqueLojaViewModel vm) return;
            // Caixas de janela ficam aqui na View, não no ViewModel.
            vm.EscolherArquivoParaAbrir = () =>
            {
                var dialogo = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Escolha a planilha de produtos",
                    Filter = "Planilha CSV|*.csv;*.txt|Todos os arquivos|*.*"
                };
                return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
            };
            vm.ConfirmarImportacao = texto =>
                MessageBox.Show(Window.GetWindow(this), texto, "Importar produtos", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            vm.InformarImportacao = texto =>
                MessageBox.Show(Window.GetWindow(this), texto, "Importar produtos", MessageBoxButton.OK, MessageBoxImage.Information);
        };
    }
}
