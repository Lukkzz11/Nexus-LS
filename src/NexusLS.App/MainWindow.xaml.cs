using NexusLS.App.ViewModels;
using Wpf.Ui.Controls;

namespace NexusLS.App;

/// <summary>
/// A janela principal usa a FluentWindow da biblioteca WPF-UI: barra de título e fundo (Mica) no estilo do Windows 11.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private bool _semTravaDeCaixa;
    private DateTime _ultimaAtividade = DateTime.Now;

    /// <summary>
    /// Depois de alguns minutos sem mouse nem teclado (se a preferência estiver ligada), fecha a Gestão e pede o PIN de novo.
    /// Só vale com login ativo; sem funcionários cadastrados não há quem bloquear.
    /// </summary>
    private void IniciarBloqueioPorInatividade(ViewModels.MainViewModel vm)
    {
        System.Windows.Input.InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is System.Windows.Input.MouseEventArgs or System.Windows.Input.KeyboardEventArgs)
                _ultimaAtividade = DateTime.Now;
        };
        var relogio = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        var bloqueando = false;
        relogio.Tick += (_, _) =>
        {
            if (bloqueando || !Seguranca.Sessao.LoginAtivo || Seguranca.Sessao.Atual is null) return;
            if (!NexusLS.Core.Seguranca.RegrasSeguranca.Ocioso(_ultimaAtividade, DateTime.Now, App.Servicos.Seguranca.Carregar().MinutosInatividade)) return;

            // A Gestão devolve a sessão do balcão ao fechar; por isso ela fecha antes de bloquear.
            foreach (var g in System.Windows.Application.Current.Windows.OfType<Views.GestaoWindow>().ToList()) g.Close();
            // Outra janela aberta (Pix, impressão, diálogo): espera ela terminar em vez de empilhar o PIN por cima.
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().Any(w => w != this && w.IsVisible)) return;

            bloqueando = true;
            try
            {
                Seguranca.Sessao.Sair();
                do { vm.TrocarUsuarioCommand.Execute(null); } while (Seguranca.Sessao.Atual is null);
            }
            finally
            {
                _ultimaAtividade = DateTime.Now;
                bloqueando = false;
            }
        };
        relogio.Start();
    }

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
        vm.AbrirJanelaGestao = item => Views.GestaoWindow.Abrir(App.Servicos, this, item, vm.Zoom, z => vm.Zoom = z, () => vm.LicencaTexto);
        vm.AtualizarLicenca();
        IniciarBloqueioPorInatividade(vm);
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

        Loaded += async (_, _) => await AvisarAoAbrirAsync(vm);

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

    /// <summary>
    /// Ao abrir, junta os avisos ligados (estoque baixo, contas a pagar, backup atrasado) numa janela só, uma vez.
    /// Nunca atrapalha a abertura: se algo falhar, o Nexus abre normalmente.
    /// </summary>
    private async Task AvisarAoAbrirAsync(ViewModels.MainViewModel vm)
    {
        try
        {
            var cfg = App.Servicos.Seguranca.Carregar();
            var linhas = new List<string>();

            var baixos = new List<NexusLS.Core.Models.Produto>();
            if (cfg.AvisarEstoqueBaixoAoAbrir && Seguranca.Sessao.Pode(NexusLS.Core.Models.Permissao.Estoque))
                baixos = await App.Servicos.Estoque.ListarAbaixoDoMinimoAsync();
            if (baixos.Count > 0)
            {
                linhas.Add($"Estoque baixo: {baixos.Count} produto(s) abaixo do mínimo.");
                linhas.AddRange(baixos.Take(5).Select(p => $"    • {p.Nome} (estoque {p.EstoqueAtual}, mínimo {p.EstoqueMinimo})"));
                if (baixos.Count > 5) linhas.Add($"    ... e mais {baixos.Count - 5}.");
            }

            if (cfg.AvisarContasAoAbrir && Seguranca.Sessao.Pode(NexusLS.Core.Models.Permissao.Financeiro))
            {
                var contas = await App.Servicos.Financeiro.ListarContasAPagarProximasAsync(cfg.DiasAvisoContas);
                var frase = NexusLS.Core.Services.AvisosAoAbrir.Contas(contas, cfg.DiasAvisoContas, DateTime.Today);
                if (frase is not null) linhas.Add(frase);
            }

            if (cfg.AvisarBackupAtrasado)
            {
                var frase = NexusLS.Core.Services.AvisosAoAbrir.Backup(App.Servicos.Backup.Configuracao.UltimoBackup, DateTime.Now, cfg.DiasBackupAtrasado);
                if (frase is not null) linhas.Add(frase);
            }

            if (linhas.Count == 0) return;
            var texto = string.Join("\n", linhas);
            if (baixos.Count > 0)
            {
                var r = System.Windows.MessageBox.Show(this, texto + "\n\nAbrir o estoque agora?", "Avisos do Nexus",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                if (r == System.Windows.MessageBoxResult.Yes) vm.IrParaEstoqueCommand.Execute(null);
            }
            else
            {
                System.Windows.MessageBox.Show(this, texto, "Avisos do Nexus", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception)
        {
            // Os avisos são só lembretes: se não der para consultar, o Nexus abre normalmente.
        }
    }
}
