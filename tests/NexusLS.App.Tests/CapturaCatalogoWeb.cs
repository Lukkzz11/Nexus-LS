using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Services;
using NexusLS.Core.Vitrine;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Abre de verdade o editor do catálogo (WebView2 + pacote embutido + ponte) e confere que a página carregou os
/// dados do estoque. Precisa de janela e do WebView2 Runtime: só roda com NEXUSLS_WEBVIEW apontando para uma pasta
/// (onde salva uma captura); nos testes normais não faz nada.
/// </summary>
public sealed class CapturaCatalogoWeb : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-web-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Abre_o_editor_no_webview_e_carrega_os_produtos()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_WEBVIEW");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        await produtos.CadastrarAsync(new DadosProduto("Camiseta Onda", "7891", 5990, 2000, 5), 8);
        var servico = new ServicoCatalogoWeb(produtos, new ConfiguracaoService(_banco.Fabrica),
            new VitrineCompletaStore(Path.Combine(_pasta, "v.json")), Path.Combine(_pasta, "img"), null, null);

        string? texto = null, erro = null;
        var terminou = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new NexusLS.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            var view = new CatalogoWebView { DataContext = new CatalogoWebViewModel(servico) };
            var janela = new Window { Width = 1300, Height = 850, Content = view };
            janela.Show();
            var relogio = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            var inicio = DateTime.UtcNow;
            relogio.Tick += async (_, _) =>
            {
                var web = (WebView2)view.FindName("Navegador");
                if (web.CoreWebView2 is null)
                {
                    if (DateTime.UtcNow - inicio > TimeSpan.FromSeconds(60))
                    {
                        erro = "WebView2 não iniciou: " + ((System.Windows.Controls.TextBlock)view.FindName("Aviso")).Text;
                        relogio.Stop(); terminou.Set();
                    }
                    return;
                }
                var bruto = await web.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
                if (bruto.Contains("Modelos") || DateTime.UtcNow - inicio > TimeSpan.FromSeconds(60))
                {
                    relogio.Stop();
                    texto = bruto;
                    await Task.Delay(2500); // deixa a prévia (iframe) desenhar
                    using var arquivo = File.Create(Path.Combine(saida, "editor-catalogo-webview.png"));
                    await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, arquivo);
                    janela.Close();
                    terminou.Set();
                }
            };
            relogio.Start();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(terminou.Wait(TimeSpan.FromSeconds(150)), "A janela não terminou a tempo.");

        Assert.Null(erro);
        Assert.Contains("Modelos", texto);
        Assert.Contains("Salvar e publicar", texto);
    }
}
