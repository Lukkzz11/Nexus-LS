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
        var vm = new MainViewModel(App.Servicos);
        vm.PedirLogin = () => Views.LoginWindow.Pedir(App.Servicos.Funcionarios, this);
        DataContext = vm;

        // Atualização: as perguntas ficam aqui (janela); a consulta ao abrir é silenciosa e nunca atrapalha o uso.
        vm.Atualizacao.Confirmar = texto =>
            System.Windows.MessageBox.Show(this, texto, "Atualizar o Nexus LS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
        vm.Atualizacao.MostrarNotas = (titulo, texto) =>
            System.Windows.MessageBox.Show(this, texto, titulo, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        Loaded += async (_, _) => await vm.Atualizacao.VerificarAoAbrirAsync();

        // Sincronização: o login do Firebase é opcional ("Continuar sem entrar"); a fila espera até alguém entrar.
        vm.Sincronizacao.PedirLogin = () => Views.LoginFirebaseWindow.Pedir(App.Servicos.Login, this);
        Loaded += async (_, _) => await vm.Sincronizacao.IniciarAsync();
    }
}
