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
    }
}
