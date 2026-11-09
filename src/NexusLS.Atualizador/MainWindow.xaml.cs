using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NexusLS.Core.Atualizacao;
using NexusLS.Core.Licenciamento;
using NexusLS.Core.Services;

namespace NexusLS.Atualizador;

public partial class MainWindow : Window
{
    private static readonly HttpClient HttpCompartilhado = CriarHttpClient();
    private InfoVersao? _versaoDisponivel;
    private AtualizadorService? _atualizador;
    private InfoVersao? _versaoDisponivelBarTender;
    private AtualizadorService? _atualizadorBarTender;
    private ResultadoLicencaNuvem? _ultimoResultadoLicenca;

    private readonly ServicoLicencaNuvem _licenca = new(
        new FonteLicencaComReserva(
            new FonteLicencaCloudFunction(HttpCompartilhado),
            new FonteLicencaFirestore(HttpCompartilhado)),
        ServicoLicencaNuvem.CaminhoPadrao());

    private static HttpClient CriarHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NexusLS-Atualizador/1.0");
        return client;
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TxtCaminhoExe.Text = LocalizarExecutavelPadrao();
        TxtCaminhoBarTenderExe.Text = LocalizarBarTenderPadrao();
        CarregarCodigoMaquinaEDiagnostico();
        CarregarStatusLicenca();
        CarregarStatusVersao();
        CarregarStatusVersaoBarTender();

        // Processar argumentos de linha de comando se houver
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--atalho"))
        {
            CriarAtalhoDesktop(silencioso: true);
        }

