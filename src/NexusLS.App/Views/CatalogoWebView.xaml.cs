using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using NexusLS.App.ViewModels;
using NexusLS.App.Web;

namespace NexusLS.App.Views;

/// <summary>
/// Hospeda o editor e o catálogo em JSX. A página só enxerga três endereços virtuais (o pacote do editor, as fotos dos
/// produtos e as imagens do editor, todos de pastas deste computador) e conversa com o programa por mensagens.
/// </summary>
public partial class CatalogoWebView : UserControl
{
    private const string HostPagina = "catalogo.nexus.local";
    private bool _iniciado;
    private PonteCatalogo? _ponte;

    public CatalogoWebView() => InitializeComponent();

    private async void AoCarregar(object sender, RoutedEventArgs e)
    {
        if (_iniciado || DataContext is not CatalogoWebViewModel vm) return;
        _iniciado = true;
        try
        {
            var pasta = PacoteWeb.Preparar();
            var dados = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "webview2");
            var ambiente = await CoreWebView2Environment.CreateAsync(userDataFolder: dados);
            await Navegador.EnsureCoreWebView2Async(ambiente);

            var core = Navegador.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(HostPagina, pasta, CoreWebView2HostResourceAccessKind.Allow);
            System.IO.Directory.CreateDirectory(NexusLS.Core.Services.FotoProdutoService.PastaPadrao());
            core.SetVirtualHostNameToFolderMapping(NexusLS.Core.Vitrine.ServicoCatalogoWeb.HostFotos,
                NexusLS.Core.Services.FotoProdutoService.PastaPadrao(), CoreWebView2HostResourceAccessKind.Allow);
            System.IO.Directory.CreateDirectory(NexusLS.Core.Vitrine.ServicoCatalogoWeb.PastaImagensPadrao());
            core.SetVirtualHostNameToFolderMapping(NexusLS.Core.Vitrine.ServicoCatalogoWeb.HostImagensVitrine,
                NexusLS.Core.Vitrine.ServicoCatalogoWeb.PastaImagensPadrao(), CoreWebView2HostResourceAccessKind.Allow);

            // A página não sai dos endereços do programa e não abre janelas novas.
            core.NavigationStarting += (_, a) =>
            {
                if (!Uri.TryCreate(a.Uri, UriKind.Absolute, out var u) || !u.Host.Equals(HostPagina, StringComparison.OrdinalIgnoreCase))
                    a.Cancel = true;
            };
            core.NewWindowRequested += (_, a) => a.Handled = true;

            _ponte = new PonteCatalogo(vm.Servico);
            core.WebMessageReceived += AoReceberMensagem;

            Aviso.Visibility = Visibility.Collapsed;
            Navegador.Visibility = Visibility.Visible;
            core.Navigate($"https://{HostPagina}/index.html");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or System.Runtime.InteropServices.COMException or WebView2RuntimeNotFoundException)
        {
            _iniciado = false;
            Aviso.Text = ex is WebView2RuntimeNotFoundException
                ? "O editor do catálogo precisa do Microsoft Edge WebView2 Runtime (já vem no Windows 11 e no Windows 10 atualizado). Instale em microsoft.com/edge/webview2 e abra a tela de novo."
                : "Não foi possível abrir o editor do catálogo: " + ex.Message;
            Aviso.Visibility = Visibility.Visible;
        }
    }

    private async void AoReceberMensagem(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_ponte is null) return;
        // Só aceita mensagens da própria página do programa.
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var origem) || !origem.Host.Equals(HostPagina, StringComparison.OrdinalIgnoreCase)) return;
        var resposta = await _ponte.ResponderAsync(e.WebMessageAsJson);
        Navegador.CoreWebView2?.PostWebMessageAsJson(resposta);
    }

    private void AoDescarregar(object sender, RoutedEventArgs e)
    {
        if (Navegador.CoreWebView2 is { } core) core.WebMessageReceived -= AoReceberMensagem;
        Navegador.Dispose();
        _iniciado = false;
    }
}
