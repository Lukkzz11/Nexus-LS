using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NexusLS.Core.Atualizacao;

namespace NexusLS.Atualizador;

/// <summary>
/// Alinhavo (Moldes e costura 3D): terceiro aplicativo do Hub. É um programa em Rust (alinhavo.exe + assets\manequim).
/// Pode vir dentro do instalador do Hub, ou ser instalado/atualizado direto da nuvem por este card
/// (Alinhavo.zip + Alinhavo.zip.sha256 + alinhavo-versao.txt na release do GitHub, mesmo mecanismo do Nexus LS e BarTender).
/// </summary>
public partial class MainWindow
{
    public const string ExeAlinhavo = "alinhavo.exe";
    public const string ArquivoVersaoAlinhavo = "versao.txt";

    /// <summary>Caminho do alinhavo.exe encontrado (ou o primeiro candidato, mesmo que ainda não exista).</summary>
    private string _exeAlinhavo = "";

    private enum EstadoAlinhavo { Instalar, Abrir, Atualizar }
    private EstadoAlinhavo _estadoAlinhavo = EstadoAlinhavo.Abrir;
    private InfoAlinhavo? _infoAlinhavo;
    private bool _ocupadoAlinhavo;
    private FonteAlinhavo? _fonteAlinhavo;

    private FonteAlinhavo FonteDoAlinhavo => _fonteAlinhavo ??= new FonteAlinhavo(HttpCompartilhado, ConfiguracaoAtualizacao.Repositorio());

    /// <summary>
    /// Só para testes de tela (build Debug): a variável de ambiente NEXUSHUB_TESTE_PASTA_ALINHAVO aponta o Hub para
    /// outra pasta (por exemplo, vazia, para simular "não instalado"). Não existe no build Release.
    /// </summary>
    private static string? PastaTesteAlinhavo()
    {
#if DEBUG
        var p = Environment.GetEnvironmentVariable("NEXUSHUB_TESTE_PASTA_ALINHAVO");
        return string.IsNullOrWhiteSpace(p) ? null : p;
#else
        return null;
#endif
    }

    /// <summary>Lugares onde o Alinhavo pode estar, do instalado ao ambiente de desenvolvimento.</summary>
    public static IReadOnlyList<string> CandidatosAlinhavo(string pastaDoHub, string pastaLocalAppData) =>
    [
        Path.Combine(pastaDoHub, "Alinhavo", ExeAlinhavo),
        Path.Combine(pastaDoHub, "..", "Alinhavo", ExeAlinhavo),
        Path.Combine(pastaLocalAppData, "Programs", "Nexus", "Alinhavo", ExeAlinhavo),
        Path.Combine(pastaLocalAppData, "Programs", "NexusLS", "Alinhavo", ExeAlinhavo),
        @"C:\NexusLS\publicar\Alinhavo\" + ExeAlinhavo,
        @"C:\Alinhavo\target\release\" + ExeAlinhavo
    ];

    /// <summary>Quantos dos primeiros candidatos são pastas "instaladas" (as que o Hub pode instalar/atualizar).</summary>
    private const int CandidatosInstalados = 4;

