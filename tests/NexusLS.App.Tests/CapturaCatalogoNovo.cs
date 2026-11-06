using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Vitrine;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Capturas do editor do catálogo no formato da tela do ERP (aba "Mercadorias e dados da loja" e aba "Editor visual"),
/// em 1900x1000 e 1280x760. Só roda com NEXUSLS_WEBVIEW apontando para a pasta de saída; nos testes normais não faz nada.
/// Rode UM teste de captura por vez (--filter).
/// </summary>
public sealed class CapturaCatalogoNovo : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-webnovo-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private async Task<ServicoCatalogoWeb> PrepararAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var itens = new (string Nome, string Cat, long Preco, long Atacado, string Ref)[]
        {
            ("Camiseta Onda Classic", "Camiseta", 7990, 4800, "CAM-001"),
            ("Camiseta Maré Alta", "Camiseta", 8990, 5400, "CAM-002"),
            ("Bermuda Tactel Surf", "Bermuda", 12990, 7800, "BER-001"),
            ("Boné Aba Curva", "Boné", 5990, 3500, "BON-001"),
            ("Regata Sol Nascente", "Regata", 5490, 3200, "REG-001"),
            ("Moletom Brisa", "Moletom", 18990, 11000, "MOL-001"),
            ("Chinelo Praia", "Calçado", 3990, 2400, "CHI-001"),
            ("Jaqueta Corta-Vento", "Jaqueta", 22990, 14000, "JAQ-001")
        };
        var criados = new List<Produto>();
        foreach (var (nome, cat, preco, atacado, referencia) in itens)
            criados.Add(await produtos.CadastrarAsync(new DadosProduto(nome, null, preco, preco / 2, 3, referencia, atacado, cat), 4));
        await using (var db = _banco.CriarContexto())
        {
            foreach (var p in criados.Take(5))
                foreach (var (tam, qtd) in new[] { ("P", 3), ("M", 6), ("G", 4) })
                    db.TamanhosProduto.Add(new ProdutoTamanho { ProdutoId = p.Id, Tamanho = tam, Estoque = qtd });
            await db.SaveChangesAsync();
        }
        var servico = new ServicoCatalogoWeb(produtos, new ConfiguracaoService(_banco.Fabrica),
            new VitrineCompletaStore(Path.Combine(_pasta, "v.json")), Path.Combine(_pasta, "img"), null, null,
            ajustes: new AjustesProdutoStore(Path.Combine(_pasta, "ajustes.json")));
        await servico.SalvarAjusteProdutoAsync(criados[0].Guid.ToString("D"), true, true, 0);
        await servico.SalvarAjusteProdutoAsync(criados[2].Guid.ToString("D"), true, false, 9990);
        await servico.SalvarAjusteProdutoAsync(criados[4].Guid.ToString("D"), false, false, 0);
        await servico.SalvarAjusteProdutoAsync(criados[5].Guid.ToString("D"), true, true, 15990);
        return servico;
    }

    [Fact]
    public async Task Captura_o_editor_nas_duas_abas_e_nos_dois_tamanhos()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_WEBVIEW");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);
        var servico = await PrepararAsync();

        string? erro = null;
        var terminou = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new NexusLS.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) => e.Handled = true; // MainWindow do StartupUri não existe no host de teste
            app.InitializeComponent();
            var view = new CatalogoWebView { DataContext = new CatalogoWebViewModel(servico) };
            var janela = new Window { Left = 0, Top = 0, Width = 1900, Height = 1000, Content = view };
            janela.Show();
            _ = janela.Dispatcher.InvokeAsync(() => Rodar(janela, view, saida)).Task.Unwrap().ContinueWith(t =>
            {
                erro = t.Exception?.GetBaseException().Message;
                janela.Dispatcher.Invoke(janela.Close);
                terminou.Set();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(terminou.Wait(TimeSpan.FromSeconds(240)), "A janela não terminou a tempo.");
        Assert.Null(erro);
    }

    private static async Task<string> Js(WebView2 web, string codigo) => await web.CoreWebView2.ExecuteScriptAsync(codigo);

    private static async Task Rodar(Window janela, CatalogoWebView view, string saida)
    {
        var web = (WebView2)view.FindName("Navegador");
        var inicio = DateTime.UtcNow;
        while (web.CoreWebView2 is null || !(await Js(web, "document.body ? document.body.innerText : ''")).Contains("Modelos"))
        {
            if (DateTime.UtcNow - inicio > TimeSpan.FromSeconds(90)) throw new TimeoutException("O editor não carregou.");
            await Task.Delay(500);
        }
        async Task Foto(string nome)
        {
            await Task.Delay(2500); // deixa a prévia (iframe) e as imagens desenharem
            using var arquivo = File.Create(Path.Combine(saida, nome));
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, arquivo);
        }
        const string Clicar = "(t)=>{const b=[...document.querySelectorAll('button')].find(x=>x.innerText.trim().startsWith(t)); if(b){b.click(); return true;} return false;}";

        foreach (var (largura, altura) in new[] { (1900, 1000), (1280, 760) })
        {
            janela.Width = largura; janela.Height = altura;
            await Task.Delay(1200);
            var rotulo = $"{largura}x{altura}";
            await Js(web, $"({Clicar})('Editor visual')");
            await Foto($"editor-{rotulo}.png");
            await Js(web, $"({Clicar})('Loja e rodapé')");
            await Foto($"editor-loja-{rotulo}.png");
            await Js(web, $"({Clicar})('Mercadorias e dados')");
            await Foto($"mercadorias-{rotulo}.png");
            await Js(web, "window.scrollTo(0, 99999)");
            await Foto($"mercadorias-tabela-{rotulo}.png");
            await Js(web, "window.scrollTo(0, 0)");
            await Js(web, "document.querySelector('[aria-label^=\"Editar \"]')?.click()");
            await Foto($"mercadorias-modal-{rotulo}.png");
            await Js(web, "document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'})); window.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'}))");
            await Task.Delay(300);
        }
    }
}
