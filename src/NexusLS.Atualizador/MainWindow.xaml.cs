using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        ServicoLicencaNuvem.CaminhoPadrao(),
        fonteConsulta: new FonteLicencaFirestore(HttpCompartilhado));

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

        // Faz checagem automática e não bloqueante do Git vs Instalado na inicialização
        _ = VerificarAtualizacoesGitBackgroundAsync();
        _ = AutoAtualizarAtualizadorAsync();

        // Processar argumentos de linha de comando se houver
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--atalho"))
        {
            CriarAtalhoDesktop(silencioso: true);
        }

        if (args.Any(a => a.Equals("--bartender", StringComparison.OrdinalIgnoreCase) || a.Equals("-b", StringComparison.OrdinalIgnoreCase)))
        {
            NavegarPara("atualizacoes");
        }
        else if (args.Any(a => a.Equals("--licenca", StringComparison.OrdinalIgnoreCase) || a.Equals("-l", StringComparison.OrdinalIgnoreCase)))
        {
            NavegarPara("licenca");
        }
        else if (args.Any(a => a.Equals("--sistema", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase)))
        {
            NavegarPara("atualizacoes");
        }
        else if (args.Any(a => a.Equals("--diagnostico", StringComparison.OrdinalIgnoreCase) || a.Equals("-d", StringComparison.OrdinalIgnoreCase)))
        {
            NavegarPara("diagnostico");
        }
        else
        {
            NavegarPara("inicio");
        }
    }

    // ==================== NAVEGAÇÃO ENTRE AMBIENTES ====================

    private void NavegarPara(string pagina)
    {
        PageInicio.Visibility = pagina == "inicio" ? Visibility.Visible : Visibility.Collapsed;
        PageAtualizacoes.Visibility = pagina == "atualizacoes" ? Visibility.Visible : Visibility.Collapsed;
        PageLicenca.Visibility = pagina == "licenca" ? Visibility.Visible : Visibility.Collapsed;
        PageDiagnostico.Visibility = pagina == "diagnostico" ? Visibility.Visible : Visibility.Collapsed;
        PageSobre.Visibility = pagina == "sobre" ? Visibility.Visible : Visibility.Collapsed;

        AtualizarMenu(BtnMenuInicio, pagina == "inicio");
        AtualizarMenu(BtnMenuAtualizacoes, pagina == "atualizacoes");
        AtualizarMenu(BtnMenuLicenca, pagina == "licenca");
        AtualizarMenu(BtnMenuDiagnostico, pagina == "diagnostico");
        AtualizarMenu(BtnMenuSobre, pagina == "sobre");

        switch (pagina)
        {
            case "inicio":
                TxtTituloPagina.Text = "Início — Central de Aplicativos";
                TxtSubtituloPagina.Text = "Gerenciamento e inicialização rápida do ecossistema Nexus";
                break;
            case "atualizacoes":
                TxtTituloPagina.Text = "Atualizações do Sistema";
                TxtSubtituloPagina.Text = "Gerenciamento de versões e pacotes de atualização para o Nexus LS e BarTender";
                break;
            case "licenca":
                TxtTituloPagina.Text = "Licenciamento Comercial";
                TxtSubtituloPagina.Text = "Controle de ativação, plano contratado, validade e dispositivos autorizados";
                break;
            case "diagnostico":
                TxtTituloPagina.Text = "Diagnóstico e Suporte Técnico";
                TxtSubtituloPagina.Text = "Verificações automáticas de integridade, comunicação em nuvem e ambiente";
                break;
            case "sobre":
                TxtTituloPagina.Text = "Sobre o Nexus Hub";
                TxtSubtituloPagina.Text = "Informações da plataforma, versão dos aplicativos e suporte oficial";
                break;
        }
    }

    private static void AtualizarMenu(Button btn, bool ativo)
    {
        btn.Tag = ativo ? "Ativo" : null;
    }

    private void BtnMenuInicio_Click(object sender, RoutedEventArgs e) => NavegarPara("inicio");
    private void BtnMenuAtualizacoes_Click(object sender, RoutedEventArgs e) => NavegarPara("atualizacoes");
    private void BtnMenuLicenca_Click(object sender, RoutedEventArgs e) => NavegarPara("licenca");
    private void BtnMenuDiagnostico_Click(object sender, RoutedEventArgs e) => NavegarPara("diagnostico");
    private void BtnMenuSobre_Click(object sender, RoutedEventArgs e) => NavegarPara("sobre");

    // ==================== CHECAGEM E SINCRONIZAÇÃO COM O GITHUB ====================

    private async Task VerificarAtualizacoesGitBackgroundAsync()
    {
        try
        {
            TxtRotuloSincronizarGit.Text = "Checando Git...";
            var repo = ConfiguracaoAtualizacao.Repositorio();

            // 1. Verificar Nexus LS
            var exeNexus = TxtCaminhoExe.Text.Trim();
            if (!File.Exists(exeNexus)) exeNexus = LocalizarExecutavelPadrao();
            var fonteNexus = new FonteGitHub(HttpCompartilhado, repo);
            var viNexus = File.Exists(exeNexus) ? FileVersionInfo.GetVersionInfo(exeNexus) : null;
            var versaoNexusStr = viNexus?.ProductVersion?.Split('+')[0] ?? "1.0.0";
            VersaoPrograma.TryParse(versaoNexusStr, out var versaoNexus);
            _atualizador = new AtualizadorService(fonteNexus, AtualizadorService.PastaPadrao(), versaoNexus);
            var resNexus = await _atualizador.VerificarAsync(exeNexus).ConfigureAwait(false);

            // 2. Verificar Nexus BarTender
            var exeBt = TxtCaminhoBarTenderExe.Text.Trim();
            if (!File.Exists(exeBt)) exeBt = LocalizarBarTenderPadrao();
            var fonteBt = new FonteGitHub(HttpCompartilhado, repo, "NexusBarTender.exe", "NexusBarTender.exe.sha256");
            var viBt = File.Exists(exeBt) ? FileVersionInfo.GetVersionInfo(exeBt) : null;
            var versaoBtStr = viBt?.ProductVersion?.Split('+')[0] ?? "1.0.0";
            VersaoPrograma.TryParse(versaoBtStr, out var versaoBt);
            _atualizadorBarTender = new AtualizadorService(fonteBt, AtualizadorService.PastaPadrao(), versaoBt);
            var resBt = await _atualizadorBarTender.VerificarAsync(exeBt).ConfigureAwait(false);

            Dispatcher.Invoke(() =>
            {
                AtualizarUiAposChecagemGit(resNexus, resBt);
            });
        }
        catch
        {
            Dispatcher.Invoke(() =>
            {
                TxtRotuloSincronizarGit.Text = "Sincronizar Git";
            });
        }
    }

    private void AtualizarUiAposChecagemGit(ResultadoVerificacao resNexus, ResultadoVerificacao resBt)
    {
        TxtRotuloSincronizarGit.Text = "Sincronizado";

        // --- Nexus LS ---
        if (resNexus.Estado == EstadoVerificacao.SemNovidade)
        {
            TxtBadgeNexusLS.Text = $"● Atualizado v{resNexus.Atual} (Git)";
            TxtBadgeNexusLS.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            TxtVersaoDetalheNexus.Text = $"v{resNexus.Atual} (Sincronizado com o GitHub)";
            TxtVersaoStatus.Text = $"v{resNexus.Atual} (Sincronizado Git)";
            TxtHomeVersaoInstalada.Text = $"v{resNexus.Atual}";
            TxtVersaoArquivo.Text = $"Hash SHA-256 verificado com o repositório oficial ({resNexus.Atual})";
            BrdNovidade.Visibility = Visibility.Collapsed;
        }
        else if (resNexus.Estado == EstadoVerificacao.Disponivel && resNexus.Nova != null)
        {
            _versaoDisponivel = resNexus.Nova;
            TxtBadgeNexusLS.Text = $"⚠️ Nova versão v{resNexus.Nova.Versao}!";
            TxtBadgeNexusLS.Foreground = (SolidColorBrush)FindResource("CorAviso");
            TxtVersaoDetalheNexus.Text = $"Instalada: v{resNexus.Atual} • Nova v{resNexus.Nova.Versao} no Git";
            TxtVersaoStatus.Text = $"v{resNexus.Atual} (v{resNexus.Nova.Versao} disponível)";
            BadgeMenuUpdate.Visibility = Visibility.Visible;
            TxtNovidadeTitulo.Text = $"🎉 Nova Versão {resNexus.Nova.Versao} Disponível no Git!";
            TxtNovidadeNotas.Text = string.IsNullOrWhiteSpace(resNexus.Nova.Notas)
                ? "Atualização oficial com melhorias críticas e estabilidade."
                : resNexus.Nova.Notas;
            BrdNovidade.Visibility = Visibility.Visible;
            TxtRotuloSincronizarGit.Text = "Atualizar Apps";
        }

        // --- BarTender ---
        if (resBt.Estado == EstadoVerificacao.SemNovidade)
        {
            TxtBadgeBarTender.Text = $"● Atualizado v{resBt.Atual} (Git)";
            TxtBadgeBarTender.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            TxtVersaoDetalheBarTender.Text = $"v{resBt.Atual} (Sincronizado com o GitHub)";
            TxtStatusModuloBarTender.Text = $"v{resBt.Atual} (Sincronizado Git)";
            TxtVersaoBarTenderInstalada.Text = $"Hash SHA-256 verificado com o repositório oficial ({resBt.Atual})";
            BrdNovidadeBarTender.Visibility = Visibility.Collapsed;
        }
        else if (resBt.Estado == EstadoVerificacao.Disponivel && resBt.Nova != null)
        {
            _versaoDisponivelBarTender = resBt.Nova;
            TxtBadgeBarTender.Text = $"⚠️ Nova versão v{resBt.Nova.Versao}!";
            TxtBadgeBarTender.Foreground = (SolidColorBrush)FindResource("CorAviso");
            TxtVersaoDetalheBarTender.Text = $"Instalada: v{resBt.Atual} • Nova v{resBt.Nova.Versao} no Git";
            BadgeMenuUpdate.Visibility = Visibility.Visible;
            TxtNovidadeTituloBarTender.Text = $"🎉 Nova Versão {resBt.Nova.Versao} do BarTender no Git!";
            TxtNovidadeNotasBarTender.Text = string.IsNullOrWhiteSpace(resBt.Nova.Notas)
                ? "Melhorias de impressão e modelos de etiquetas."
                : resBt.Nova.Notas;
            BrdNovidadeBarTender.Visibility = Visibility.Visible;
            TxtRotuloSincronizarGit.Text = "Atualizar Apps";
        }

        if (resNexus.Estado == EstadoVerificacao.SemNovidade && resBt.Estado == EstadoVerificacao.SemNovidade)
        {
            BadgeMenuUpdate.Visibility = Visibility.Collapsed;
            TxtStatusGeral.Text = $"Todos os programas estão 100% atualizados com o GitHub (v{resNexus.Atual}).";
        }
    }

    private async void BtnSincronizarTudo_Click(object sender, RoutedEventArgs e)
    {
        BtnSincronizarGit.IsEnabled = false;
        TxtStatusGeral.Text = "Consultando e sincronizando com o GitHub...";
        try
        {
            var repo = ConfiguracaoAtualizacao.Repositorio();
            var exeNexus = TxtCaminhoExe.Text.Trim();
            if (!File.Exists(exeNexus)) exeNexus = LocalizarExecutavelPadrao();
            var exeBt = TxtCaminhoBarTenderExe.Text.Trim();
            if (!File.Exists(exeBt)) exeBt = LocalizarBarTenderPadrao();

            var fonteNexus = new FonteGitHub(HttpCompartilhado, repo);
            var viNexus = File.Exists(exeNexus) ? FileVersionInfo.GetVersionInfo(exeNexus) : null;
            var versaoNexusStr = viNexus?.ProductVersion?.Split('+')[0] ?? "1.0.0";
            VersaoPrograma.TryParse(versaoNexusStr, out var versaoNexus);
            _atualizador = new AtualizadorService(fonteNexus, AtualizadorService.PastaPadrao(), versaoNexus);
            var resNexus = await _atualizador.VerificarAsync(exeNexus);

            var fonteBt = new FonteGitHub(HttpCompartilhado, repo, "NexusBarTender.exe", "NexusBarTender.exe.sha256");
            var viBt = File.Exists(exeBt) ? FileVersionInfo.GetVersionInfo(exeBt) : null;
            var versaoBtStr = viBt?.ProductVersion?.Split('+')[0] ?? "1.0.0";
            VersaoPrograma.TryParse(versaoBtStr, out var versaoBt);
            _atualizadorBarTender = new AtualizadorService(fonteBt, AtualizadorService.PastaPadrao(), versaoBt);
            var resBt = await _atualizadorBarTender.VerificarAsync(exeBt);

            AtualizarUiAposChecagemGit(resNexus, resBt);

            bool precisaNexus = resNexus.Estado == EstadoVerificacao.Disponivel && resNexus.Nova != null;
            bool precisaBt = resBt.Estado == EstadoVerificacao.Disponivel && resBt.Nova != null;

            if (!precisaNexus && !precisaBt)
            {
                TxtStatusGeral.Text = $"Todos os programas estão 100% atualizados com o GitHub (v{resNexus.Atual}).";
                MessageBox.Show(
                    this,
                    $"Todos os aplicativos do Nexus já estão atualizados na versão mais recente oficial do GitHub (v{resNexus.Atual})!\n\n" +
                    $"• Nexus LS: Sincronizado (hash SHA-256 verificado)\n" +
                    $"• Nexus BarTender: Sincronizado (hash SHA-256 verificado)\n\n" +
                    "O programa está pronto e nenhuma atualização é necessária.",
                    "Sistemas Atualizados",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"Foram encontradas atualizações no GitHub:\n\n" +
                (precisaNexus ? $"• Nexus LS: versão {resNexus.Nova!.Versao}\n" : "• Nexus LS: Atualizado\n") +
                (precisaBt ? $"• Nexus BarTender: versão {resBt.Nova!.Versao}\n" : "• Nexus BarTender: Atualizado\n") +
                "\nDeseja baixar e atualizar os aplicativos agora?",
                "Atualizar com Git",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            NavegarPara("atualizacoes");

            if (precisaNexus)
            {
                _versaoDisponivel = resNexus.Nova;
                BtnBaixarEInstalar_Click(sender, e);
            }
            if (precisaBt)
            {
                _versaoDisponivelBarTender = resBt.Nova;
                BtnBaixarEInstalarBarTender_Click(sender, e);
            }
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Erro ao sincronizar com Git: " + ex.Message;
            MessageBox.Show(this, "Falha ao sincronizar com o GitHub:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnSincronizarGit.IsEnabled = true;
        }
    }

    // ==================== LOCALIZAÇÃO E VERSÕES ====================

    private static string LocalizarExecutavelPadrao()
    {
        var candidatos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "NexusLS.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "NexusLS.exe"),
            @"C:\Nexus\NexusLS.exe",
            @"C:\NexusLS\NexusLS.exe",
            @"C:\NexusLS\publicar\NexusLS.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NexusLS", "NexusLS.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexus", "NexusLS.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "NexusLS.exe")
        };

        return candidatos.FirstOrDefault(File.Exists) ?? candidatos[0];
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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexus", "NexusBarTender.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "NexusBarTender.exe")
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
            TxtSidebarHwid.Text = $"HWID: {hwid}";

            TxtDiagSO.Text = RuntimeInformation.OSDescription;
            TxtDiagComputador.Text = $"{Environment.MachineName} ({RuntimeInformation.OSArchitecture})";
            TxtDiagDotNet.Text = RuntimeInformation.FrameworkDescription;
            TxtDiagPastaExec.Text = AppContext.BaseDirectory;
        }
        catch (Exception ex)
        {
            TxtCodigoMaquina.Text = "Erro: " + ex.Message;
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
                var tamMb = Math.Max(1, fi.Length / (1024 * 1024));

                TxtVersaoStatus.Text = $"Versão v{verLimpa} ({tamMb} MB)";
                TxtVersaoArquivo.Text = $"v{verLimpa} ({tamMb} MB) — Atualizado em {fi.LastWriteTime:dd/MM/yyyy HH:mm}";
                TxtStatusGeral.Text = $"Nexus LS pronto em: {exe}";

                TxtVersaoDetalheNexus.Text = $"v{verLimpa} ({tamMb} MB)";
                TxtHomeVersaoInstalada.Text = $"v{verLimpa}";
                TxtHomeUltimaAtualizacao.Text = fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm");
                TxtSobreVersaoNexus.Text = $"v{verLimpa}";

                TxtBadgeNexusLS.Text = "● Instalado";
                TxtBadgeNexusLS.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            }
            catch (Exception ex)
            {
                TxtVersaoStatus.Text = "Arquivo presente (ilegível)";
                TxtVersaoArquivo.Text = ex.Message;
                TxtVersaoDetalheNexus.Text = "Presente";
                TxtBadgeNexusLS.Text = "○ Presente";
            }
        }
        else
        {
            TxtVersaoStatus.Text = "Não localizado";
            TxtVersaoArquivo.Text = "Selecione o arquivo NexusLS.exe na página Atualizações";
            TxtVersaoDetalheNexus.Text = "Não instalado";
            TxtHomeVersaoInstalada.Text = "Não instalado";
            TxtBadgeNexusLS.Text = "○ Não Instalado";
            TxtBadgeNexusLS.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
        }
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

                TxtStatusModuloBarTender.Text = $"Módulo instalado: v{versao} ({mb:F0} MB)";
                TxtVersaoBarTenderInstalada.Text = $"v{versao} ({mb:F0} MB) — Modificado em: {fi.LastWriteTime:dd/MM/yyyy HH:mm}";
                TxtVersaoDetalheBarTender.Text = $"v{versao} ({mb:F0} MB)";
                TxtSobreVersaoBarTender.Text = $"v{versao}";

                TxtBadgeBarTender.Text = "● Instalado";
                TxtBadgeBarTender.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            }
            else
            {
                TxtStatusModuloBarTender.Text = "Executável do BarTender não encontrado no caminho informado.";
                TxtVersaoBarTenderInstalada.Text = "Módulo BarTender não localizado neste computador.";
                TxtVersaoDetalheBarTender.Text = "Não instalado";
                TxtSobreVersaoBarTender.Text = "Não instalado";

                TxtBadgeBarTender.Text = "○ Opcional";
                TxtBadgeBarTender.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
            }
        }
        catch
        {
            TxtStatusModuloBarTender.Text = "Módulo BarTender opcional.";
            TxtVersaoDetalheBarTender.Text = "Opcional";
            TxtBadgeBarTender.Text = "○ Opcional";
        }
    }

    // ==================== LICENCIAMENTO ====================

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
            TxtSidebarConexao.Text = "Offline";
            DotStatusConexao.Fill = (SolidColorBrush)FindResource("CorAviso");
        }
    }

    private void MostrarLicenca(ResultadoLicencaNuvem r)
    {
        _ultimoResultadoLicenca = r;

        if (!string.IsNullOrWhiteSpace(r.Chave) && string.IsNullOrWhiteSpace(TxtChaveLicenca.Text))
        {
            TxtChaveLicenca.Text = r.Chave;
        }

        var statusNome = r.Estado switch
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

        var statusCor = r.Estado switch
        {
            EstadoLicencaNuvem.Valida => (SolidColorBrush)FindResource("CorPositivo"),
            EstadoLicencaNuvem.Vencendo or EstadoLicencaNuvem.SemConexao => (SolidColorBrush)FindResource("CorAviso"),
            EstadoLicencaNuvem.NaoEncontrada => (SolidColorBrush)FindResource("CorTextoSuave"),
            _ => (SolidColorBrush)FindResource("CorPerigo")
        };

        // Header e Sidebar
        TxtTopoEmpresa.Text = string.IsNullOrWhiteSpace(r.Empresa) ? "NEXUS HUB" : r.Empresa.ToUpperInvariant();
        TxtTopoLicencaBadge.Text = r.Ativa ? (r.Offline ? "● Licença Offline" : "● Licença Ativa") : "● Licença Não Ativada";
        TxtTopoLicencaBadge.Foreground = statusCor;
        TxtSidebarConexao.Text = r.Offline ? "Modo Offline" : "Conectado";
        DotStatusConexao.Fill = r.Offline ? (SolidColorBrush)FindResource("CorAviso") : (SolidColorBrush)FindResource("CorPositivo");

        // Informações da Instalação (Página Início)
        TxtHomeCliente.Text = string.IsNullOrWhiteSpace(r.Cliente) ? "—" : r.Cliente;
        TxtHomeEmpresa.Text = string.IsNullOrWhiteSpace(r.Empresa) ? "—" : r.Empresa;
        TxtHomePlano.Text = string.IsNullOrWhiteSpace(r.Plano) ? (r.Ativa ? "COMERCIAL" : "—") : r.Plano.ToUpperInvariant();
        TxtHomeLicencaStatus.Text = statusNome;
        TxtHomeLicencaStatus.Foreground = statusCor;
        TxtHomeValidade.Text = r.Validade is { } v ? $"{v:dd/MM/yyyy}" : (r.Ativa ? "Vitalícia" : "—");
        TxtHomeDiasRestantes.Text = r.DiasRestantes is { } d ? $"{d} dia(s)" : (r.Ativa ? "Ilimitado" : "0");

        // Página de Licença
        TxtLicencaStatus.Text = statusNome;
        TxtLicencaStatus.Foreground = statusCor;
        TxtLicencaDetalhe.Text = r.Mensagem;
        TxtLicencaCliente.Text = string.IsNullOrWhiteSpace(r.Cliente) ? "—" : r.Cliente;
        TxtLicencaEmpresa.Text = string.IsNullOrWhiteSpace(r.Empresa) ? "—" : r.Empresa;
        TxtLicencaPlano.Text = string.IsNullOrWhiteSpace(r.Plano) ? (r.Ativa ? "COMERCIAL" : "—") : r.Plano.ToUpperInvariant();

        TxtLicencaValidade.Text = r.Validade is { } v2
            ? $"{v2:dd/MM/yyyy}" + (r.DiasRestantes is { } d2 ? $" ({d2} dia(s) restantes)" : "")
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
    }

    private void CardAppNexus_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            BtnIniciarNexus_Click(sender, e);
        }
    }

    private void CardAppBarTender_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            BtnIniciarBarTender_Click(sender, e);
        }
    }

    private void BtnIniciarNexus_Click(object sender, RoutedEventArgs e)
    {
        var avaliacao = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
        if (!avaliacao.Ativa)
        {
            MessageBox.Show(
                this,
                $"Não é possível iniciar o Nexus LS porque a licença não está ativa neste computador.\n\nSituação atual: {TxtLicencaStatus.Text}\nDetalhe: {avaliacao.Mensagem}\n\nAtive ou valide sua chave no menu 'Licença'.",
                "Licença Necessária",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            NavegarPara("licenca");
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
                $"Não é possível iniciar o Nexus BarTender porque a licença não está ativa neste computador.\n\nSituação atual: {TxtLicencaStatus.Text}\nDetalhe: {avaliacao.Mensagem}\n\nAtive ou valide sua chave no menu 'Licença'.",
                "Licença Necessária",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            NavegarPara("licenca");
            return;
        }

        var candidatos = new[]
        {
            TxtCaminhoBarTenderExe.Text.Trim(),
            Path.Combine(AppContext.BaseDirectory, "NexusBarTender.exe"),
            @"C:\NexusLS\publicar\NexusBarTender.exe",
            Path.Combine(Path.GetDirectoryName(TxtCaminhoExe.Text) ?? "", "NexusBarTender.exe")
        };

        var exe = candidatos.FirstOrDefault(File.Exists);
        if (exe == null)
        {
            MessageBox.Show(this, "Executável do Nexus BarTender (NexusBarTender.exe) não foi localizado neste computador.\nVocê pode instalá-lo ou atualizar pelo menu Atualizações.", "Módulo Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    // ==================== EVENTOS DE LICENÇA ====================

    private void BtnCopiarHwid_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwid = LicencaService.CodigoDaMaquina();
            Clipboard.SetText(hwid);
            TxtStatusGeral.Text = $"Código deste computador ({hwid}) copiado para a área de transferência!";
            BtnCopiarHwid.Content = "✓ HWID Copiado!";
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

    private async void BtnConsultarChave_Click(object sender, RoutedEventArgs e)
    {
        var chave = TxtChaveLicenca.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(chave))
        {
            MessageBox.Show(this, "Digite ou cole a chave da licença antes de consultar.", "Chave Obrigatória", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtChaveLicenca.Focus();
            return;
        }

        BtnConsultarChave.IsEnabled = false;
        BtnAtivarChave.IsEnabled = false;
        BtnRevalidarLicenca.IsEnabled = false;
        TxtStatusGeral.Text = "Consultando a chave no servidor central Nexus LS (somente leitura)...";
        try
        {
            var r = await _licenca.ConsultarAsync(chave);

            TxtResultadoLicenca.Text = r.Mensagem;
            TxtResultadoLicenca.Foreground = r.Estado switch
            {
                EstadoLicencaNuvem.Valida or EstadoLicencaNuvem.Vencendo => (SolidColorBrush)FindResource("CorPositivo"),
                EstadoLicencaNuvem.SemConexao => (SolidColorBrush)FindResource("CorAviso"),
                _ => (SolidColorBrush)FindResource("CorPerigo")
            };

            // Atualiza apenas o bloco de detalhes: o estado da licença ativa/header continua o mesmo.
            TxtLicencaCliente.Text = string.IsNullOrWhiteSpace(r.Cliente) ? "—" : r.Cliente;
            TxtLicencaEmpresa.Text = string.IsNullOrWhiteSpace(r.Empresa) ? "—" : r.Empresa;
            TxtLicencaPlano.Text = string.IsNullOrWhiteSpace(r.Plano) ? "—" : r.Plano.ToUpperInvariant();
            TxtLicencaValidade.Text = r.Validade is { } v
                ? $"{v:dd/MM/yyyy}" + (r.DiasRestantes is { } d ? $" ({d} dia(s) restantes)" : "")
                : "—";

            if (r.DispositivosUsados.HasValue && r.LimiteDispositivos.HasValue)
            {
                var vinculo = r.EsteComputadorVinculado == true
                    ? " • este computador vinculado"
                    : r.TemVagaNesteComputador == true
                        ? " • há vaga neste computador"
                        : " • limite atingido";
                TxtLicencaDispositivos.Text = $"{r.DispositivosUsados} de {r.LimiteDispositivos} computador(es){vinculo}";
            }
            else
            {
                TxtLicencaDispositivos.Text = "—";
            }

            TxtLicencaVerificacao.Text = r.VerificadaEm is { } ver ? $"{ver:dd/MM/yyyy HH:mm} (Consulta)" : "—";

            if (r.MaquinasVinculadas is { } maquinas)
            {
                var lista = maquinas.Count > 0 ? string.Join(", ", maquinas) : "nenhum computador vinculado";
                TxtLicencaDetalhe.Text = $"{r.Mensagem}\nComputadores vinculados: {lista}.";
            }
            else
            {
                TxtLicencaDetalhe.Text = r.Mensagem;
            }

            TxtStatusGeral.Text = "Consulta concluída (somente leitura, nada foi vinculado).";
        }
        finally
        {
            BtnConsultarChave.IsEnabled = true;
            BtnAtivarChave.IsEnabled = true;
            BtnRevalidarLicenca.IsEnabled = true;
        }
    }

    private async void BtnTestarConexao_Click(object sender, RoutedEventArgs e)
    {
        BtnTestarConexao.IsEnabled = false;
        TxtStatusGeral.Text = "Testando a conexão com o serviço de licenças...";
        try
        {
            var r = await _licenca.TestarConexaoAsync();
            if (r.Online)
            {
                TxtResultadoLicenca.Text = $"Servidor online em {r.LatenciaMs} ms.";
                TxtResultadoLicenca.Foreground = (SolidColorBrush)FindResource("CorPositivo");
                TxtSidebarConexao.Text = "Conectado";
                DotStatusConexao.Fill = (SolidColorBrush)FindResource("CorPositivo");
                TxtStatusGeral.Text = "Serviço de licenças acessível.";
            }
            else
            {
                TxtResultadoLicenca.Text = $"Sem conexão: {r.Mensagem}";
                TxtResultadoLicenca.Foreground = (SolidColorBrush)FindResource("CorPerigo");
                TxtSidebarConexao.Text = "Offline";
                DotStatusConexao.Fill = (SolidColorBrush)FindResource("CorAviso");
                TxtStatusGeral.Text = "Não foi possível falar com o serviço de licenças.";
            }
        }
        finally
        {
            BtnTestarConexao.IsEnabled = true;
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

    // ==================== ATUALIZAÇÕES DO NEXUS LS ====================

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
            var exeAlvo = TxtCaminhoExe.Text.Trim();
            if (!File.Exists(exeAlvo)) exeAlvo = LocalizarExecutavelPadrao();

            var vi = File.Exists(exeAlvo) ? FileVersionInfo.GetVersionInfo(exeAlvo) : null;
            var versaoStr = vi?.ProductVersion?.Split('+')[0] ?? "1.0.0";
            if (!VersaoPrograma.TryParse(versaoStr, out var versaoAtual))
            {
                versaoAtual = new VersaoPrograma(1, 0, 0);
            }

            _atualizador = new AtualizadorService(fonte, AtualizadorService.PastaPadrao(), versaoAtual);
            var res = await _atualizador.VerificarAsync(exeAlvo);

            if (res.Estado == EstadoVerificacao.Disponivel && res.Nova != null)
            {
                _versaoDisponivel = res.Nova;
                BrdNovidade.Visibility = Visibility.Visible;
                TxtNovidadeTitulo.Text = $"🎉 Nova Versão {_versaoDisponivel.Versao} Disponível!";
                TxtNovidadeNotas.Text = string.IsNullOrWhiteSpace(_versaoDisponivel.Notas)
                    ? "Melhorias de estabilidade, segurança e desempenho."
                    : _versaoDisponivel.Notas;
                TxtStatusGeral.Text = $"Nova versão {_versaoDisponivel.Versao} encontrada no GitHub!";
                BadgeMenuUpdate.Visibility = Visibility.Visible;
                TxtBadgeNexusLS.Text = $"⚠️ Nova v{_versaoDisponivel.Versao}!";
                TxtBadgeNexusLS.Foreground = (SolidColorBrush)FindResource("CorAviso");
            }
            else if (res.Estado == EstadoVerificacao.SemNovidade)
            {
                BrdNovidade.Visibility = Visibility.Collapsed;
                BadgeMenuUpdate.Visibility = Visibility.Collapsed;
                TxtVersaoStatus.Text = $"v{res.Atual} (Sincronizado Git)";
                TxtVersaoArquivo.Text = $"Hash SHA-256 verificado com o Git ({res.Atual})";
                TxtBadgeNexusLS.Text = $"● Atualizado v{res.Atual} (Git)";
                TxtBadgeNexusLS.Foreground = (SolidColorBrush)FindResource("CorPositivo");
                TxtVersaoDetalheNexus.Text = $"v{res.Atual} (Sincronizado com o GitHub)";
                TxtStatusGeral.Text = "O Nexus LS já está atualizado com a versão oficial do GitHub.";
                MessageBox.Show(this, $"O Nexus LS já está totalmente atualizado na versão v{res.Atual}.\nO executável instalado confere exatamente com o repositório oficial do GitHub.", "Sistema Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
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
                TxtStatusGeral.Text = $"Baixando atualização do Nexus LS... {p:P0}";
            });

            var versaoInstaladaNova = _versaoDisponivel.Versao;
            var arquivoBaixado = await _atualizador.BaixarAsync(_versaoDisponivel, progresso, "NexusLS");
            TxtStatusGeral.Text = "Download concluído e conferido com assinatura SHA-256.";

            if (!FecharProcessoSeguro("NexusLS"))
            {
                AvisarQueNaoFechou("Nexus LS");
                return;
            }

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
                if (!FecharProcessoSeguro("NexusLS"))
                {
                    AvisarQueNaoFechou("Nexus LS");
                    return;
                }

                // Mesma troca segura da atualização: copia para um intermediário e renomeia (nunca deixa pela metade).
                var pastaExe = Path.GetDirectoryName(Path.GetFullPath(exeAlvo)) ?? AppContext.BaseDirectory;
                var intermediario = Path.Combine(pastaExe, "restaurar-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(backup, intermediario, overwrite: true);
                AtualizadorService.TrocarExecutavel(exeAlvo, intermediario);
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
                if (!FecharProcessoSeguro("NexusLS"))
                {
                    AvisarQueNaoFechou("Nexus LS");
                    return;
                }

                var exeAlvo = TxtCaminhoExe.Text.Trim();
                if (File.Exists(exeAlvo))
                {
                    var backup = Path.Combine(Path.GetDirectoryName(exeAlvo)!, "NexusLS.anterior.exe");
                    File.Copy(exeAlvo, backup, overwrite: true);
                }

                // O arquivo escolhido é copiado para um intermediário: a troca não apaga o original do operador.
                var pastaExe = Path.GetDirectoryName(Path.GetFullPath(exeAlvo)) ?? AppContext.BaseDirectory;
                var intermediario = Path.Combine(pastaExe, "novo-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(ofd.FileName, intermediario, overwrite: true);
                AtualizadorService.TrocarExecutavel(exeAlvo, intermediario);
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

    // ==================== ATUALIZAÇÕES DO BARTENDER ====================

    private void BtnSelecionarBarTenderExe_Click(object sender, RoutedEventArgs e)
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

    private async void BtnVerificarAtualizacaoBarTender_Click(object sender, RoutedEventArgs e)
    {
        BtnVerificarAtualizacaoBarTender.IsEnabled = false;
        TxtStatusGeral.Text = "Consultando atualizações online para o Nexus BarTender...";
        try
        {
            var repo = ConfiguracaoAtualizacao.Repositorio();
            var fonte = new FonteGitHub(HttpCompartilhado, repo, "NexusBarTender.exe", "NexusBarTender.exe.sha256");
            var exeAlvoBt = TxtCaminhoBarTenderExe.Text.Trim();
            if (!File.Exists(exeAlvoBt)) exeAlvoBt = LocalizarBarTenderPadrao();

            var vi = File.Exists(exeAlvoBt) ? FileVersionInfo.GetVersionInfo(exeAlvoBt) : null;
            var versaoStr = vi?.ProductVersion?.Split('+')[0] ?? vi?.FileVersion ?? "1.0.0";
            if (!VersaoPrograma.TryParse(versaoStr, out var versaoAtual))
            {
                versaoAtual = new VersaoPrograma(1, 0, 0);
            }

            _atualizadorBarTender = new AtualizadorService(fonte, AtualizadorService.PastaPadrao(), versaoAtual);
            var res = await _atualizadorBarTender.VerificarAsync(exeAlvoBt);

            if (res.Estado == EstadoVerificacao.Disponivel && res.Nova != null)
            {
                _versaoDisponivelBarTender = res.Nova;
                BrdNovidadeBarTender.Visibility = Visibility.Visible;
                TxtNovidadeTituloBarTender.Text = $"🎉 Nova Versão {_versaoDisponivelBarTender.Versao} do BarTender Disponível!";
                TxtNovidadeNotasBarTender.Text = string.IsNullOrWhiteSpace(_versaoDisponivelBarTender.Notas)
                    ? "Melhorias nos modelos de etiquetas, código de barras e desempenho de impressão."
                    : _versaoDisponivelBarTender.Notas;
                TxtStatusGeral.Text = $"Nova versão {_versaoDisponivelBarTender.Versao} do BarTender encontrada no GitHub!";
                BadgeMenuUpdate.Visibility = Visibility.Visible;
                TxtBadgeBarTender.Text = $"⚠️ Nova v{_versaoDisponivelBarTender.Versao}!";
                TxtBadgeBarTender.Foreground = (SolidColorBrush)FindResource("CorAviso");
            }
            else if (res.Estado == EstadoVerificacao.SemNovidade)
            {
                BrdNovidadeBarTender.Visibility = Visibility.Collapsed;
                TxtStatusModuloBarTender.Text = $"v{res.Atual} (Sincronizado Git)";
                TxtVersaoBarTenderInstalada.Text = $"Hash SHA-256 verificado com o Git ({res.Atual})";
                TxtBadgeBarTender.Text = $"● Atualizado v{res.Atual} (Git)";
                TxtBadgeBarTender.Foreground = (SolidColorBrush)FindResource("CorPositivo");
                TxtVersaoDetalheBarTender.Text = $"v{res.Atual} (Sincronizado com o GitHub)";
                TxtStatusGeral.Text = "O Nexus BarTender já está na versão mais recente disponível (idêntico ao Git).";
                MessageBox.Show(this, $"O Nexus BarTender já está totalmente atualizado na versão v{res.Atual}.\nO executável instalado confere exatamente com o repositório oficial do GitHub.", "Módulo Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
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

            if (!FecharProcessoSeguro("NexusBarTender"))
            {
                AvisarQueNaoFechou("Nexus BarTender");
                return;
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

    private void BtnRestaurarBackupBarTender_Click(object sender, RoutedEventArgs e)
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
                if (!FecharProcessoSeguro("NexusBarTender"))
                {
                    AvisarQueNaoFechou("Nexus BarTender");
                    return;
                }

                var pastaExe = Path.GetDirectoryName(Path.GetFullPath(exeAlvo)) ?? AppContext.BaseDirectory;
                var intermediario = Path.Combine(pastaExe, "restaurar-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(backup, intermediario, overwrite: true);
                AtualizadorService.TrocarExecutavel(exeAlvo, intermediario);
                CarregarStatusVersaoBarTender();
                MessageBox.Show(this, "Versão anterior do BarTender restaurada com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao restaurar backup do BarTender: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnCopiarCompiladoBarTender_Click(object sender, RoutedEventArgs e)
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

                if (!FecharProcessoSeguro("NexusBarTender"))
                {
                    AvisarQueNaoFechou("Nexus BarTender");
                    return;
                }

                if (File.Exists(destino))
                {
                    var backup = Path.Combine(Path.GetDirectoryName(destino)!, "NexusBarTender.anterior.exe");
                    File.Copy(destino, backup, overwrite: true);
                }

                var pastaExe = Path.GetDirectoryName(Path.GetFullPath(destino)) ?? AppContext.BaseDirectory;
                var intermediario = Path.Combine(pastaExe, "novo-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(d.FileName, intermediario, overwrite: true);
                AtualizadorService.TrocarExecutavel(destino, intermediario);
                CarregarStatusVersaoBarTender();
                MessageBox.Show(this, "Executável do BarTender atualizado com sucesso!", "Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao copiar arquivo do BarTender: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Pede para o programa fechar (como clicar no X). Espere até ele sair e NÃO mata o processo:
    /// encerrar o Nexus à força no meio de uma gravação é justamente o que estraga o banco. Se ele não
    /// fechar (por exemplo, com o caixa aberto, que o programa bloqueia de propósito), o chamador avisa e desiste.
    /// </summary>
    private static bool FecharProcessoSeguro(string nomeDoProcesso)
    {
        var processos = Process.GetProcessesByName(nomeDoProcesso);
        if (processos.Length == 0) return true;

        foreach (var p in processos)
        {
            try { p.CloseMainWindow(); }
            catch { }
        }

        var limite = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < limite)
        {
            var vivos = 0;
            foreach (var p in processos)
            {
                try { if (!p.HasExited) vivos++; }
                catch { }
            }
            if (vivos == 0) return true;
            Thread.Sleep(500);
        }

        foreach (var p in processos)
        {
            try { if (!p.HasExited) return false; }
            catch { }
        }
        return true;
    }

    /// <summary>Avisa que o programa não fechou e que nada foi trocado.</summary>
    private void AvisarQueNaoFechou(string nomeAmigavel)
    {
        TxtStatusGeral.Text = $"Atualização cancelada: feche o {nomeAmigavel} e tente de novo.";
        MessageBox.Show(this,
            $"O {nomeAmigavel} não fechou, então nada foi trocado.\n\n" +
            "Feche o programa (confira se há uma tela com o caixa aberto pedindo para fechar) e clique em instalar de novo.\n\n" +
            "O atualizador não encerra o programa à força para não corromper o banco de dados.",
            "Nexus Atualizador", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==================== DIAGNÓSTICO E SUPORTE ====================

    private async void BtnExecutarDiagnostico_Click(object sender, RoutedEventArgs e)
    {
        BtnExecutarDiagnostico.IsEnabled = false;
        TxtStatusGeral.Text = "Executando bateria de diagnósticos reais do Nexus...";

        try
        {
            // 1. Internet
            TxtDiagInternetStatus.Text = "Testando...";
            bool internetOk = false;
            try
            {
                using var pingHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var resp = await pingHttp.GetAsync("https://www.google.com");
                internetOk = resp.IsSuccessStatusCode;
            }
            catch
            {
                internetOk = false;
            }
            SetBadgeStatus(BrdDiagInternet, TxtDiagInternetStatus, internetOk, "Conectado", "Falha de Conexão");

            // 2. Firebase
            TxtDiagFirebaseStatus.Text = "Verificando...";
            bool firebaseOk = false;
            try
            {
                var r = await _licenca.VerificarAsync();
                MostrarLicenca(r);
                firebaseOk = !r.Offline && r.Estado != EstadoLicencaNuvem.SemConexao;
            }
            catch
            {
                firebaseOk = false;
            }
            SetBadgeStatus(BrdDiagFirebase, TxtDiagFirebaseStatus, firebaseOk, "Online (nexus-ls)", "Serviço Inacessível");

            // 3. Licença
            var lic = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
            SetBadgeStatus(BrdDiagLicenca, TxtDiagLicencaStatus, lic.Ativa, "Ativa e Autorizada", "Não Ativada", "Vencendo");

            // 4. Arquivos e Dados
            var pastaDados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS");
            bool arquivosOk = false;
            try
            {
                Directory.CreateDirectory(pastaDados);
                var teste = Path.Combine(pastaDados, ".test_diag");
                File.WriteAllText(teste, "ok");
                File.Delete(teste);
                arquivosOk = true;
            }
            catch
            {
                arquivosOk = false;
            }
            SetBadgeStatus(BrdDiagArquivos, TxtDiagArquivosStatus, arquivosOk, "Íntegro e Acessível", "Erro de Permissão");

            // 5. Nexus LS
            var exeLS = TxtCaminhoExe.Text.Trim();
            bool nexusOk = File.Exists(exeLS);
            SetBadgeStatus(BrdDiagNexus, TxtDiagNexusStatus, nexusOk, "Instalado e Pronto", "Executável Ausente");

            // 6. BarTender
            var exeBT = TxtCaminhoBarTenderExe.Text.Trim();
            bool btOk = File.Exists(exeBT);
            SetBadgeStatus(BrdDiagBarTender, TxtDiagBarTenderStatus, btOk ? true : (bool?)null, "Instalado e Pronto", "Não Instalado", "Módulo Opcional");

            // 7. Sistema de Updates
            TxtDiagUpdatesStatus.Text = "Checando...";
            bool updateOk = false;
            try
            {
                var repo = ConfiguracaoAtualizacao.Repositorio();
                using var req = new HttpRequestMessage(HttpMethod.Head, $"https://api.github.com/repos/{repo}/releases/latest");
                req.Headers.UserAgent.ParseAdd("NexusLS-Diag/1.0");
                var rResp = await HttpCompartilhado.SendAsync(req);
                updateOk = rResp.IsSuccessStatusCode;
            }
            catch
            {
                updateOk = false;
            }
            SetBadgeStatus(BrdDiagUpdates, TxtDiagUpdatesStatus, updateOk, "GitHub Operacional", "Repositório Offline");

            TxtStatusGeral.Text = "Diagnóstico do Nexus concluído.";
        }
        finally
        {
            BtnExecutarDiagnostico.IsEnabled = true;
        }
    }

    private static void SetBadgeStatus(Border brd, TextBlock txt, bool? status, string okTexto, string erroTexto, string? neutroTexto = null)
    {
        if (status == true)
        {
            brd.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)); // Verde
            txt.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
            txt.Text = $"✓ {okTexto}";
        }
        else if (status == false)
        {
            brd.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)); // Vermelho
            txt.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
            txt.Text = $"✕ {erroTexto}";
        }
        else
        {
            brd.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)); // Amarelo
            txt.Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
            txt.Text = $"○ {neutroTexto ?? "Opcional"}";
        }
    }

    private void BtnCopiarDiagnostico_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
            var relatorio =
                $"=== RELATÓRIO TÉCNICO NEXUS HUB ===\r\n" +
                $"Data e Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n" +
                $"Computador: {Environment.MachineName}\r\n" +
                $"HWID: {LicencaService.CodigoDaMaquina()}\r\n" +
                $"Sistema Operacional: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\r\n" +
                $"Ambiente .NET: {RuntimeInformation.FrameworkDescription}\r\n" +
                $"Status da Licença: {TxtLicencaStatus.Text}\r\n" +
                $"Cliente: {TxtLicencaCliente.Text} | Empresa: {TxtLicencaEmpresa.Text}\r\n" +
                $"Plano: {TxtLicencaPlano.Text} | Validade: {TxtLicencaValidade.Text}\r\n" +
                $"Executável Nexus LS: {TxtCaminhoExe.Text} ({TxtVersaoStatus.Text})\r\n" +
                $"Executável BarTender: {TxtCaminhoBarTenderExe.Text} ({TxtStatusModuloBarTender.Text})";

            Clipboard.SetText(relatorio);
            TxtStatusGeral.Text = "Relatório de diagnóstico copiado para a área de transferência!";
            MessageBox.Show(this, "O relatório de diagnóstico foi copiado para a área de transferência!\nBasta colar (Ctrl+V) no atendimento ao cliente.", "Relatório Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível copiar o relatório: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    // ==================== ATALHO DO WINDOWS ====================

    private void BtnCriarAtalhoDesktop_Click(object sender, RoutedEventArgs e)
    {
        CriarAtalhoDesktop(silencioso: false);
    }

    public static void CriarAtalhoDesktop(bool silencioso)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var caminhoAtalho = Path.Combine(desktop, "Nexus Hub — Central de Aplicativos.lnk");
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
                    shortcut.Description = "Nexus Hub — Central de Aplicativos Nexus";
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