    private static string LocalizarAlinhavoPadrao()
    {
        if (PastaTesteAlinhavo() is { } teste) return Path.Combine(teste, ExeAlinhavo);
        var candidatos = CandidatosAlinhavo(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        return candidatos.FirstOrDefault(File.Exists) ?? candidatos[0];
    }

    /// <summary>Pasta onde o Hub instala/atualiza o Alinhavo: a instalação existente, ou %LOCALAPPDATA%\Programs\Nexus\Alinhavo.</summary>
    private static string PastaInstalacaoAlinhavo()
    {
        if (PastaTesteAlinhavo() is { } teste) return Path.GetFullPath(teste);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var existente = CandidatosAlinhavo(AppContext.BaseDirectory, local).Take(CandidatosInstalados).FirstOrDefault(File.Exists);
        return Path.GetFullPath(existente is null ? Path.Combine(local, "Programs", "Nexus", "Alinhavo") : Path.GetDirectoryName(existente)!);
    }

    private static bool MesmaPasta(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Versão do pacote: versao.txt ao lado do exe (gerado por publicar-alinhavo.ps1); senão a do arquivo.</summary>
    public static string LerVersaoAlinhavo(string exe)
    {
        try
        {
            var arquivo = Path.Combine(Path.GetDirectoryName(exe) ?? "", ArquivoVersaoAlinhavo);
            if (File.Exists(arquivo))
            {
                var texto = File.ReadAllText(arquivo).Trim();
                if (texto.Length is > 0 and <= 32) return texto.TrimStart('v', 'V');
            }

            var vi = FileVersionInfo.GetVersionInfo(exe);
            var v = (vi.ProductVersion ?? vi.FileVersion ?? "").Split('+')[0].Trim();
            if (v.Length > 0 && v != "0.0.0.0") return v;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "0.1.0";
    }

    /// <summary>O manequim 3D precisa estar na pasta assets\manequim ao lado do exe.</summary>
    public static bool ManequimAlinhavoPresente(string exe)
    {
        var dir = Path.GetDirectoryName(exe) ?? "";
        // Instalado: assets ao lado do exe. Desenvolvimento: C:\Alinhavo\target\release\alinhavo.exe usa C:\Alinhavo\assets.
        foreach (var pasta in new[] { Path.Combine(dir, "assets", "manequim"), Path.Combine(dir, "..", "..", "assets", "manequim") })
        {
            if (Directory.Exists(pasta) && Directory.EnumerateFiles(pasta, "*.glb").Any()) return true;
        }
        return false;
    }

    private void CarregarStatusVersaoAlinhavo()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_exeAlinhavo) || !File.Exists(_exeAlinhavo))
                _exeAlinhavo = LocalizarAlinhavoPadrao();

            if (!File.Exists(_exeAlinhavo))
            {
                _estadoAlinhavo = EstadoAlinhavo.Instalar;
                TxtStatusModuloAlinhavo.Text = "Alinhavo não instalado neste computador.";
                TxtVersaoAlinhavoInstalada.Text = "Clique em INSTALAR ALINHAVO na tela inicial para baixar da nuvem.";
                TxtVersaoDetalheAlinhavo.Text = _infoAlinhavo is null ? "Não instalado" : $"Não instalado • disponível v{_infoAlinhavo.Versao}";
                TxtSobreVersaoAlinhavo.Text = "Não instalado";
                TxtProntoAlinhavo.Text = "Baixar da nuvem";
                TxtProntoAlinhavo.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
                TxtBadgeAlinhavo.Text = "○ Não instalado";
                TxtBadgeAlinhavo.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
                AplicarBotaoAlinhavo();
                return;
            }

            var versao = LerVersaoAlinhavo(_exeAlinhavo);
            var fi = new FileInfo(_exeAlinhavo);
            var mb = fi.Length / (1024.0 * 1024.0);
            var manequim = ManequimAlinhavoPresente(_exeAlinhavo);

            // Só atualiza pelo Hub o que o Hub instala (nunca o exe de desenvolvimento em C:\Alinhavo).
            var gerenciado = MesmaPasta(Path.GetDirectoryName(_exeAlinhavo)!, PastaInstalacaoAlinhavo());
            var instalada = InstalacaoAlinhavo.VersaoInstalada(Path.GetDirectoryName(_exeAlinhavo)!);
            var haNova = gerenciado && _infoAlinhavo is { } nuvem && InstalacaoAlinhavo.HaVersaoNova(instalada ?? default(VersaoPrograma), nuvem.Versao);
            _estadoAlinhavo = haNova ? EstadoAlinhavo.Atualizar : EstadoAlinhavo.Abrir;

            TxtStatusModuloAlinhavo.Text = $"Módulo instalado: v{versao} ({mb:F0} MB)";
            TxtVersaoAlinhavoInstalada.Text = $"v{versao} ({mb:F0} MB) — Modificado em: {fi.LastWriteTime:dd/MM/yyyy HH:mm}" +
                (manequim ? "" : " — manequim 3D não encontrado");
            TxtSobreVersaoAlinhavo.Text = $"v{versao}";

            if (haNova)
            {
                TxtVersaoDetalheAlinhavo.Text = $"Instalada v{versao} • Nova v{_infoAlinhavo!.Versao}";
                TxtProntoAlinhavo.Text = "Atualização";
                TxtProntoAlinhavo.Foreground = (SolidColorBrush)FindResource("CorAviso");
                TxtBadgeAlinhavo.Text = $"⚠ Nova v{_infoAlinhavo.Versao}";
                TxtBadgeAlinhavo.Foreground = (SolidColorBrush)FindResource("CorAviso");
            }
            else
            {
                TxtVersaoDetalheAlinhavo.Text = $"v{versao} ({mb:F0} MB)";
                TxtProntoAlinhavo.Text = manequim ? "Pronto para uso" : "Sem manequim 3D";
                TxtProntoAlinhavo.Foreground = (SolidColorBrush)FindResource("CorPositivo");
                TxtBadgeAlinhavo.Text = "● Instalado";
                TxtBadgeAlinhavo.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            }
            AplicarBotaoAlinhavo();
        }
        catch
        {
            TxtStatusModuloAlinhavo.Text = "Alinhavo opcional.";
            TxtVersaoDetalheAlinhavo.Text = "Opcional";
            TxtBadgeAlinhavo.Text = "○ Opcional";
        }
    }

    /// <summary>Texto e ícone do botão do card conforme o estado (instalar, abrir ou atualizar).</summary>
    private void AplicarBotaoAlinhavo()
    {
        (string texto, string icone) = _estadoAlinhavo switch
        {
            EstadoAlinhavo.Instalar => ("INSTALAR ALINHAVO", "IconeDownload"),
            EstadoAlinhavo.Atualizar => ("ATUALIZAR ALINHAVO", "IconeAtualizar"),
            _ => ("ABRIR ALINHAVO", "IconePlay")
        };
        TxtBtnAlinhavo.Text = texto;
        IcoBtnAlinhavo.Data = (Geometry)FindResource(icone);
        BtnIniciarAlinhavo.IsEnabled = !_ocupadoAlinhavo;
    }

    /// <summary>Consulta a nuvem (sem travar a janela) e atualiza o card. Em modo silencioso, falhas são ignoradas.</summary>
    private async Task VerificarAlinhavoNuvemAsync(bool silencioso)
    {
        try
        {
            var info = await FonteDoAlinhavo.UltimaAsync();
            _infoAlinhavo = info;
            CarregarStatusVersaoAlinhavo();
            if (!silencioso)
            {
                if (info is null)
                    JanelaDialogo.Mostrar("Alinhavo", "O Alinhavo ainda não foi publicado na nuvem.", TipoDialogo.Informacao, this);
                else if (_estadoAlinhavo == EstadoAlinhavo.Abrir)
                    JanelaDialogo.Mostrar("Alinhavo", $"Seu Alinhavo está na versão mais recente (v{info.Versao}).", TipoDialogo.Sucesso, this);
                else
                    TxtStatusGeral.Text = $"Alinhavo v{info.Versao} disponível na nuvem.";
            }
        }
        catch (Exception ex)
        {
            if (!silencioso) JanelaDialogo.Mostrar("Alinhavo", InstalacaoAlinhavo.Traduzir(ex), TipoDialogo.Aviso, this);
        }
    }

    private async void BtnVerificarAlinhavo_Click(object sender, RoutedEventArgs e)
    {
        if (_ocupadoAlinhavo) return;
        TxtStatusGeral.Text = "Consultando atualizações online para o Alinhavo...";
        await VerificarAlinhavoNuvemAsync(silencioso: false);
    }

    private void CardAppAlinhavo_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !_ocupadoAlinhavo)
        {
            BtnIniciarAlinhavo_Click(sender, e);
        }
    }

    private async void BtnIniciarAlinhavo_Click(object sender, RoutedEventArgs e)
    {
        if (_ocupadoAlinhavo) return;

        var avaliacao = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
        if (!avaliacao.Ativa)
        {
            JanelaDialogo.Mostrar(
                "Licença Necessária",
                $"Não é possível usar o Alinhavo porque a licença não está ativa neste computador.\n\nSituação atual: {TxtLicencaStatus.Text}\nDetalhe: {avaliacao.Mensagem}\n\nAtive ou valide sua chave no menu 'Licença'.",
                TipoDialogo.Aviso,
                this);
            NavegarPara("licenca");
            return;
        }

        if (_estadoAlinhavo != EstadoAlinhavo.Abrir)
        {
            await InstalarOuAtualizarAlinhavoAsync();
            return;
        }

        var exe = File.Exists(_exeAlinhavo) ? _exeAlinhavo : LocalizarAlinhavoPadrao();
        if (!File.Exists(exe))
        {
            CarregarStatusVersaoAlinhavo();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });
            TxtStatusGeral.Text = "Alinhavo iniciado com sucesso.";
        }
        catch (Exception ex)
        {
            JanelaDialogo.Mostrar("Erro de Inicialização", "Erro ao iniciar o Alinhavo: " + ex.Message, TipoDialogo.Erro, this);
        }
    }

    /// <summary>Baixa da nuvem (com progresso), confere o SHA-256, fecha o Alinhavo, troca a pasta e cria os atalhos.</summary>
    private async Task InstalarOuAtualizarAlinhavoAsync()
    {
        var atualizando = _estadoAlinhavo == EstadoAlinhavo.Atualizar;
        var temp = Path.Combine(Path.GetTempPath(), "NexusHub-Alinhavo-" + Guid.NewGuid().ToString("N"));
        _ocupadoAlinhavo = true;
        BtnIniciarAlinhavo.IsEnabled = false;
        PrgAlinhavo.Visibility = Visibility.Visible;
        PrgAlinhavo.IsIndeterminate = true;
        TxtBtnAlinhavo.Text = "CONSULTANDO...";
        try
        {
            if (_infoAlinhavo is null) _infoAlinhavo = await FonteDoAlinhavo.UltimaAsync();
            if (_infoAlinhavo is null)
            {
                JanelaDialogo.Mostrar("Alinhavo", "O Alinhavo ainda não foi publicado na nuvem. Tente de novo mais tarde.", TipoDialogo.Aviso, this);
                return;
            }

            var info = _infoAlinhavo;
            TxtStatusGeral.Text = $"Baixando o Alinhavo v{info.Versao}...";
            var progresso = new Progress<double>(p =>
            {
                PrgAlinhavo.IsIndeterminate = false;
                PrgAlinhavo.Value = p * 100;
                TxtBtnAlinhavo.Text = $"BAIXANDO... {p:P0}";
                TxtStatusGeral.Text = $"Baixando o Alinhavo v{info.Versao}... {p:P0}";
            });
            var zip = await InstalacaoAlinhavo.BaixarAsync(FonteDoAlinhavo.FonteDoZip, info.Zip, temp, progresso);

            TxtBtnAlinhavo.Text = "INSTALANDO...";
            PrgAlinhavo.IsIndeterminate = true;
            if (!FecharProcessoSeguro("alinhavo"))
            {
                AvisarQueNaoFechou("Alinhavo");
                return;
            }

            var destino = PastaInstalacaoAlinhavo();
            await Task.Run(() => InstalacaoAlinhavo.Instalar(zip, destino));
            _exeAlinhavo = Path.Combine(destino, ExeAlinhavo);
            CriarAtalhosAlinhavo(_exeAlinhavo);

            TxtStatusGeral.Text = $"Alinhavo v{info.Versao} {(atualizando ? "atualizado" : "instalado")} com sucesso.";
            JanelaDialogo.Mostrar(
                atualizando ? "Atualização Concluída" : "Instalação Concluída",
                $"Alinhavo v{info.Versao} {(atualizando ? "atualizado" : "instalado")} com sucesso!\nO atalho \"Alinhavo\" está na Área de Trabalho e no Menu Iniciar.",
                TipoDialogo.Sucesso,
                this);
        }
        catch (Exception ex)
        {
            TxtStatusGeral.Text = "Alinhavo: " + InstalacaoAlinhavo.Traduzir(ex);
            JanelaDialogo.Mostrar(atualizando ? "Falha na Atualização" : "Falha na Instalação", InstalacaoAlinhavo.Traduzir(ex), TipoDialogo.Erro, this);
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _ocupadoAlinhavo = false;
            PrgAlinhavo.Visibility = Visibility.Collapsed;
            CarregarStatusVersaoAlinhavo();
        }
    }

    /// <summary>Atalho "Alinhavo" na Área de Trabalho e no Menu Iniciar (pasta Nexus). Falha de atalho nunca cancela a instalação.</summary>
    private static void CriarAtalhosAlinhavo(string exe)
    {
        try
        {
            var tipo = Type.GetTypeFromProgID("WScript.Shell");
            if (tipo is null) return;
            dynamic shell = Activator.CreateInstance(tipo)!;
            var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nexus");
            Directory.CreateDirectory(menu);
            foreach (var pasta in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), menu })
            {
                dynamic s = shell.CreateShortcut(Path.Combine(pasta, "Alinhavo.lnk"));
                s.TargetPath = exe;
                s.WorkingDirectory = Path.GetDirectoryName(exe);
                s.IconLocation = exe + ",0";
                s.Save();
            }
        }
        catch (Exception) { }
    }

    private void BtnAbrirPastaAlinhavo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pasta = File.Exists(_exeAlinhavo) ? Path.GetDirectoryName(_exeAlinhavo) : PastaInstalacaoAlinhavo();
            if (Directory.Exists(pasta))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", pasta) { UseShellExecute = true });
            }
            else
            {
                JanelaDialogo.Mostrar("Diretório", $"O Alinhavo ainda não está instalado (a pasta {pasta} ainda não existe).", TipoDialogo.Aviso, this);
            }
        }
        catch (Exception ex)
        {
            JanelaDialogo.Mostrar("Erro", $"Não foi possível abrir o diretório: {ex.Message}", TipoDialogo.Erro, this);
        }
    }
}
