using System.Windows;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Backup;
using NexusLS.Core.Data;

namespace NexusLS.App;

public partial class App : Application
{
    /// <summary>Opções do banco compartilhadas pelo app (aponta para %LOCALAPPDATA%\NexusLS\pdv.db).</summary>
    public static DbContextOptions<PdvDbContext> OpcoesBanco { get; } =
        new DbContextOptionsBuilder<PdvDbContext>()
            // "Default Timeout": se o banco estiver ocupado, espera até 30 s em vez de falhar na hora com "banco bloqueado".
            .UseSqlite($"Data Source={CaminhoBanco.Padrao()};Default Timeout=30")
            // Todo produto salvo entra na fila de sincronização na mesma transação (o envio é feito à parte).
            .AddInterceptors(new FilaSincronizacaoInterceptor(() => UsuarioUidAtual))
            .Options;

    /// <summary>Uid do Firebase do usuário logado (nulo enquanto não houver tela de login).</summary>
    public static string? UsuarioUidAtual { get; set; }

    private static ContainerDeServicos? _servicos;

    /// <summary>Os serviços do programa, criados uma vez só (raiz de composição). Quem precisa deles os recebe pelo construtor.</summary>
    public static ContainerDeServicos Servicos => _servicos ??= new ContainerDeServicos(OpcoesBanco);

    // Trava de instância única: dois Nexus abertos gravariam no mesmo banco ao mesmo tempo.
    private static Mutex? _instanciaUnica;

    /// <summary>
    /// Só um Nexus por usuário do Windows. Espera até 15 s porque, numa atualização, o programa novo abre
    /// enquanto o antigo ainda está terminando de fechar.
    /// </summary>
    private static bool ObterInstanciaUnica()
    {
        _instanciaUnica = new Mutex(false, @"Local\NexusLS.InstanciaUnica");
        try
        {
            return _instanciaUnica.WaitOne(TimeSpan.FromSeconds(15));
        }
        catch (AbandonedMutexException)
        {
            return true; // a instância anterior caiu sem soltar a trava: seguimos normalmente
        }
    }

    /// <summary>
    /// OnStartup roda uma vez quando o programa abre. Migrate() cria o arquivo do banco
    /// se não existir e aplica as migrations pendentes (as "versões" da estrutura das tabelas).
    /// Precisa vir ANTES do base.OnStartup, que é quem abre a janela principal.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!ObterInstanciaUnica())
        {
            MessageBox.Show("O Nexus já está aberto neste computador. Use a janela que já existe.", "Nexus",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (!PrepararBanco()) return; // falhou e o usuário escolheu sair: nada de abrir a janela

        // Cor de destaque da biblioteca WPF-UI (foco dos campos, seleção, abas...) igual ao azul do Nexus.
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB), Wpf.Ui.Appearance.ApplicationTheme.Light);

