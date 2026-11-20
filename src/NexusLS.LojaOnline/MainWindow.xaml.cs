using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Web.WebView2.Core;
using NexusLS.App.Web;
using NexusLS.Core.Data;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Vitrine;

namespace NexusLS.LojaOnline;

/// <summary>
/// App separado do PDV: abre o editor do catálogo e do site da loja. Lê os produtos do mesmo banco do Nexus LS e publica
/// no Firebase do catálogo (só no clique). A página só enxerga endereços virtuais de pastas deste computador.
/// </summary>
public partial class MainWindow : Window
{
    private const string HostPagina = "catalogo.nexus.local";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private PonteCatalogo? _ponte;

    public MainWindow() => InitializeComponent();

    private async void AoCarregar(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(CaminhoBanco.Padrao()))
            {
                MostrarAviso("Não encontrei o banco do Nexus LS neste computador. Abra o Nexus LS uma vez e cadastre os produtos; depois abra este programa de novo.");
                return;
            }

            _ponte = new PonteCatalogo(CriarServico());

            var pasta = PacoteWeb.Preparar();
            var dados = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "webview2-loja");
            var ambiente = await CoreWebView2Environment.CreateAsync(userDataFolder: dados);
            await Navegador.EnsureCoreWebView2Async(ambiente);

            var core = Navegador.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(HostPagina, pasta, CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(FotoProdutoService.PastaPadrao());
            core.SetVirtualHostNameToFolderMapping(ServicoCatalogoWeb.HostFotos, FotoProdutoService.PastaPadrao(), CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(ServicoCatalogoWeb.PastaImagensPadrao());
            core.SetVirtualHostNameToFolderMapping(ServicoCatalogoWeb.HostImagensVitrine, ServicoCatalogoWeb.PastaImagensPadrao(), CoreWebView2HostResourceAccessKind.Allow);

            // A página não sai dos endereços do programa e não abre janelas novas.
            core.NavigationStarting += (_, a) =>
            {
                if (!Uri.TryCreate(a.Uri, UriKind.Absolute, out var u) || !u.Host.Equals(HostPagina, StringComparison.OrdinalIgnoreCase))
                    a.Cancel = true;
            };
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.WebMessageReceived += AoReceberMensagem;

            Aviso.Visibility = Visibility.Collapsed;
            Navegador.Visibility = Visibility.Visible;
            core.Navigate($"https://{HostPagina}/index.html");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException or WebView2RuntimeNotFoundException)
        {
            MostrarAviso(ex is WebView2RuntimeNotFoundException
                ? "Falta o Microsoft Edge WebView2 Runtime (já vem no Windows 11 e no Windows 10 atualizado). Instale em microsoft.com/edge/webview2 e abra de novo."
                : "Não foi possível abrir o catálogo: " + ex.Message);
        }
    }

    private ServicoCatalogoWeb CriarServico()
    {
        var opcoes = new DbContextOptionsBuilder<PdvDbContext>().UseSqlite(CaminhoBanco.StringConexao()).Options;
        var fabrica = new FabricaContexto(opcoes);
        var firebase = new ConfiguracaoFirebaseStore(ConfiguracaoFirebaseStore.CaminhoPadrao(), new ProtetorDpapi());
        return new ServicoCatalogoWeb(
            new ProdutoService(fabrica),
            new ConfiguracaoService(fabrica),
            new VitrineCompletaStore(VitrineCompletaStore.CaminhoPadrao()),
            ServicoCatalogoWeb.PastaImagensPadrao(),
            new EnviadorFotoConfiguravel(firebase, _http),
            new PublicadorVitrineConfiguravel(firebase, _http));
    }

    private async void AoReceberMensagem(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_ponte is null) return;
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var origem) || !origem.Host.Equals(HostPagina, StringComparison.OrdinalIgnoreCase)) return;
        var resposta = await _ponte.ResponderAsync(e.WebMessageAsJson);
        Navegador.CoreWebView2?.PostWebMessageAsJson(resposta);
    }

    private void MostrarAviso(string texto)
    {
        Aviso.Text = texto;
        Aviso.Visibility = Visibility.Visible;
    }
}
