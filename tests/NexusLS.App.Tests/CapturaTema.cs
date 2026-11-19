using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.App.Aparencia;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha as telas mais usadas (PDV, menu, Configurações, Pedidos, Trocas) nos temas claro e escuro, para conferir
/// a legibilidade a olho. Só roda com NEXUSLS_PRINTS; os arquivos saem como tema-claro-*.png e tema-escuro-*.png.
/// </summary>
public sealed class CapturaTema : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-tema-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Capturador.Rodar(() => GerenciadorDeTema.Aplicar(false));
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private static string CaminhoDoXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusLS.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "NexusLS.App", "MainWindow.xaml");
    }

    private static async Task Espera(Func<bool> c) { for (var i = 0; i < 300 && !c(); i++) await Task.Delay(10); }

    [Fact]
    public async Task Gera_as_telas_nos_dois_temas()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        // ---- dados ----
        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var clientes = new ClienteService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "789", 5990, 2000, 2, PrecoAtacadoCentavos: 3990), 2);
        foreach (var t in new[] { "P", "M", "G", "GG" }) await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 6, null, "P");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 9, null, "M");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 4, null, "G");
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 1), 10);
        await produtos.CadastrarAsync(new DadosProduto("Camisa Polo Piquet", null, 8990, 3000, 1), 5);
        await clientes.CadastrarAsync(new DadosCliente("Maria Souza"));
        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        await configuracao.SalvarAsync(new DadosLoja("Casa Nexus", "12.345.678/0001-90", "Rua das Flores, 123 - Centro",
            "(11) 3333-4444", "Obrigado pela preferência! Volte sempre."));
        var cupons = new CupomService(_banco.Fabrica, configuracao, new ImpressoraDeMentira());

        // PDV: monta um carrinho novo a cada tema e desenha a lista de itens e o pagamento em dinheiro.
        async Task CapturarPdv(string pdvItens, string pdvPagamento)
        {
            var pdv = new PdvViewModel(produtos, vendas,
                new RascunhoService(new RascunhoStore(Path.Combine(_pasta, Guid.NewGuid().ToString("N") + ".json")), _banco.Fabrica), clientes, cupons, TimeSpan.Zero);
            void Tecla(params ComandoTecla[] ts) => Capturador.Rodar(() => { foreach (var t in ts) pdv.ProcessarTecla(t); });
            Capturador.Rodar(() => pdv.Busca = "cam");
            await Espera(() => pdv.Resultados.Count == 2);
            Tecla(ComandoTecla.Baixo, ComandoTecla.Enter);
            await Espera(() => pdv.Etapa == EtapaPdv.Tamanho);
            Tecla(ComandoTecla.Direita, ComandoTecla.Enter, ComandoTecla.Cima, ComandoTecla.Enter);
            Capturador.Rodar(() => pdv.Busca = "555");
            Tecla(ComandoTecla.Enter);
            await Espera(() => pdv.Etapa == EtapaPdv.Tamanho);
            Tecla(ComandoTecla.Enter, ComandoTecla.Enter);
            Capturador.Salvar(pdv, pdvItens, 1400, 820);
            Tecla(ComandoTecla.Enter, ComandoTecla.Enter);
            Capturador.Rodar(() => pdv.ValorRecebidoTexto = "200");
            Capturador.Salvar(pdv, pdvPagamento, 1400, 820);
        }


        // Pedidos
        var pedidos = new PedidoService(_banco.Fabrica);
        Pedido Novo(string cliente, OrigemPedido o, params ItemPedidoDados[] itens) => pedidos.CriarAsync(new DadosPedido(o, itens, ClienteNome: cliente)).GetAwaiter().GetResult();
        var a = Novo("Maria Souza", OrigemPedido.WhatsApp, new ItemPedidoDados(camiseta.Id, 3));
        var b = Novo("João Lima", OrigemPedido.Catalogo, new ItemPedidoDados(camiseta.Id, 1), new ItemPedidoDados(null, 2, "Boné", 3000));
        var c = Novo("Ana Paula", OrigemPedido.Encomenda, new ItemPedidoDados(camiseta.Id, 10));
        var d = Novo("Rui Costa", OrigemPedido.Balcao, new ItemPedidoDados(camiseta.Id, 2));
        Novo("Dona Zefa", OrigemPedido.WhatsApp, new ItemPedidoDados(camiseta.Id, 1));
        await pedidos.AvancarAsync(b.Id);
        await pedidos.AvancarAsync(c.Id); await pedidos.AvancarAsync(c.Id);
        for (var i = 0; i < 3; i++) await pedidos.AvancarAsync(d.Id);
        var pedidosVm = new PedidosViewModel(pedidos, produtos, clientes);
        await pedidosVm.Carregamento;
        pedidosVm.Selecionado = pedidosVm.Colunas[1].Cartoes.First();
        await Task.Delay(300);

        // Trocas
        var trocas = new TrocaService(_banco.Fabrica);
        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 2) }, 0, FormaPagamento.Pix));
        await trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(venda.Itens.Single().Id, 1) }, "Cor errada");
        var trocasVm = new TrocasViewModel(trocas, cupons);
        await trocasVm.Carregamento;
        trocasVm.VendaSelecionada = trocasVm.Vendas[0];
        trocasVm.MostrarDetalhe = _ => true;
        await trocasVm.AbrirVendaCommand.ExecuteAsync(null);
        trocasVm.Linhas.Single().QuantidadeTexto = "1";
        trocasVm.Motivo = "Tamanho errado";

        // Configurações
        var arquivoPref = Path.Combine(_pasta, "pref.json");
        var config = new ConfiguracoesViewModel(configuracao, cupons);
        config.UsarPreferencias(new PreferenciasStore(arquivoPref), _ => { }, () => "Licença válida", _pasta);

        // Menu principal (lê o MainWindow.xaml do código-fonte, como CapturaJanelaPrincipal)
        var xamlMenu = Regex.Replace(Regex.Replace(File.ReadAllText(CaminhoDoXaml()), @"\sx:Class=""[^""]*""", ""), @"\s(Icon|Source)=""pack://[^""]*""", "");
        EstoqueLojaViewModel? estoqueVm = null;
        Capturador.Rodar(() => estoqueVm = new EstoqueLojaViewModel(produtos, estoque));
        for (var i = 0; i < 300 && estoqueVm!.Produtos.Count < 3; i++) await Task.Delay(10);
        Capturador.Rodar(() => estoqueVm!.Selecionado = estoqueVm.Produtos.First(p => p.Id == camiseta.Id));
        await Task.Delay(150);

        foreach (var (nome, escuro) in new[] { ("claro", false), ("escuro", true) })
        {
            Capturador.Rodar(() => GerenciadorDeTema.Aplicar(escuro));
            string P(string arq) => Path.Combine(saida, $"tema-{nome}-{arq}.png");

            await CapturarPdv(P("pdv"), P("pdv-pagamento"));
            Capturador.Desenhar(() => new NexusLS.App.Views.PedidosView { DataContext = pedidosVm }, P("pedidos"), 1500, 820);
            Capturador.Desenhar(() => new NexusLS.App.Views.TrocasView { DataContext = trocasVm }, P("trocas"), 1400, 820);
            Capturador.SalvarConfiguracoesPagina(config, "aparencia", P("config-aparencia"), 1300, 640);
            Capturador.SalvarConfiguracoesPagina(config, "loja", P("config-loja"), 1300, 820);
            Capturador.SalvarConfiguracoesPagina(config, "seguranca", P("config-seguranca"), 1300, 820);
            Capturador.DesenharJanela(() => new NexusLS.App.Views.ConfiguracoesWindow(config), P("janela-configuracoes"), 1180, 780);

            Capturador.Rodar(() =>
            {
                var janela = (Window)XamlReader.Parse(xamlMenu);
                var raiz = (FrameworkElement)janela.Content;
                raiz.DataContext = new CapturaJanelaPrincipal.MenuFalso { ItemAtivo = ItemMenu.EstoqueDaLoja, TelaAtual = estoqueVm };
                janela.Content = null;
                const int largura = 1440, altura = 860;
                var fundo = new System.Windows.Controls.Border { Width = largura, Height = altura, Background = (Brush)Application.Current.Resources["CorFundo"], Child = raiz };
                fundo.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "CorTexto"); // na janela real o texto herda da janela
                fundo.Measure(new Size(largura, altura));
                fundo.Arrange(new Rect(0, 0, largura, altura));
                fundo.UpdateLayout();
                var imagem = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
                imagem.Render(fundo);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(imagem));
                using var arquivo = File.Create(P("menu"));
                png.Save(arquivo);
            });
        }
        Capturador.Rodar(() => GerenciadorDeTema.Aplicar(false));
    }
}
