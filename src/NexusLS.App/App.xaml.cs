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
            .UseSqlite($"Data Source={CaminhoBanco.Padrao()}")
            .Options;

    /// <summary>
    /// OnStartup roda uma vez quando o programa abre. Migrate() cria o arquivo do banco
    /// se não existir e aplica as migrations pendentes (as "versões" da estrutura das tabelas).
    /// Precisa vir ANTES do base.OnStartup, que é quem abre a janela principal.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!PrepararBanco()) return; // falhou e o usuário escolheu sair: nada de abrir a janela

        // Cor de destaque da biblioteca WPF-UI (foco dos campos, seleção, abas...) igual ao azul do Nexus LS.
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB), Wpf.Ui.Appearance.ApplicationTheme.Light);

        // Ao entrar em qualquer campo de texto pelo teclado (Tab), seleciona tudo:
        // quem digita por cima não precisa apagar o "0" que já estava lá.
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TextBox),
            UIElement.GotKeyboardFocusEvent,
            new System.Windows.Input.KeyboardFocusChangedEventHandler((s, _) => ((System.Windows.Controls.TextBox)s).SelectAll()));

        base.OnStartup(e);
        IniciarBackupAutomatico();
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
                        Environment.NewLine + Environment.NewLine + "Continuar mesmo assim? (Não é recomendado.)", "Nexus LS",
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
            MessageBox.Show(texto, "Nexus LS", MessageBoxButton.OK, MessageBoxImage.Error);
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
                            Environment.NewLine + Environment.NewLine + "Verifique a pasta de backup em Configurações.", "Nexus LS",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