        // Ao entrar em qualquer campo de texto pelo teclado (Tab), seleciona tudo:
        // quem digita por cima não precisa apagar o "0" que já estava lá.
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TextBox),
            UIElement.GotKeyboardFocusEvent,
            new System.Windows.Input.KeyboardFocusChangedEventHandler((s, _) => ((System.Windows.Controls.TextBox)s).SelectAll()));

        if (!EntrarNoSistema()) { Shutdown(); return; }

        // Depois de uma atualização, o programa antigo e o arquivo baixado ficaram ao lado: apaga os restos.
        try
        {
            if (Environment.ProcessPath is { } exe)
                NexusLS.Core.Atualizacao.AtualizadorService.LimparRestos(exe, NexusLS.Core.Atualizacao.AtualizadorService.PastaPadrao());
        }
        catch (Exception) { /* limpeza é só higiene: nunca impede de abrir */ }

        base.OnStartup(e);
        IniciarBackupAutomatico();
    }

    /// <summary>
    /// Se há funcionários cadastrados, pede o PIN antes de abrir a janela principal. Sem funcionários, abre direto.
    /// Devolve false se a pessoa desistiu (o programa fecha).
    /// </summary>
    private bool EntrarNoSistema()
    {
        try
        {
            var exige = Task.Run(() => Servicos.Funcionarios.ExigeLoginAsync()).GetAwaiter().GetResult();
            Seguranca.Sessao.DefinirLoginAtivo(exige);
            if (!exige) return true;

            // Enquanto só a janela de entrada existe, fechá-la não pode encerrar o programa por conta própria.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var funcionario = Views.LoginWindow.Pedir(Servicos.Funcionarios, null);
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            if (funcionario is null) return false;
            Seguranca.Sessao.Entrar(funcionario);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível abrir a tela de entrada: " + ex.Message, "Nexus", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Antes de mudar a estrutura do banco (versão nova do programa), guarda um backup. Se a mudança falhar,
    /// volta o banco para esse backup e avisa. Devolve false se o programa não deve abrir.
    /// </summary>
    private static bool PrepararBanco()
    {
        InfoBackup? seguranca = null;
        try
        {
            using var db = new PdvDbContext(OpcoesBanco);
            var temDados = db.Database.GetAppliedMigrations().Any();
            if (temDados && db.Database.GetPendingMigrations().Any())
            {
                try
                {
                    // Versão síncrona de propósito: esperar uma tarefa async aqui, na thread da tela, travava o programa
                    // (a continuação da tarefa precisava da própria thread que estava esperando).
                    seguranca = Servicos.Backup.Servico.Fazer(Servicos.Backup.Configuracao.Pasta, MotivoBackup.PreAtualizacao);
                }
                catch (Exception ex)
                {
                    var seguir = MessageBox.Show(
                        "Não foi possível fazer o backup antes de atualizar o banco:" + Environment.NewLine + ex.Message +
                        Environment.NewLine + Environment.NewLine + "Continuar mesmo assim? (Não é recomendado.)", "Nexus",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (seguir != MessageBoxResult.Yes)
                    {
                        Current.Shutdown();
                        return false;
                    }
                }
            }
            db.Database.Migrate();
            return true;
        }
        catch (Exception ex)
        {
            var texto = "Não foi possível atualizar o banco de dados:" + Environment.NewLine + ex.Message;
            if (seguranca is not null)
            {
                try
                {
                    Servicos.Backup.Servico.Restaurar(seguranca.Caminho, Servicos.Backup.Configuracao.Pasta);
                    texto += Environment.NewLine + Environment.NewLine + "Seus dados foram devolvidos ao estado de antes da atualização.";
                }
                catch (Exception ex2)
                {
                    texto += Environment.NewLine + Environment.NewLine + "Não foi possível devolver o backup sozinho: " + ex2.Message +
                             Environment.NewLine + "O backup está em: " + seguranca.Caminho;
                }
            }
            MessageBox.Show(texto, "Nexus", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return false;
        }
    }

    private static bool _avisouFalhaBackup;

    /// <summary>Faz o backup automático ao abrir (se já passou o intervalo) e confere de novo a cada 15 minutos.</summary>
    private void IniciarBackupAutomatico()
    {
        _ = VerificarBackupAutomaticoAsync();
        var relogio = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        relogio.Tick += async (_, _) => await VerificarBackupAutomaticoAsync();
        relogio.Start();
    }

    private static async Task VerificarBackupAutomaticoAsync()
    {
        try
        {
            await Servicos.Backup.FazerSeEstiverNaHoraAsync();
        }
        catch (Exception ex)
        {
            // Erro de backup nunca fica em silêncio; avisa uma vez por sessão para não atrapalhar a venda.
            if (_avisouFalhaBackup) return;
            _avisouFalhaBackup = true;
            MessageBox.Show("O backup automático falhou:" + Environment.NewLine + ex.Message +
                            Environment.NewLine + Environment.NewLine + "Verifique a pasta de backup em Configurações.", "Nexus",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