        if (args.Any(a => a.Equals("--bartender", StringComparison.OrdinalIgnoreCase) || a.Equals("-b", StringComparison.OrdinalIgnoreCase)))
        {
            TabControle.SelectedIndex = 3; // Aba Atualizações BarTender
        }
        else if (args.Any(a => a.Equals("--licenca", StringComparison.OrdinalIgnoreCase) || a.Equals("-l", StringComparison.OrdinalIgnoreCase)))
        {
            TabControle.SelectedIndex = 1; // Aba Licença Comercial
        }
        else if (args.Any(a => a.Equals("--sistema", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase)))
        {
            TabControle.SelectedIndex = 2; // Aba Atualizações do Sistema
        }
        else
        {
            TabControle.SelectedIndex = 0; // Aba 0: Central do Cliente (Início)
        }
    }

    private static string LocalizarBarTenderPadrao()
    {
        var candidatos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "NexusBarTender.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "NexusBarTender.exe"),
            @"C:\Nexus\NexusBarTender.exe",
            @"C:\NexusLS\NexusBarTender.exe",
            @"C:\NexusLS\publicar\NexusBarTender.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NexusLS", "NexusBarTender.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "NexusBarTender.exe")
        };

        return candidatos.FirstOrDefault(File.Exists) ?? candidatos[0];
    }

    private void CarregarStatusVersaoBarTender()
    {
        try
        {
            var exe = TxtCaminhoBarTenderExe.Text.Trim();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                var detectado = LocalizarBarTenderPadrao();
                if (File.Exists(detectado))
                {
                    exe = detectado;
                    TxtCaminhoBarTenderExe.Text = detectado;
                }
            }

            if (File.Exists(exe))
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                var versao = (vi.ProductVersion ?? vi.FileVersion ?? "1.0.0").Split('+')[0];
                var fi = new FileInfo(exe);
                var mb = fi.Length / (1024.0 * 1024.0);
                TxtStatusModuloBarTender.Text = "Módulo instalado e pronto para uso.";
                TxtVersaoBarTenderInstalada.Text = $"• Versão: v{versao} ({mb:F0} MB) — Modificado em: {fi.LastWriteTime:dd/MM/yyyy HH:mm}";
                TxtHomeBarTenderStatus.Text = $"● Instalado: v{versao} ({mb:F0} MB)";
                TxtHomeBarTenderStatus.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            }
            else
            {
                TxtStatusModuloBarTender.Text = "Executável do BarTender não encontrado no caminho informado.";
                TxtVersaoBarTenderInstalada.Text = "• O cliente não possui o módulo BarTender instalado ou está em outra pasta.";
                TxtHomeBarTenderStatus.Text = "○ Não instalado neste computador";
                TxtHomeBarTenderStatus.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
            }
        }
        catch
        {
            TxtStatusModuloBarTender.Text = "Módulo BarTender opcional.";
            TxtHomeBarTenderStatus.Text = "○ Módulo opcional";
            TxtHomeBarTenderStatus.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
        }
    }

    private static string LocalizarExecutavelPadrao()
    {
        var candidatos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "NexusLS.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "NexusLS.exe"),
            @"C:\Nexus\NexusLS.exe",
            @"C:\NexusLS\NexusLS.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NexusLS", "NexusLS.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "NexusLS.exe"),
            @"C:\NexusLS\publicar\NexusLS.exe"
        };

        return candidatos.FirstOrDefault(File.Exists) ?? candidatos[0];
    }

    private void CarregarCodigoMaquinaEDiagnostico()
    {
        try
        {
            var hwid = LicencaService.CodigoDaMaquina();
            TxtCodigoMaquina.Text = hwid;
            TxtNomeComputador.Text = $"Terminal: {Environment.MachineName}";

            var pastaDados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS");
            TxtDiagMaquina.Text = $"• Computador: {Environment.MachineName}  |  HWID: {hwid}";
            TxtDiagSistema.Text = $"• Sistema Operacional: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
            TxtDiagPastaDados.Text = $"• Diretório de Dados Locais: {pastaDados}";
        }
        catch (Exception ex)
        {
            TxtCodigoMaquina.Text = "Erro: " + ex.Message;
        }
    }

    private async void CarregarStatusLicenca()
    {
        var chaveSalva = _licenca.ChaveSalva;
        if (!string.IsNullOrWhiteSpace(chaveSalva) && string.IsNullOrWhiteSpace(TxtChaveLicenca.Text))
        {
            TxtChaveLicenca.Text = chaveSalva;
        }

        MostrarLicenca(_licenca.AvaliarLocal());
        try
        {
            var online = await _licenca.VerificarAsync();
            MostrarLicenca(online);
        }
        catch (Exception ex)
        {
            TxtLicencaDetalhe.Text = ex.Message;
        }
    }

    private void MostrarLicenca(ResultadoLicencaNuvem r)
    {
        _ultimoResultadoLicenca = r;

        if (!string.IsNullOrWhiteSpace(r.Chave) && string.IsNullOrWhiteSpace(TxtChaveLicenca.Text))
        {
            TxtChaveLicenca.Text = r.Chave;
        }

        TxtLicencaStatus.Text = r.Estado switch
        {
            EstadoLicencaNuvem.Valida => r.Offline ? "Válida (Modo Offline)" : "Ativa e Autorizada",
            EstadoLicencaNuvem.Vencendo => "Vencendo em breve",
            EstadoLicencaNuvem.Vencida => "Licença Vencida",
            EstadoLicencaNuvem.Bloqueada => "Suspensa / Bloqueada",
            EstadoLicencaNuvem.OutraMaquina => "Limite de Computadores",
            EstadoLicencaNuvem.SemConexao => "Sem Conexão",
            EstadoLicencaNuvem.NaoEncontrada => "Sem Licença Ativa",
            _ => "Chave Inválida"
        };

        TxtLicencaStatus.Foreground = r.Estado switch
        {
            EstadoLicencaNuvem.Valida => (SolidColorBrush)FindResource("CorPositivo"),
            EstadoLicencaNuvem.Vencendo or EstadoLicencaNuvem.SemConexao => (SolidColorBrush)FindResource("CorAviso"),
            EstadoLicencaNuvem.NaoEncontrada => (SolidColorBrush)FindResource("CorTextoSuave"),
            _ => (SolidColorBrush)FindResource("CorPerigo")
        };

        TxtLicencaDetalhe.Text = r.Mensagem;
        TxtLicencaCliente.Text = string.IsNullOrWhiteSpace(r.Cliente) ? "—" : r.Cliente;
        TxtLicencaEmpresa.Text = string.IsNullOrWhiteSpace(r.Empresa) ? "—" : r.Empresa;
        TxtLicencaPlano.Text = string.IsNullOrWhiteSpace(r.Plano) ? (r.Ativa ? "COMERCIAL" : "—") : r.Plano.ToUpperInvariant();

        TxtLicencaValidade.Text = r.Validade is { } v
            ? $"{v:dd/MM/yyyy}" + (r.DiasRestantes is { } d ? $" ({d} dia(s) restantes)" : "")
            : "—";

        if (r.DispositivosUsados.HasValue && r.LimiteDispositivos.HasValue)
        {
            TxtLicencaDispositivos.Text = $"{r.DispositivosUsados} de {r.LimiteDispositivos} computador(es)";
        }
        else if (r.Ativa)
        {
            TxtLicencaDispositivos.Text = $"Autorizado ({LicencaService.CodigoDaMaquina()})";
        }
        else
        {
            TxtLicencaDispositivos.Text = "—";
        }

        TxtLicencaVerificacao.Text = r.VerificadaEm is { } ver
            ? $"{ver:dd/MM/yyyy HH:mm}" + (r.Offline ? " (Cache)" : " (Online)")
            : (r.Ativa ? $"{DateTime.Now:dd/MM/yyyy HH:mm}" : "—");

        BtnRemoverLicenca.IsEnabled = !string.IsNullOrWhiteSpace(_licenca.ChaveSalva) || r.Ativa;

        // Atualiza a Central do Cliente (Home)
        TxtHomeCliente.Text = string.IsNullOrWhiteSpace(r.Cliente) ? "Cliente Nexus" : r.Cliente;
        TxtHomeEmpresa.Text = string.IsNullOrWhiteSpace(r.Empresa) ? "Nexus LS" : r.Empresa;
        if (r.Ativa)
        {
            var validadeStr = r.Validade is { } val ? $"{val:dd/MM/yyyy}" : "Vitalícia";
            var planoStr = string.IsNullOrWhiteSpace(r.Plano) ? "COMERCIAL" : r.Plano.ToUpperInvariant();
            var diasStr = r.DiasRestantes is { } dias ? $" • {dias} dia(s) restantes" : "";
            TxtHomeDetalhesLicenca.Text = $"Plano {planoStr} • Validade: {validadeStr}{diasStr} • {r.Mensagem}";
            TxtHomeStatusBadge.Text = "● Licença Ativa";
            TxtHomeStatusBadge.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            BrdHomeStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x16, 0xA3, 0x4A));
            BrdHomeStatusBadge.BorderBrush = (SolidColorBrush)FindResource("CorPositivo");
        }
        else
        {
            TxtHomeDetalhesLicenca.Text = r.Mensagem;
            TxtHomeStatusBadge.Text = "● Não Ativada";
            TxtHomeStatusBadge.Foreground = (SolidColorBrush)FindResource("CorPerigo");
            BrdHomeStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xDC, 0x26, 0x26));
            BrdHomeStatusBadge.BorderBrush = (SolidColorBrush)FindResource("CorPerigo");
        }
    }

    private void CarregarStatusVersao()
    {
        var exe = TxtCaminhoExe.Text.Trim();
        if (!File.Exists(exe))
        {
            var detectado = LocalizarExecutavelPadrao();
            if (File.Exists(detectado))
            {
                exe = detectado;
                TxtCaminhoExe.Text = detectado;
            }
        }

        if (File.Exists(exe))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                var fi = new FileInfo(exe);
                var verLimpa = (vi.ProductVersion ?? vi.FileVersion ?? "1.0.0").Split('+')[0];
                TxtVersaoStatus.Text = $"v{verLimpa} ({Math.Max(1, fi.Length / (1024 * 1024))} MB)";
                TxtVersaoArquivo.Text = $"Atualizado em: {fi.LastWriteTime:dd/MM/yyyy HH:mm}";
                TxtStatusGeral.Text = $"Executável pronto em: {exe}";
                TxtHomeNexusStatus.Text = $"● Instalado: v{verLimpa} ({Math.Max(1, fi.Length / (1024 * 1024))} MB)";
                TxtHomeNexusStatus.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            }
            catch (Exception ex)
            {
                TxtVersaoStatus.Text = "Arquivo presente (ilegível)";
                TxtVersaoArquivo.Text = ex.Message;
                TxtHomeNexusStatus.Text = "○ Arquivo presente";
                TxtHomeNexusStatus.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
            }
        }
        else
        {
            TxtVersaoStatus.Text = "Não localizado";
            TxtVersaoArquivo.Text = "Selecione o arquivo NexusLS.exe na aba Atualizações";
            TxtHomeNexusStatus.Text = "○ Não instalado neste computador";
            TxtHomeNexusStatus.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
        }
    }

    // --- ABA 0: CENTRAL DO CLIENTE (HOME / LAUNCHER) ---

    private void BtnHomeIrParaUpdates_Click(object sender, RoutedEventArgs e)
    {
        TabControle.SelectedIndex = 2; // Aba Atualizações do Sistema
    }

    private void BtnHomeIrParaLicenca_Click(object sender, RoutedEventArgs e)
    {
        TabControle.SelectedIndex = 1; // Aba Licença Comercial
    }

    // --- ABA 1: LICENÇA COMERCIAL ---

    private void BtnCopiarHwid_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwid = LicencaService.CodigoDaMaquina();
            Clipboard.SetText(hwid);
            TxtStatusGeral.Text = $"Código deste computador ({hwid}) copiado para a área de transferência!";
            BtnCopiarHwid.Content = "✓ Copiado!";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível copiar o código: " + ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnColarChave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                TxtChaveLicenca.Text = Clipboard.GetText().Trim().ToUpperInvariant();
                TxtStatusGeral.Text = "Chave colada da área de transferência.";
            }
        }
        catch { }
    }

    private async void BtnAtivarChave_Click(object sender, RoutedEventArgs e)
    {
        var chave = TxtChaveLicenca.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(chave))
        {
            MessageBox.Show(this, "Digite ou cole a chave da licença antes de validar.", "Chave Obrigatória", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtChaveLicenca.Focus();
            return;
        }

        BtnAtivarChave.IsEnabled = false;
        BtnRevalidarLicenca.IsEnabled = false;
        TxtStatusGeral.Text = "Validando chave de licença no servidor central Nexus LS...";
        try
        {
            var r = await _licenca.AtivarAsync(chave);
            MostrarLicenca(r);
            TxtResultadoLicenca.Text = r.Mensagem;
            TxtResultadoLicenca.Foreground = r.Ativa ? (SolidColorBrush)FindResource("CorPositivo") : (SolidColorBrush)FindResource("CorPerigo");
            TxtStatusGeral.Text = r.Ativa ? "Licença validada e vinculada a este computador com sucesso!" : "Não foi possível ativar a licença: " + r.Mensagem;
        }
        finally
        {
            BtnAtivarChave.IsEnabled = true;
            BtnRevalidarLicenca.IsEnabled = true;
        }
    }

    private async void BtnRevalidarLicenca_Click(object sender, RoutedEventArgs e)
    {
        BtnRevalidarLicenca.IsEnabled = false;
        TxtStatusGeral.Text = "Sincronizando situação da licença com o servidor...";
        try
        {
            var r = await _licenca.VerificarAsync();
            MostrarLicenca(r);
            TxtResultadoLicenca.Text = r.Mensagem;
            TxtResultadoLicenca.Foreground = r.Ativa ? (SolidColorBrush)FindResource("CorPositivo") : (SolidColorBrush)FindResource("CorPerigo");
            TxtStatusGeral.Text = "Sincronização de licença concluída.";
        }
        finally
        {
            BtnRevalidarLicenca.IsEnabled = true;
        }
    }

    private async void BtnRemoverLicenca_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
            "Deseja desvincular e remover a licença deste computador?\n\nIsso liberará a vaga deste computador no servidor e você precisará informar a chave novamente para usar o Nexus LS aqui.",
            "Confirmar Desvinculação", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        BtnRemoverLicenca.IsEnabled = false;
        TxtStatusGeral.Text = "Desvinculando computador no servidor e removendo licença local...";
        try
        {
            await _licenca.RemoverAsync();
            TxtChaveLicenca.Text = "";
            MostrarLicenca(_licenca.AvaliarLocal());
            TxtResultadoLicenca.Text = "Licença desvinculada e removida deste computador.";
            TxtResultadoLicenca.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
            TxtStatusGeral.Text = "Computador desvinculado com sucesso.";
        }
        finally
        {
            BtnRemoverLicenca.IsEnabled = true;
        }
    }

    // --- ABA 2: ATUALIZAÇÃO DO SISTEMA ---

    private void BtnSelecionarExe_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Nexus Executável (NexusLS.exe)|NexusLS.exe|Executáveis (*.exe)|*.exe",
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
        TxtStatusGeral.Text = "Consultando atualizações online no repositório oficial...";
        try
        {
            var repo = ConfiguracaoAtualizacao.Repositorio();
            var fonte = new FonteGitHub(HttpCompartilhado, repo);
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
                    ? "Melhorias de estabilidade, segurança e desempenho."
                    : _versaoDisponivel.Notas;
                TxtStatusGeral.Text = $"Nova versão {_versaoDisponivel.Versao} encontrada!";
            }
            else if (res.Estado == EstadoVerificacao.SemNovidade)
            {
                BrdNovidade.Visibility = Visibility.Collapsed;
                TxtStatusGeral.Text = "Você já está na versão mais recente disponível.";
                MessageBox.Show(this, "O Nexus LS já está atualizado na versão mais recente.", "Sistema Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
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
                TxtStatusGeral.Text = $"Baixando atualização... {p:P0}";
            });

            var versaoInstaladaNova = _versaoDisponivel.Versao;
            var arquivoBaixado = await _atualizador.BaixarAsync(_versaoDisponivel, progresso, "NexusLS");
            TxtStatusGeral.Text = "Download concluído e conferido com assinatura SHA-256.";

            FecharProcessoNexusLSSeguro();

            var exeAlvo = TxtCaminhoExe.Text.Trim();
            if (File.Exists(exeAlvo))
            {
                var backup = Path.Combine(Path.GetDirectoryName(exeAlvo)!, "NexusLS.anterior.exe");
                File.Copy(exeAlvo, backup, overwrite: true);
            }

            AtualizadorService.TrocarExecutavel(exeAlvo, arquivoBaixado);
            CarregarStatusVersao();
            _versaoDisponivel = null;
            BrdNovidade.Visibility = Visibility.Collapsed;
            TxtStatusGeral.Text = "Nexus LS atualizado com sucesso para a versão " + versaoInstaladaNova;
            MessageBox.Show(this, "Nexus LS atualizado com sucesso para a versão " + versaoInstaladaNova + "!", "Atualização Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var pasta = Path.GetDirectoryName(exeAlvo) ?? AppContext.BaseDirectory;
        var backup = Path.Combine(pasta, "NexusLS.anterior.exe");

        if (!File.Exists(backup))
        {
            MessageBox.Show(this, "Nenhum arquivo de backup anterior encontrado em:\n" + backup, "Backup Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, $"Deseja restaurar a versão anterior ({backup}) sobre o executável atual?",
            "Restaurar Versão Anterior", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                FecharProcessoNexusLSSeguro();
                File.Copy(backup, exeAlvo, overwrite: true);
                CarregarStatusVersao();
                TxtStatusGeral.Text = "Versão anterior restaurada com sucesso.";
                MessageBox.Show(this, "Versão anterior restaurada com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
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
            Title = "Selecionar Pacote Executável para Atualizar"
        };

        if (ofd.ShowDialog(this) == true)
        {
            try
            {
                FecharProcessoNexusLSSeguro();
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

    private void BtnProcurarBarTenderExe_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Title = "Selecionar Executável do Nexus BarTender",
            Filter = "Executável Nexus BarTender|NexusBarTender.exe|Executáveis (*.exe)|*.exe|Todos os Arquivos|*.*"
        };

        if (d.ShowDialog(this) == true)
        {
            TxtCaminhoBarTenderExe.Text = d.FileName;
            CarregarStatusVersaoBarTender();
        }
    }

    private void BtnCopiarBarTenderCompilado_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Title = "Selecionar Novo Executável NexusBarTender.exe",
            Filter = "Executável NexusBarTender.exe|NexusBarTender.exe|Executáveis (*.exe)|*.exe"
        };

        if (d.ShowDialog(this) == true)
        {
            try
            {
                var destino = TxtCaminhoBarTenderExe.Text.Trim();
                if (string.IsNullOrWhiteSpace(destino)) destino = @"C:\NexusLS\publicar\NexusBarTender.exe";

                foreach (var p in Process.GetProcessesByName("NexusBarTender"))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }

                if (File.Exists(destino))
                {
                    var backup = Path.Combine(Path.GetDirectoryName(destino)!, "NexusBarTender.anterior.exe");
                    File.Copy(destino, backup, overwrite: true);
                }

                File.Copy(d.FileName, destino, overwrite: true);
                CarregarStatusVersaoBarTender();
                MessageBox.Show(this, "Executável do BarTender atualizado com sucesso!", "Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao copiar arquivo do BarTender: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnRestaurarBarTenderBackup_Click(object sender, RoutedEventArgs e)
    {
        var exeAlvo = TxtCaminhoBarTenderExe.Text.Trim();
        var pasta = Path.GetDirectoryName(exeAlvo) ?? AppContext.BaseDirectory;
        var backup = Path.Combine(pasta, "NexusBarTender.anterior.exe");

        if (!File.Exists(backup))
        {
            MessageBox.Show(this, "Nenhum arquivo de backup anterior do BarTender encontrado em:\n" + backup, "Backup Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, $"Deseja restaurar a versão anterior do BarTender ({backup})?", "Confirmar Restauração", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("NexusBarTender"))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }

                File.Copy(backup, exeAlvo, overwrite: true);
                CarregarStatusVersaoBarTender();
                MessageBox.Show(this, "Versão anterior do BarTender restaurada com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao restaurar backup do BarTender: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnVerificarAtualizacaoBarTender_Click(object sender, RoutedEventArgs e)
    {
        BtnVerificarAtualizacaoBarTender.IsEnabled = false;
        TxtStatusGeral.Text = "Consultando atualizações online para o Nexus BarTender...";
        try
        {
            var repo = ConfiguracaoAtualizacao.Repositorio();
            var fonte = new FonteGitHub(HttpCompartilhado, repo, "NexusBarTender.exe", "NexusBarTender.exe.sha256");
            var vi = File.Exists(TxtCaminhoBarTenderExe.Text) ? FileVersionInfo.GetVersionInfo(TxtCaminhoBarTenderExe.Text) : null;
            var versaoStr = vi?.ProductVersion?.Split('+')[0] ?? vi?.FileVersion ?? "1.0.0";
            if (!VersaoPrograma.TryParse(versaoStr, out var versaoAtual))
            {
                versaoAtual = new VersaoPrograma(1, 0, 0);
            }

            _atualizadorBarTender = new AtualizadorService(fonte, AtualizadorService.PastaPadrao(), versaoAtual);
            var res = await _atualizadorBarTender.VerificarAsync();

            if (res.Estado == EstadoVerificacao.Disponivel && res.Nova != null)
            {
                _versaoDisponivelBarTender = res.Nova;
                BrdNovidadeBarTender.Visibility = Visibility.Visible;
                TxtNovidadeTituloBarTender.Text = $"🎉 Nova Versão {_versaoDisponivelBarTender.Versao} do BarTender Disponível!";
                TxtNovidadeNotasBarTender.Text = string.IsNullOrWhiteSpace(_versaoDisponivelBarTender.Notas)
                    ? "Melhorias nos modelos de etiquetas, código de barras e desempenho de impressão."
                    : _versaoDisponivelBarTender.Notas;
                TxtStatusGeral.Text = $"Nova versão {_versaoDisponivelBarTender.Versao} do BarTender encontrada!";
            }
            else if (res.Estado == EstadoVerificacao.SemNovidade)
            {
                BrdNovidadeBarTender.Visibility = Visibility.Collapsed;
                TxtStatusGeral.Text = "O Nexus BarTender já está na versão mais recente disponível.";
                MessageBox.Show(this, "O Nexus BarTender já está atualizado na versão mais recente.", "Módulo Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                BrdNovidadeBarTender.Visibility = Visibility.Collapsed;
                TxtStatusGeral.Text = res.Mensagem;
                MessageBox.Show(this, res.Mensagem, "Verificação BarTender", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Erro ao verificar BarTender: " + ex.Message;
            MessageBox.Show(this, "Falha ao verificar atualizações do BarTender:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnVerificarAtualizacaoBarTender.IsEnabled = true;
        }
    }

    private async void BtnBaixarEInstalarBarTender_Click(object sender, RoutedEventArgs e)
    {
        if (_atualizadorBarTender is null || _versaoDisponivelBarTender is null) return;

        BtnBaixarEInstalarBarTender.IsEnabled = false;
        PrgAtualizacaoBarTender.Visibility = Visibility.Visible;
        PrgAtualizacaoBarTender.IsIndeterminate = true;
        var versaoBaixando = _versaoDisponivelBarTender.Versao;
        TxtStatusGeral.Text = $"Baixando BarTender versão {versaoBaixando}...";

        try
        {
            var progresso = new Progress<double>(p =>
            {
                PrgAtualizacaoBarTender.IsIndeterminate = false;
                PrgAtualizacaoBarTender.Value = p * 100;
                TxtStatusGeral.Text = $"Baixando atualização do BarTender... {p:P0}";
            });

            var arquivoBaixado = await _atualizadorBarTender.BaixarAsync(_versaoDisponivelBarTender, progresso, "NexusBarTender");
            TxtStatusGeral.Text = "Download do BarTender concluído com verificação SHA-256.";

            foreach (var p in Process.GetProcessesByName("NexusBarTender"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }

            var exeAlvo = TxtCaminhoBarTenderExe.Text.Trim();
            if (string.IsNullOrWhiteSpace(exeAlvo)) exeAlvo = @"C:\NexusLS\publicar\NexusBarTender.exe";

            if (File.Exists(exeAlvo))
            {
                var backup = Path.Combine(Path.GetDirectoryName(exeAlvo)!, "NexusBarTender.anterior.exe");
                File.Copy(exeAlvo, backup, overwrite: true);
            }

            AtualizadorService.TrocarExecutavel(exeAlvo, arquivoBaixado);
            CarregarStatusVersaoBarTender();
            _versaoDisponivelBarTender = null;
            BrdNovidadeBarTender.Visibility = Visibility.Collapsed;
            TxtStatusGeral.Text = "Nexus BarTender atualizado com sucesso para a versão " + versaoBaixando;
            MessageBox.Show(this, "Nexus BarTender atualizado com sucesso!", "Atualização Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Erro na atualização do BarTender: " + ex.Message;
            MessageBox.Show(this, "Falha na atualização do BarTender:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PrgAtualizacaoBarTender.Visibility = Visibility.Collapsed;
            BtnBaixarEInstalarBarTender.IsEnabled = true;
        }
    }

    /// <summary>Solicita o fechamento limpo da janela principal do Nexus LS antes de forçar o encerramento, protegendo o banco SQLite.</summary>
    private static void FecharProcessoNexusLSSeguro()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("NexusLS"))
            {
                try
                {
                    if (p.CloseMainWindow())
                    {
                        if (p.WaitForExit(4000)) continue;
                    }
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
            }
        }
        catch { }
    }

    private void BtnIniciarNexus_Click(object sender, RoutedEventArgs e)
    {
        var avaliacao = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
        if (!avaliacao.Ativa)
        {
            MessageBox.Show(
                this,
                $"Não é possível iniciar o Nexus LS porque a licença não está ativa neste computador.\n\nSituação atual: {TxtLicencaStatus.Text}\nDetalhe: {avaliacao.Mensagem}\n\nAtive ou renove sua chave na aba 'Licença Comercial'.",
                "Licença Necessária",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var exe = TxtCaminhoExe.Text.Trim();
        if (!File.Exists(exe))
        {
            MessageBox.Show(this, "Executável do Nexus LS não encontrado em:\n" + exe, "Executável Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });
            TxtStatusGeral.Text = "Nexus LS iniciado com sucesso.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Erro ao iniciar o Nexus LS: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnIniciarBarTender_Click(object sender, RoutedEventArgs e)
    {
        var avaliacao = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
        if (!avaliacao.Ativa)
        {
            MessageBox.Show(
                this,
                $"Não é possível iniciar o Nexus BarTender porque a licença não está ativa neste computador.\n\nSituação atual: {TxtLicencaStatus.Text}\nDetalhe: {avaliacao.Mensagem}\n\nAtive ou renove sua chave na aba 'Licença Comercial'.",
                "Licença Necessária",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var candidatos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "NexusBarTender.exe"),
            @"C:\NexusLS\publicar\NexusBarTender.exe",
            Path.Combine(Path.GetDirectoryName(TxtCaminhoExe.Text) ?? "", "NexusBarTender.exe")
        };

        var exe = candidatos.FirstOrDefault(File.Exists);
        if (exe == null)
        {
            MessageBox.Show(this, "Executável do Nexus BarTender (NexusBarTender.exe) não foi localizado.", "Módulo Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });
            TxtStatusGeral.Text = "Nexus BarTender iniciado com sucesso.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Erro ao iniciar o Nexus BarTender: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- ABA 3: DIAGNÓSTICO & SUPORTE ---

    private async void BtnTestarConexao_Click(object sender, RoutedEventArgs e)
    {
        BtnTestarConexao.IsEnabled = false;
        TxtStatusGeral.Text = "Testando comunicação com o servidor de licenças em nuvem...";
        try
        {
            var r = await _licenca.VerificarAsync();
            MostrarLicenca(r);
            if (r.Estado == EstadoLicencaNuvem.SemConexao)
            {
                MessageBox.Show(this, "Não foi possível comunicar com o servidor de licenças:\n" + r.Mensagem, "Sem Conexão", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(this, $"Conexão estabelecida com sucesso com o servidor Firebase (nexus-ls)!\n\nSituação da licença: {TxtLicencaStatus.Text}", "Conexão OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            TxtStatusGeral.Text = "Teste de conexão concluído.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao testar conexão: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnTestarConexao.IsEnabled = true;
        }
    }

    private void BtnCopiarDiagnostico_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
            var relatorio =
                $"=== DIAGNÓSTICO NEXUS LS ===\r\n" +
                $"Data/Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n" +
                $"Computador: {Environment.MachineName}\r\n" +
                $"Código HWID: {LicencaService.CodigoDaMaquina()}\r\n" +
                $"Sistema: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\r\n" +
                $"Status Licença: {TxtLicencaStatus.Text}\r\n" +
                $"Cliente: {TxtLicencaCliente.Text} | Empresa: {TxtLicencaEmpresa.Text}\r\n" +
                $"Validade: {TxtLicencaValidade.Text}\r\n" +
                $"Versão Executável: {TxtVersaoStatus.Text}\r\n" +
                $"Caminho Exe: {TxtCaminhoExe.Text}";

            Clipboard.SetText(relatorio);
            TxtStatusGeral.Text = "Relatório de diagnóstico copiado para a área de transferência!";
            MessageBox.Show(this, "O relatório de diagnóstico foi copiado!\nAgora basta colar (Ctrl+V) na conversa com o suporte.", "Relatório Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível copiar o diagnóstico: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnAbrirPastaDados_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS");
            Directory.CreateDirectory(pasta);
            Process.Start(new ProcessStartInfo(pasta) { UseShellExecute = true });
            TxtStatusGeral.Text = "Pasta de dados locais aberta: " + pasta;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir a pasta de dados: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var alvoExe = Process.GetCurrentProcess().MainModule?.FileName ?? @"C:\NexusLS\publicar\atualizador.exe";

            if (!File.Exists(alvoExe) || alvoExe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                alvoExe = @"C:\NexusLS\publicar\atualizador.exe";
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
