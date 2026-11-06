using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class ClientesView : UserControl
{
    public ClientesView()
    {
        InitializeComponent();

        // As caixas "Abrir" e "Salvar como" do Windows ficam na tela; o ViewModel só pede o caminho.
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not ClientesViewModel vm) return;
            vm.EscolherArquivoParaAbrir = () =>
            {
                var dialogo = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Escolha a planilha de clientes",
                    Filter = "Planilha CSV|*.csv;*.txt|Todos os arquivos|*.*"
                };
                return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
            };
            vm.EscolherArquivoParaSalvar = nome =>
            {
                var dialogo = new Microsoft.Win32.SaveFileDialog { FileName = nome, DefaultExt = ".csv", Filter = "Planilha CSV (Excel)|*.csv" };
                return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
            };
        };
    }
}
