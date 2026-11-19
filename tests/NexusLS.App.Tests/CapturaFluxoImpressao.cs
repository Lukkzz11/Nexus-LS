using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.ViewModels;
using NexusLS.BarTender.Views;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;

namespace NexusLS.App.Tests;

/// <summary>
/// Capturas do fluxo novo do BarTender (modal de quantidades e página de preparação).
/// Só roda com NEXUSLS_CAPTURAS_BARTENDER apontando para uma pasta; nos testes normais não faz nada.
/// </summary>
public sealed class CapturaFluxoImpressao
{
    [Fact]
    public void Captura_o_modal_e_a_preparacao_da_bermuda_cargo()
    {
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_CAPTURAS_BARTENDER");
        if (string.IsNullOrWhiteSpace(pasta)) return;

        Directory.CreateDirectory(pasta);

        Exception? falha = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new NexusLS.BarTender.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (_, e) => e.Handled = true;
                app.InitializeComponent();

                Capturar(pasta);
                app.Shutdown();
            }
            catch (Exception ex)
            {
                falha = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        Assert.Null(falha);
    }

    private static void Capturar(string pasta)
    {
        var selecao = SelecaoBermuda();
        var modelo = Modelo50x30();

        var modal = new JanelaProdutoCatalogo(selecao);
        RenderizarConteudo(modal, Path.Combine(pasta, "20-modal-produto.png"), 900, 680);

        var preparacao = new PreparacaoImpressaoViewModel(new DialogosSilenciosos(), new ImpressoraNula());
        preparacao.Carregar(selecao, modelo, "NEXUS LS");
        preparacao.DefinirImpressoras(new[] { "Zebra ZD230 (exemplo)" }, "Zebra ZD230 (exemplo)");

        var tela = new PreparacaoImpressaoView { DataContext = preparacao };
        RenderizarTela(tela, Path.Combine(pasta, "21-preparacao-rolo.png"), 1360, 980);

        preparacao.MidiaSelecionada = PerfisMidia.FolhaA4TresPorSete;
        preparacao.PerfilImpressoraSelecionado =
            PerfilImpressora.Perfis.First(p => p.Nome.StartsWith("Impressora de folhas", StringComparison.Ordinal));
        preparacao.DefinirImpressoras(new[] { "HP LaserJet (exemplo)" }, "HP LaserJet (exemplo)");
        var folha = new PreparacaoImpressaoView { DataContext = preparacao };
        RenderizarTela(folha, Path.Combine(pasta, "22-preparacao-folha.png"), 1360, 980);
    }

    private static SelecaoImpressaoViewModel SelecaoBermuda()
    {
        var produto = new Produto
        {
            Id = 45,
            Nome = "BERMUDA CARGO",
            Referencia = "1402",
            CodigoInterno = "INT-1402",
            PrecoVendaCentavos = 7000,
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = "42", Ordem = 1, Estoque = 6 },
                new() { Tamanho = "44", Ordem = 2, Estoque = 8 },
                new() { Tamanho = "46", Ordem = 3, Estoque = 8 },
                new() { Tamanho = "48", Ordem = 4, Estoque = 12 },
                new() { Tamanho = "50", Ordem = 5, Estoque = 2 }
            }
        };

        var selecao = new SelecaoImpressaoViewModel(new ProdutoItemViewModel(produto));
        selecao.DefinirQuantidade("42", 30);
        selecao.DefinirQuantidade("44", 38);
        return selecao;
    }

    private static ModeloEtiquetaDesign Modelo50x30() => new()
    {
        Nome = "Etiqueta padrão 50 × 30",
        LarguraMm = 50,
        AlturaMm = 30,
        Elementos = new System.Collections.ObjectModel.ObservableCollection<ElementoDesign>
        {
            new() { Tipo = TipoElemento.Texto, X = 4, Y = 3, Largura = 42, Altura = 6, TamanhoFonte = 11, Variavel = VariavelEtiqueta.NomeProduto },
            new() { Tipo = TipoElemento.Texto, X = 4, Y = 10, Largura = 20, Altura = 5, TamanhoFonte = 8, Variavel = VariavelEtiqueta.Tamanho },
            new() { Tipo = TipoElemento.Texto, X = 26, Y = 9, Largura = 20, Altura = 8, TamanhoFonte = 14, Negrito = true, Variavel = VariavelEtiqueta.PrecoVarejo },
            new() { Tipo = TipoElemento.CodigoBarras, X = 4, Y = 17, Largura = 42, Altura = 10 }
        }
    };

    private static void RenderizarConteudo(Window janela, string caminho, int largura, int altura)
    {
        janela.WindowStartupLocation = WindowStartupLocation.Manual;
        janela.ShowInTaskbar = false;
        janela.ShowActivated = false;
        janela.Left = -20000;
        janela.Top = 0;
        janela.Width = largura;
        janela.Height = altura;
        janela.Show();
        janela.UpdateLayout();
        Renderizar((FrameworkElement)janela.Content, caminho, largura, altura);
        janela.Close();
    }

    private static void RenderizarTela(FrameworkElement tela, string caminho, int largura, int altura)
    {
        var janela = new Window
        {
            Width = largura,
            Height = altura,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = 0,
            Background = (Brush)Application.Current.Resources["CorFundo"],
            Content = new Border { Padding = new Thickness(20), Child = tela }
        };

        janela.Show();
        janela.UpdateLayout();
        Renderizar((FrameworkElement)janela.Content, caminho, largura, altura);
        janela.Close();
    }

    private static void Renderizar(FrameworkElement elemento, string caminho, int largura, int altura)
    {
        var bitmap = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(elemento);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var arquivo = File.Create(caminho);
        png.Save(arquivo);
    }

    private sealed class DialogosSilenciosos : NexusLS.BarTender.Services.IDialogoService
    {
        public bool Confirmar(string mensagem, string titulo = "Confirmação", string textoConfirmar = "Confirmar", string textoCancelar = "Cancelar") => true;
        public void Informar(string mensagem, string titulo = "Informação") { }
        public void Erro(string mensagem, string titulo = "Erro") { }
        public void Sucesso(string mensagem, string titulo = "Sucesso") { }
    }

    private sealed class ImpressoraNula : NexusLS.BarTender.Services.IImpressoraEtiquetas
    {
        public bool ImprimirPreparacao(
            IEnumerable<NexusLS.BarTender.Models.ItemFila> itens,
            ModeloEtiquetaDesign modelo,
            string nomeLoja,
            PerfilMidia midia,
            string? nomeImpressora,
            int primeiraPosicao = 1) => true;
    }
}
