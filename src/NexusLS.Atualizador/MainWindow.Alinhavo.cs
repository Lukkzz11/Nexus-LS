using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NexusLS.Atualizador;

/// <summary>
/// Alinhavo (Moldes e costura 3D): terceiro aplicativo do Hub. É um programa em Rust que vem dentro do
/// instalador do Hub, na pasta "Alinhavo" ao lado do atualizador (alinhavo.exe + assets\manequim\*.glb).
/// Não tem atualização pela nuvem: uma versão nova chega num instalador novo do Hub.
/// </summary>
public partial class MainWindow
{
    public const string ExeAlinhavo = "alinhavo.exe";
    public const string ArquivoVersaoAlinhavo = "versao.txt";

    /// <summary>Caminho do alinhavo.exe encontrado (ou o primeiro candidato, mesmo que ainda não exista).</summary>
    private string _exeAlinhavo = "";

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

    private static string LocalizarAlinhavoPadrao()
    {
        var candidatos = CandidatosAlinhavo(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        return candidatos.FirstOrDefault(File.Exists) ?? candidatos[0];
    }

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

            if (File.Exists(_exeAlinhavo))
            {
                var versao = LerVersaoAlinhavo(_exeAlinhavo);
                var fi = new FileInfo(_exeAlinhavo);
                var mb = fi.Length / (1024.0 * 1024.0);
                var manequim = ManequimAlinhavoPresente(_exeAlinhavo);

                TxtStatusModuloAlinhavo.Text = $"Módulo instalado: v{versao} ({mb:F0} MB)";
                TxtVersaoAlinhavoInstalada.Text = $"v{versao} ({mb:F0} MB) — Modificado em: {fi.LastWriteTime:dd/MM/yyyy HH:mm}" +
                    (manequim ? "" : " — manequim 3D não encontrado");
                TxtVersaoDetalheAlinhavo.Text = $"v{versao} ({mb:F0} MB)";
                TxtSobreVersaoAlinhavo.Text = $"v{versao}";
                TxtProntoAlinhavo.Text = manequim ? "Pronto para uso" : "Sem manequim 3D";

                TxtBadgeAlinhavo.Text = "● Instalado";
                TxtBadgeAlinhavo.Foreground = (SolidColorBrush)FindResource("CorPositivo");
            }
            else
            {
                TxtStatusModuloAlinhavo.Text = "Alinhavo não encontrado neste computador.";
                TxtVersaoAlinhavoInstalada.Text = "Reinstale pelo instalador do Nexus Hub para recuperar o Alinhavo.";
                TxtVersaoDetalheAlinhavo.Text = "Não instalado";
                TxtSobreVersaoAlinhavo.Text = "Não instalado";
                TxtProntoAlinhavo.Text = "Opcional";

                TxtBadgeAlinhavo.Text = "○ Opcional";
                TxtBadgeAlinhavo.Foreground = (SolidColorBrush)FindResource("CorTextoSuave");
            }
        }
        catch
        {
            TxtStatusModuloAlinhavo.Text = "Alinhavo opcional.";
            TxtVersaoDetalheAlinhavo.Text = "Opcional";
            TxtBadgeAlinhavo.Text = "○ Opcional";
        }
    }

    private void CardAppAlinhavo_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            BtnIniciarAlinhavo_Click(sender, e);
        }
    }

    private void BtnIniciarAlinhavo_Click(object sender, RoutedEventArgs e)
    {
        var avaliacao = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
        if (!avaliacao.Ativa)
        {
            JanelaDialogo.Mostrar(
                "Licença Necessária",
                $"Não é possível iniciar o Alinhavo porque a licença não está ativa neste computador.\n\nSituação atual: {TxtLicencaStatus.Text}\nDetalhe: {avaliacao.Mensagem}\n\nAtive ou valide sua chave no menu 'Licença'.",
                TipoDialogo.Aviso,
                this);
            NavegarPara("licenca");
            return;
        }

        var exe = File.Exists(_exeAlinhavo) ? _exeAlinhavo : LocalizarAlinhavoPadrao();
        if (!File.Exists(exe))
        {
            JanelaDialogo.Mostrar("Módulo Não Encontrado", "O Alinhavo (alinhavo.exe) não foi localizado neste computador.\nReinstale pelo instalador do Nexus Hub marcando o Alinhavo.", TipoDialogo.Aviso, this);
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

    private void BtnAbrirPastaAlinhavo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pasta = File.Exists(_exeAlinhavo)
                ? Path.GetDirectoryName(_exeAlinhavo)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexus", "Alinhavo");
            if (Directory.Exists(pasta))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", pasta) { UseShellExecute = true });
            }
            else
            {
                JanelaDialogo.Mostrar("Diretório", $"A pasta de instalação ainda não foi criada:\n{pasta}", TipoDialogo.Aviso, this);
            }
        }
        catch (Exception ex)
        {
            JanelaDialogo.Mostrar("Erro", $"Não foi possível abrir o diretório: {ex.Message}", TipoDialogo.Erro, this);
        }
    }
}
