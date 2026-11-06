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
using NexusLS.Core.Services;

namespace NexusLS.Atualizador;

public partial class MainWindow : Window
{
    private string? _chavePrivada;
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
        CarregarChavePrivada();
        CarregarStatusLicenca();
        CarregarStatusVersao();
        TxtValidade.Text = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd");

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

    private void CarregarChavePrivada()
    {
        var caminhoPadrao = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "NexusLS_chaves", "licenca-privada.txt");

        if (File.Exists(caminhoPadrao))
        {
            try
            {
                _chavePrivada = File.ReadAllText(caminhoPadrao).Trim();
                TxtChaveStatus.Text = $"Chave mestra carregada com sucesso ({caminhoPadrao})";
                TxtChaveStatus.Foreground = (SolidColorBrush)FindResource("CorSucesso");
                BrdChaveStatus.BorderBrush = (SolidColorBrush)FindResource("CorSucesso");
                BrdChaveStatus.Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129));
                return;
            }
            catch (Exception ex)
            {
                TxtChaveStatus.Text = $"Erro ao ler chave em {caminhoPadrao}: {ex.Message}";
            }
        }
        else
        {
            TxtChaveStatus.Text = $"Chave mestra não encontrada em {caminhoPadrao}. Você ainda pode instalar licenças geradas.";
        }

        TxtChaveStatus.Foreground = (SolidColorBrush)FindResource("CorAviso");
        BrdChaveStatus.BorderBrush = (SolidColorBrush)FindResource("CorAviso");
        BrdChaveStatus.Background = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
    }

    private void CarregarStatusLicenca()
    {
        try
        {
            var licencaService = new LicencaService(LicencaService.CaminhoPadrao());
            var res = licencaService.Avaliar();

            TxtLicencaStatus.Text = res.Estado switch
            {
                EstadoLicenca.Valida => "Válida",
                EstadoLicenca.Vencendo => "Vencendo em breve",
                EstadoLicenca.Vencida => "Vencida",
                EstadoLicenca.SemLicenca => "Sem licença (avaliação)",
                EstadoLicenca.OutraMaquina => "Outro computador",
                _ => "Inválida"
            };

            TxtLicencaStatus.Foreground = res.Estado switch
            {
                EstadoLicenca.Valida => (SolidColorBrush)FindResource("CorSucesso"),
                EstadoLicenca.Vencendo => (SolidColorBrush)FindResource("CorAviso"),
                EstadoLicenca.SemLicenca => (SolidColorBrush)FindResource("CorTextoSecundario"),
                _ => (SolidColorBrush)FindResource("CorErro")
            };

            TxtLicencaDetalhe.Text = res.Mensagem;
        }
        catch (Exception ex)
        {
            TxtLicencaStatus.Text = "Erro ao avaliar";
            TxtLicencaDetalhe.Text = ex.Message;
        }
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

    private void BtnAdicionarDias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out var dias))
        {
            TxtValidade.Text = DateTime.Today.AddDays(dias).ToString("yyyy-MM-dd");
        }
    }

    private void BtnPreencherMaquina_Click(object sender, RoutedEventArgs e)
    {
        TxtMaquina.Text = LicencaService.CodigoDaMaquina();
    }

    private void BtnGerarEAtivar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = GerarLicencaJson();
            if (json is null) return;

            var caminhoDestino = LicencaService.CaminhoPadrao();
            Directory.CreateDirectory(Path.GetDirectoryName(caminhoDestino)!);
            File.WriteAllText(caminhoDestino, json);

            CarregarStatusLicenca();
            TxtStatusGeral.Text = "Licença gerada e instalada com sucesso em: " + caminhoDestino;
            MessageBox.Show(this, "Licença ativada com sucesso!\n\n" + TxtLicencaDetalhe.Text,
                "Licença Ativada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Erro ao ativar licença: " + ex.Message;
            MessageBox.Show(this, "Erro ao gerar/ativar licença:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSalvarArquivo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = GerarLicencaJson();
            if (json is null) return;

            var clienteFormatado = new string(TxtCliente.Text.Where(char.IsLetterOrDigit).ToArray());
            var sfd = new SaveFileDialog
            {
                Filter = "Arquivo de Licença (*.json)|*.json",
                FileName = $"licenca-{clienteFormatado}-{TxtValidade.Text.Trim()}.json",
                Title = "Salvar Arquivo de Licença"
            };

            if (sfd.ShowDialog(this) == true)
            {
                File.WriteAllText(sfd.FileName, json);
                TxtStatusGeral.Text = "Licença salva em: " + sfd.FileName;
                MessageBox.Show(this, $"Licença salva com sucesso em:\n{sfd.FileName}", "Licença Salva",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Erro ao salvar arquivo de licença: " + ex.Message;
            MessageBox.Show(this, "Erro:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? GerarLicencaJson()
    {
        var chave = _chavePrivada;
        if (string.IsNullOrWhiteSpace(chave))
        {
            MessageBox.Show(this,
                "A chave privada mestra não foi encontrada em %USERPROFILE%\\NexusLS_chaves\\licenca-privada.txt.\n" +
                "Somente o fornecedor pode gerar novas licenças.",
                "Chave Mestra Não Encontrada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var cliente = TxtCliente.Text.Trim();
        if (string.IsNullOrWhiteSpace(cliente))
        {
            MessageBox.Show(this, "Informe o nome do cliente.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        if (!DateTime.TryParseExact(TxtValidade.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validade))
        {
            MessageBox.Show(this, "Informe uma data de validade válida no formato AAAA-MM-DD.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var maquina = string.IsNullOrWhiteSpace(TxtMaquina.Text) ? "*" : TxtMaquina.Text.Trim();
        return GeradorLicenca.Gerar(chave, cliente, validade, maquina);
    }

    private void BtnInstalarArquivo_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Arquivo de Licença (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            Title = "Selecionar Arquivo de Licença do Nexus LS"
        };

        if (ofd.ShowDialog(this) == true)
        {
            try
            {
                var s = new LicencaService(LicencaService.CaminhoPadrao());
                var res = s.Instalar(ofd.FileName);
                CarregarStatusLicenca();
                TxtStatusGeral.Text = "Licença instalada: " + res.Mensagem;
                MessageBox.Show(this, "Licença instalada com sucesso!\n\n" + res.Mensagem,
                    "Licença Instalada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtStatusGeral.Text = "Erro ao instalar licença: " + ex.Message;
                MessageBox.Show(this, "Não foi possível instalar esta licença:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnRemoverLicenca_Click(object sender, RoutedEventArgs e)
    {
        var caminho = LicencaService.CaminhoPadrao();
        if (!File.Exists(caminho))
        {
            MessageBox.Show(this, "Nenhuma licença instalada.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, "Deseja remover a licença instalada deste computador? O programa voltará ao modo de avaliação.",
            "Confirmar Remoção", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                File.Delete(caminho);
                CarregarStatusLicenca();
                TxtStatusGeral.Text = "Licença removida com sucesso.";
                MessageBox.Show(this, "Licença removida.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao remover licença: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // --- ABA ATUALIZAÇÃO ---

    private void BtnSelecionarExe_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Nexus LS Executável (NexusLS.exe)|NexusLS.exe|Executáveis (*.exe)|*.exe",
            Title = "Selecionar Executável do Nexus LS"
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
                MessageBox.Show(this, "O Nexus LS já está na versão mais atual.", "Tudo Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
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

            // Fechar Nexus LS se aberto
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
            TxtStatusGeral.Text = "Nexus LS atualizado com sucesso para a versão " + _versaoDisponivel.Versao;
            MessageBox.Show(this, "Nexus LS atualizado com sucesso!", "Atualização Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
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
            Filter = "Executável Nexus LS (*.exe)|NexusLS.exe;*.exe",
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
            MessageBox.Show(this, "Executável do Nexus LS não encontrado em: " + exe, "Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });
            TxtStatusGeral.Text = "Nexus LS iniciado.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Erro ao iniciar Nexus LS: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void BtnAbrirFirebaseApp_Click(object sender, RoutedEventArgs e)
    {
        AbrirUrl("https://nexus-ls.firebaseapp.com");
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
            var caminhoAtalho = Path.Combine(desktop, "Atualizador e Ativador Nexus LS.lnk");
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
                    shortcut.Description = "Atualizador e Ativador de Licença Nexus LS";
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
