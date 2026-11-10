using NexusLS.App.ViewModels;
using Wpf.Ui.Controls;

namespace NexusLS.App;

/// <summary>
/// A janela principal usa a FluentWindow da biblioteca WPF-UI: barra de título e fundo (Mica) no estilo do Windows 11.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private bool _semTravaDeCaixa;

    public MainWindow()
    {
        InitializeComponent();
        // DataContext = "de onde a tela tira os dados do binding". Aqui, o MainViewModel.
        var vm = new MainViewModel(App.Servicos);
        vm.PedirLogin = () => Views.LoginWindow.Pedir(App.Servicos.Funcionarios, this);
        DataContext = vm;
        vm.EscolherArquivoDeLicenca = () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Licença do Nexus|*.json", Title = "Escolha o arquivo de licença" };
            return d.ShowDialog(this) == true ? d.FileName : null;
        };
        vm.AtualizarLicenca();
        vm.AoDetectarLicencaInvalidaNaNuvem = mensagem =>
        {
            Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(this,
                    $"Atenção: {mensagem}\n\nO Atualizador / Ativador de Licença será aberto para regularização.",
                    "Licença Nexus LS",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                vm.AbrirAtualizador();
                _semTravaDeCaixa = true;   // licença inválida: sai de qualquer jeito
                Close();
            });
        };
        Loaded += async (_, _) =>
        {
            await vm.VerificarLicencaNuvemAsync();
            var timerLicenca = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            timerLicenca.Tick += async (_, _) => await vm.VerificarLicencaNuvemAsync();
            timerLicenca.Start();
        };
        // Fechar o app com o caixa aberto não é permitido: leva o operador para a conferência do caixa.
        Closing += (_, e) =>
        {
            if (_semTravaDeCaixa) return;
            try
            {
                var aberto = Task.Run(() => App.Servicos.Caixa.ObterAbertoAsync()).GetAwaiter().GetResult();
                if (aberto is null) return;
                e.Cancel = true;
                System.Windows.MessageBox.Show(this,
                    "O caixa ainda está aberto. Feche o caixa (conferência do dia) antes de sair do Nexus.",
                    "Fechar o caixa", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                vm.IrParaCaixaCommand.Execute(null);
            }
            catch (Exception)
            {
                // Sem conseguir consultar o caixa, nunca prende o operador dentro do programa.
            }
        };
        vm.AbrirConexaoBanco = () => new Views.ConexaoBancoWindow(App.Servicos.Firebase, App.Servicos.Operacional) { Owner = this }.ShowDialog() == true;
        vm.AtualizarBanco();

        // Atualização: as perguntas ficam aqui (janela); a consulta ao abrir é silenciosa e nunca atrapalha o uso.
        vm.Atualizacao.Confirmar = texto =>
            System.Windows.MessageBox.Show(this, texto, "Atualizar o Nexus", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
        vm.Atualizacao.MostrarNotas = (titulo, texto) =>
            System.Windows.MessageBox.Show(this, texto, titulo, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        Loaded += async (_, _) => await vm.Atualizacao.VerificarAoAbrirAsync();

        // Sincronização: o login do Firebase é opcional ("Continuar sem entrar"); a fila espera até alguém entrar.
        vm.Sincronizacao.PedirLogin = () => Views.LoginFirebaseWindow.Pedir(App.Servicos.Login, this);
        Loaded += async (_, _) => await vm.Sincronizacao.IniciarAsync();
    }
}
