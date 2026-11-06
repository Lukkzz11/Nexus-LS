using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NexusLS.Core.Atualizacao;
using NexusLS.Core.Licenciamento;
using NexusLS.Core.Services;

namespace NexusLS.Atualizador;

public partial class MainWindow : Window
{
    private InfoVersao? _versaoDisponivel;
    private AtualizadorService? _atualizador;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CarregarCodigoMaquina();
        CarregarStatusLicenca();
        CarregarStatusVersao();

        // Processar argumentos de linha de comando se houver
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--atalho"))
        {
            CriarAtalhoDesktop(silencioso: true);
        }
        if (args.Contains("--portal"))
        {
            AbrirUrl(TxtUrlPortal.Text);
        }
    }

    private void CarregarCodigoMaquina()
    {
        try
        {
            TxtCodigoMaquina.Text = LicencaService.CodigoDaMaquina();
        }
        catch (Exception ex)
        {
            TxtCodigoMaquina.Text = "Erro: " + ex.Message;
        }
    }

    private readonly ServicoLicencaNuvem _licenca = new(new FonteLicencaFirestore(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }), ServicoLicencaNuvem.CaminhoPadrao());

    private async void CarregarStatusLicenca()
    {
        MostrarLicenca(_licenca.AvaliarLocal());
        try { MostrarLicenca(await _licenca.VerificarAsync()); }
        catch (Exception ex) { TxtLicencaDetalhe.Text = ex.Message; }
    }

    private void MostrarLicenca(ResultadoLicencaNuvem r)
    {
        TxtLicencaStatus.Text = r.Estado switch
        {
            EstadoLicencaNuvem.Valida => "Válida",
            EstadoLicencaNuvem.Vencendo => "Vencendo em breve",
            EstadoLicencaNuvem.Vencida => "Vencida",
            EstadoLicencaNuvem.Bloqueada => "Bloqueada",
            EstadoLicencaNuvem.OutraMaquina => "Outro computador",
            EstadoLicencaNuvem.SemConexao => "Sem conexão",
            EstadoLicencaNuvem.NaoEncontrada => "Sem licença",
            _ => "Inválida"
        };
        TxtLicencaStatus.Foreground = r.Estado switch
        {
            EstadoLicencaNuvem.Valida => (SolidColorBrush)FindResource("CorPositivo"),
            EstadoLicencaNuvem.Vencendo or EstadoLicencaNuvem.SemConexao => (SolidColorBrush)FindResource("CorAviso"),
            EstadoLicencaNuvem.NaoEncontrada => (SolidColorBrush)FindResource("CorTextoSuave"),
            _ => (SolidColorBrush)FindResource("CorPerigo")
        };
        TxtLicencaDetalhe.Text = r.Mensagem;
        TxtLicencaCliente.Text = string.IsNullOrEmpty(r.Cliente) ? "—" : r.Cliente;
        TxtLicencaValidade.Text = r.Validade is { } v ? $"{v:dd/MM/yyyy}" + (r.DiasRestantes is { } d ? $"  ({d} dia(s))" : "") : "—";
    }


    private void CarregarStatusVersao()
    {
        var exe = TxtCaminhoExe.Text.Trim();
        if (!File.Exists(exe))
        {
            // Tenta achar na mesma pasta do atualizador
            var local = Path.Combine(AppContext.BaseDirectory, "NexusLS.exe");
            if (File.Exists(local))
            {
                exe = local;
                TxtCaminhoExe.Text = local;
            }
        }

        if (File.Exists(exe))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                var fi = new FileInfo(exe);
                TxtVersaoStatus.Text = $"v{vi.ProductVersion ?? vi.FileVersion ?? "1.0.0"} ({fi.Length / (1024 * 1024)} MB)";
                TxtVersaoArquivo.Text = $"Modificado em: {fi.LastWriteTime:dd/MM/yyyy HH:mm}";
                TxtStatusGeral.Text = $"Executável detectado em: {exe}";
            }
            catch (Exception ex)
            {
                TxtVersaoStatus.Text = "Arquivo presente (ilegível)";
                TxtVersaoArquivo.Text = ex.Message;
            }
        }
        else
        {
            TxtVersaoStatus.Text = "Não encontrado";
            TxtVersaoArquivo.Text = "Informe o caminho correto do NexusLS.exe";
        }
    }

    // --- ABA LICENÇA ---

    // --- ABA LICENÇA (Firebase: projeto nexus-ls, coleção "licencas") ---

    private async void BtnAtivarChave_Click(object sender, RoutedEventArgs e)
    {
        BtnAtivarChave.IsEnabled = false;
        TxtStatusGeral.Text = "Consultando a licença no Firebase...";
        try
        {
            var r = await _licenca.AtivarAsync(TxtChaveLicenca.Text);
            MostrarLicenca(r);
            TxtResultadoLicenca.Text = r.Mensagem;
            TxtResultadoLicenca.Foreground = r.Ativa ? (SolidColorBrush)FindResource("CorPositivo") : (SolidColorBrush)FindResource("CorPerigo");
            TxtStatusGeral.Text = r.Ativa ? "Licença ativada." : "Licença não ativada.";
        }
        finally
        {
            BtnAtivarChave.IsEnabled = true;
        }
    }

    private void BtnRemoverLicenca_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Remover a licença deste computador? Você poderá ativá-la de novo com a chave.",
            "Confirmar remoção", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _licenca.Remover();
        MostrarLicenca(_licenca.AvaliarLocal());
        TxtResultadoLicenca.Text = "Licença removida deste computador.";
    }


    // --- ABA ATUALIZAÇÃO ---

    private void BtnSelecionarExe_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Nexus Executável (NexusLS.exe)|NexusLS.exe|Executáveis (*.exe)|*.exe",
            Title = "Selecionar Executável do Nexus"
        };
        if (ofd.ShowDialog(this) == true)
        {
            TxtCaminhoExe.Text = ofd.FileName;
            CarregarStatusVersao();
        }
    }

    private async void BtnVerificarAtualizacao_Click(object sender, RoutedEventArgs e)
    {
        BtnVerificarAtualizacao.IsEnabled = false;
        TxtStatusGeral.Text = "Consultando atualizações online no repositório de lançamentos...";
        try
        {
            var repo = ConfiguracaoAtualizacao.Repositorio();
            var http = new HttpClient();
            var fonte = new FonteGitHub(http, repo);
            var vi = File.Exists(TxtCaminhoExe.Text) ? FileVersionInfo.GetVersionInfo(TxtCaminhoExe.Text) : null;
            var versaoStr = vi?.ProductVersion?.Split('+')[0] ?? "1.0.0";
            if (!VersaoPrograma.TryParse(versaoStr, out var versaoAtual))
            {
                versaoAtual = new VersaoPrograma(1, 0, 0);
            }

            _atualizador = new AtualizadorService(fonte, AtualizadorService.PastaPadrao(), versaoAtual);
            var res = await _atualizador.VerificarAsync();

            if (res.Estado == EstadoVerificacao.Disponivel && res.Nova != null)
            {
                _versaoDisponivel = res.Nova;
                BrdNovidade.Visibility = Visibility.Visible;
                TxtNovidadeTitulo.Text = $"🎉 Nova Versão {_versaoDisponivel.Versao} Disponível!";
                TxtNovidadeNotas.Text = string.IsNullOrWhiteSpace(_versaoDisponivel.Notas)
                    ? "Sem notas de versão fornecidas."
                    : _versaoDisponivel.Notas;
                TxtStatusGeral.Text = $"Nova versão {_versaoDisponivel.Versao} encontrada!";
            }
            else if (res.Estado == EstadoVerificacao.SemNovidade)
            {
                BrdNovidade.Visibility = Visibility.Collapsed;
                TxtStatusGeral.Text = "Você já está na versão mais recente disponível no repositório.";
                MessageBox.Show(this, "O Nexus já está na versão mais atual.", "Tudo Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                BrdNovidade.Visibility = Visibility.Collapsed;
                TxtStatusGeral.Text = res.Mensagem;
                MessageBox.Show(this, res.Mensagem, "Verificação Online", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Erro ao verificar: " + ex.Message;
            MessageBox.Show(this, "Falha ao verificar atualizações:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnVerificarAtualizacao.IsEnabled = true;
        }
    }

    private async void BtnBaixarEInstalar_Click(object sender, RoutedEventArgs e)
    {
        if (_atualizador is null || _versaoDisponivel is null) return;

        BtnBaixarEInstalar.IsEnabled = false;
        PrgAtualizacao.Visibility = Visibility.Visible;
        PrgAtualizacao.IsIndeterminate = true;
        TxtStatusGeral.Text = $"Baixando versão {_versaoDisponivel.Versao}...";

        try
        {
            var progresso = new Progress<double>(p =>
            {
                PrgAtualizacao.IsIndeterminate = false;
                PrgAtualizacao.Value = p * 100;
                TxtStatusGeral.Text = $"Baixando... {p:P0}";
            });

            var arquivoBaixado = await _atualizador.BaixarAsync(_versaoDisponivel, progresso);
            TxtStatusGeral.Text = "Download concluído e conferido com SHA-256.";

            // Fechar Nexus se aberto
            FecharProcessoNexusLS();

            var exeAlvo = TxtCaminhoExe.Text.Trim();
            if (File.Exists(exeAlvo))
            {
                var backup = Path.Combine(Path.GetDirectoryName(exeAlvo)!, "NexusLS.anterior.exe");
                File.Copy(exeAlvo, backup, overwrite: true);
            }

            AtualizadorService.TrocarExecutavel(exeAlvo, arquivoBaixado);
            CarregarStatusVersao();
            BrdNovidade.Visibility = Visibility.Collapsed;
            TxtStatusGeral.Text = "Nexus atualizado com sucesso para a versão " + _versaoDisponivel.Versao;
            MessageBox.Show(this, "Nexus atualizado com sucesso!", "Atualização Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Erro na atualização: " + ex.Message;
            MessageBox.Show(this, "Falha na atualização:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PrgAtualizacao.Visibility = Visibility.Collapsed;
            BtnBaixarEInstalar.IsEnabled = true;
        }
    }

    private void BtnRestaurarBackup_Click(object sender, RoutedEventArgs e)
    {
        var exeAlvo = TxtCaminhoExe.Text.Trim();
        var pasta = Path.GetDirectoryName(exeAlvo) ?? @"C:\NexusLS\publicar";
        var backup = Path.Combine(pasta, "NexusLS.anterior.exe");

        if (!File.Exists(backup))
        {
            MessageBox.Show(this, "Nenhum arquivo de backup anterior encontrado em: " + backup, "Backup Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, $"Deseja restaurar a versão de backup ({backup}) sobre o executável atual?",
            "Restaurar Versão", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                FecharProcessoNexusLS();
                File.Copy(backup, exeAlvo, overwrite: true);
                CarregarStatusVersao();
                TxtStatusGeral.Text = "Versão anterior restaurada com sucesso.";
                MessageBox.Show(this, "Versão restaurada com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao restaurar backup: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnCopiarCompilado_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Executável Nexus (*.exe)|NexusLS.exe;*.exe",
            Title = "Selecionar Novo Executável para Atualizar"
        };

        if (ofd.ShowDialog(this) == true)
        {
            try
            {
                FecharProcessoNexusLS();
                var exeAlvo = TxtCaminhoExe.Text.Trim();
                if (File.Exists(exeAlvo))
                {
                    var backup = Path.Combine(Path.GetDirectoryName(exeAlvo)!, "NexusLS.anterior.exe");
                    File.Copy(exeAlvo, backup, overwrite: true);
                }
                File.Copy(ofd.FileName, exeAlvo, overwrite: true);
                CarregarStatusVersao();
                TxtStatusGeral.Text = "Executável atualizado com sucesso a partir de: " + ofd.FileName;
                MessageBox.Show(this, "Executável atualizado com sucesso!", "Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao copiar arquivo: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FecharProcessoNexusLS()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("NexusLS"))
            {
                p.Kill();
                p.WaitForExit(3000);
            }
        }
        catch { }
    }

    private void BtnIniciarNexus_Click(object sender, RoutedEventArgs e)
    {
        var exe = TxtCaminhoExe.Text.Trim();
        if (!File.Exists(exe))
        {
            MessageBox.Show(this, "Executável do Nexus não encontrado em: " + exe, "Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });
            TxtStatusGeral.Text = "Nexus iniciado.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Erro ao iniciar Nexus: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- ABA PORTAL WEB ---

    private void BtnAbrirPortalWeb_Click(object sender, RoutedEventArgs e)
    {
        AbrirUrl(TxtUrlPortal.Text.Trim());
    }

    private void BtnAbrirReleases_Click(object sender, RoutedEventArgs e)
    {
        AbrirUrl("https://github.com/Lukkzz11/Nexus-LS---Update/releases");
    }

    private void AbrirUrl(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            TxtStatusGeral.Text = "Link aberto no navegador: " + url;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir o link:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- ATALHO NA ÁREA DE TRABALHO ---

    private void BtnCriarAtalho_Click(object sender, RoutedEventArgs e)
    {
        CriarAtalhoDesktop(silencioso: false);
    }

    public static void CriarAtalhoDesktop(bool silencioso)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var caminhoAtalho = Path.Combine(desktop, "Atualizador e Ativador Nexus.lnk");
            var alvoExe = @"C:\NexusLS\publicar\atualizador.exe";

            if (!File.Exists(alvoExe))
            {
                var local = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(local) && File.Exists(local))
                {
                    alvoExe = local;
                }
            }

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type != null)
            {
                dynamic? shell = Activator.CreateInstance(type);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(caminhoAtalho);
                    shortcut.TargetPath = alvoExe;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(alvoExe);
                    shortcut.Description = "Atualizador e Ativador de Licença Nexus";
                    shortcut.Save();

                    if (!silencioso)
                    {
                        MessageBox.Show($"Atalho criado com sucesso na Área de Trabalho:\n{caminhoAtalho}",
                            "Atalho Criado", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!silencioso)
            {
                MessageBox.Show("Erro ao criar atalho: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
