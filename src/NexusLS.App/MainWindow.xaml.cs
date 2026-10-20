using NexusLS.App.ViewModels;
using Wpf.Ui.Controls;

namespace NexusLS.App;

/// <summary>
/// A janela principal usa a FluentWindow da biblioteca WPF-UI: barra de título e fundo (Mica) no estilo do Windows 11.
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // DataContext = "de onde a tela tira os dados do binding". Aqui, o MainViewModel.
        var vm = new MainViewModel();
        vm.PedirLogin = () => Views.LoginWindow.Pedir(Servicos.Funcionarios, this);
        DataContext = vm;

        // Atualização: as perguntas ficam aqui (janela); a consulta ao abrir é silenciosa e nunca atrapalha o uso.
        vm.Atualizacao.Confirmar = texto =>
            System.Windows.MessageBox.Show(this, texto, "Atualizar o Nexus LS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
        vm.Atualizacao.MostrarNotas = (titulo, texto) =>
            System.Windows.MessageBox.Show(this, texto, titulo, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        Loaded += async (_, _) => await vm.Atualizacao.VerificarAoAbrirAsync();
    }
}
