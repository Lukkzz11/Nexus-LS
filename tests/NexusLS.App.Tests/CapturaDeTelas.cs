using System.Windows.Controls;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha as telas do PDV em arquivos PNG (sem usar o teclado nem o foco do Windows) para conferir o visual.
/// Só roda quando a variável de ambiente NEXUSLS_PRINTS aponta para uma pasta; nos testes normais não faz nada.
/// </summary>
public sealed class CapturaDeTelas : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-print-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public async Task Gera_as_telas_do_fluxo()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var clientes = new ClienteService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "789", 5990, 2000, 2, PrecoAtacadoCentavos: 3990), 2);
        foreach (var t in new[] { "P", "M", "G", "GG" }) await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 6, null, "P");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 9, null, "M");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 4, null, "G");
        await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 1), 10);
        await produtos.CadastrarAsync(new DadosProduto("Camisa Polo Piquet", null, 8990, 3000, 1), 5);
        await clientes.CadastrarAsync(new DadosCliente("Maria Souza"));
        await clientes.CadastrarAsync(new DadosCliente("Marcos Lima"));

        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        var vm = new PdvViewModel(produtos, new VendaService(_banco.Fabrica),
            new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "r.json")), _banco.Fabrica), clientes, cupons, TimeSpan.Zero);

        async Task Espera(Func<bool> c) { for (var i = 0; i < 300 && !c(); i++) await Task.Delay(10); }
        void Tecla(params ComandoTecla[] ts) => Capturador.Rodar(() => { foreach (var t in ts) vm.ProcessarTecla(t); });

        Capturador.Salvar(vm, Path.Combine(saida, "01-caixa-vazio.png"));

        // busca com resultados ao vivo
        Capturador.Rodar(() => vm.Busca = "cam");
        await Espera(() => vm.Resultados.Count == 2);
        Capturador.Salvar(vm, Path.Combine(saida, "02-busca-resultados.png"));

        // tamanho
        Tecla(ComandoTecla.Baixo, ComandoTecla.Enter);
        await Espera(() => vm.Etapa == EtapaPdv.Tamanho);
        Tecla(ComandoTecla.Direita);
        Capturador.Salvar(vm, Path.Combine(saida, "03-tamanho.png"));

        // quantidade
        Tecla(ComandoTecla.Enter, ComandoTecla.Cima);
        Capturador.Salvar(vm, Path.Combine(saida, "04-quantidade.png"));
        Tecla(ComandoTecla.Enter);

        // segunda linha + preço editado
        Capturador.Rodar(() => vm.Busca = "555");
        Tecla(ComandoTecla.Enter);
        await Espera(() => vm.Etapa == EtapaPdv.Tamanho);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);
        Capturador.Rodar(() => vm.Itens[1].PrecoUnitarioTexto = "25,00");
        Capturador.Salvar(vm, Path.Combine(saida, "05-caixa-com-itens.png"));

        // desconto
        Tecla(ComandoTecla.Enter);
        Tecla(ComandoTecla.Direita);
        Capturador.Rodar(() => vm.DescontoTexto = "10");
        Capturador.Salvar(vm, Path.Combine(saida, "06-desconto.png"));

        // pagamento: dinheiro com troco
        Tecla(ComandoTecla.Enter);
        Capturador.Rodar(() => vm.ValorRecebidoTexto = "200");
        Capturador.Salvar(vm, Path.Combine(saida, "07-pagamento-dinheiro.png"));

        // crédito 3x
        Tecla(ComandoTecla.Direita, ComandoTecla.Direita, ComandoTecla.Direita, ComandoTecla.Cima, ComandoTecla.Cima);
        Capturador.Salvar(vm, Path.Combine(saida, "08-pagamento-credito.png"));

        // a prazo
        Tecla(ComandoTecla.Direita, ComandoTecla.Direita);
        await Espera(() => vm.ClientesResultado.Count == 2);
        Capturador.Salvar(vm, Path.Combine(saida, "09-pagamento-a-prazo.png"));

        // finaliza em PIX e mostra o sucesso
        Tecla(ComandoTecla.Esquerda, ComandoTecla.Esquerda, ComandoTecla.Esquerda, ComandoTecla.Esquerda);
        Tecla(ComandoTecla.Enter);
        await Espera(() => vm.Etapa == EtapaPdv.Sucesso);
        Capturador.Salvar(vm, Path.Combine(saida, "10-sucesso.png"));
    }

    [Fact]
    public async Task Gera_a_tela_de_configuracoes()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        await configuracao.SalvarAsync(new DadosLoja("Casa Nexus", "12.345.678/0001-90", "Rua das Flores, 123 - Centro",
            "(11) 3333-4444", "Obrigado pela preferência! Volte sempre."));
        var cupons = new CupomService(_banco.Fabrica, configuracao, new ImpressoraDeMentira());

        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ConfiguracoesViewModel(configuracao, cupons));
        for (var i = 0; i < 300 && vm!.NomeLoja != "Casa Nexus"; i++) await Task.Delay(10);

        Capturador.SalvarConfiguracoes(vm!, Path.Combine(saida, "11-configuracoes.png"));
    }

    [Fact]
    public async Task Gera_a_tela_de_estoque_da_loja()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Basica Algodao", "789", 5990, 2000, 12, "REF-100", 3990), 2);
        foreach (var t in new[] { "P", "M", "G" }) await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 6, "Compra", "P");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 3, "Compra", "M");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Perda, 1, "Rasgou", "M");
        await produtos.CadastrarAsync(new DadosProduto("Bone Aba Reta", "555", 2990, 800, 5), 10);
        await produtos.CadastrarAsync(new DadosProduto("Cinto Couro", null, 8990, 3000, 5), 1);

        EstoqueLojaViewModel? vm = null;
        Capturador.Rodar(() => vm = new EstoqueLojaViewModel(produtos, estoque));
        for (var i = 0; i < 300 && vm!.Produtos.Count < 3; i++) await Task.Delay(10);
        Capturador.Rodar(() => vm!.Selecionado = vm.Produtos.First(p => p.Nome.StartsWith("Camiseta")));
        for (var i = 0; i < 300 && vm!.Grade.Count < 4; i++) await Task.Delay(10);
        await Task.Delay(100);

        Capturador.SalvarEstoque(vm!, Path.Combine(saida, "12-estoque-produto.png"), 0);
        Capturador.SalvarEstoque(vm!, Path.Combine(saida, "13-estoque-tamanhos.png"), 1);
    }

    [Fact]
    public async Task Gera_as_telas_de_caixa_e_mais_vendidos()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica);
        var clientes = new ClienteService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Basica Algodao", null, 5990, 2000, 2), 0);
        foreach (var t in new[] { "P", "M", "G" })
        {
            await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
            await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 50, null, t);
        }
        var bone = await produtos.CadastrarAsync(new DadosProduto("Bone Aba Reta", null, 2990, 800, 5), 100);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria Souza"));

        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 2, "M"), new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Dinheiro, 20000));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 1, "P") }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 4) }, 0, FormaPagamento.CartaoCredito, Parcelas: 2));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 3, "G") }, 1000, FormaPagamento.APrazo, ClienteId: maria.Id));
        var cancelada = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 9) }, 0, FormaPagamento.Pix));
        await vendas.CancelarAsync(cancelada.Id, "Teste");

        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        VendasViewModel? caixa = null;
        MaisVendidosViewModel? ranking = null;
        Capturador.Rodar(() =>
        {
            caixa = new VendasViewModel(vendas, cupons);
            ranking = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        });
        for (var i = 0; i < 300 && (caixa!.Vendas.Count < 5 || ranking!.Ranking.Count < 2); i++) await Task.Delay(10);
        Capturador.Rodar(() => caixa!.Selecionada = caixa.Vendas.Last());
        await Task.Delay(100);

        Capturador.Desenhar(() => new VendasView { DataContext = caixa }, Path.Combine(saida, "14-caixa.png"), 1400, 820);
        Capturador.Desenhar(() => new MaisVendidosView { DataContext = ranking }, Path.Combine(saida, "15-mais-vendidos.png"), 1400, 820);
    }

    [Fact]
    public async Task Gera_as_telas_de_clientes_relatorios_e_etiquetas()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica);
        var clientes = new ClienteService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Basica Algodao", "7891234567895", 5990, 2000, 2, "REF-100", 3990), 0);
        foreach (var t in new[] { "P", "M", "G" })
        {
            await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
            await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 50, null, t);
        }
        var bone = await produtos.CadastrarAsync(new DadosProduto("Bone Aba Reta", "555123456789", 2990, 800, 5), 100);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria Souza", "123.456.789-00", "(11) 99999-0000", "Prefere avisar por WhatsApp"));
        await clientes.CadastrarAsync(new DadosCliente("Marcos Lima", null, "(11) 98888-7777"));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 2, "M"), new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix, ClienteId: maria.Id));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 3, "G") }, 1000, FormaPagamento.APrazo, ClienteId: maria.Id));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 4) }, 0, FormaPagamento.Dinheiro, 20000));

        ClientesViewModel? cli = null;
        RelatoriosViewModel? rel = null;
        EtiquetasViewModel? etq = null;
        Capturador.Rodar(() =>
        {
            cli = new ClientesViewModel(clientes);
            rel = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica));
            etq = new EtiquetasViewModel(produtos);
        });
        for (var i = 0; i < 300 && (cli!.Clientes.Count < 2 || rel!.Vendas.Count < 3); i++) await Task.Delay(10);
        Capturador.Rodar(() => cli!.Selecionado = cli.Clientes.First(c => c.Nome.StartsWith("Maria")));
        for (var i = 0; i < 300 && cli!.Compras.Count < 2; i++) await Task.Delay(10);

        Capturador.Rodar(() => etq!.Busca = "Camiseta");
        Capturador.Rodar(() => etq!.EscolherPrimeiroCommand.Execute(null));
        for (var i = 0; i < 300 && etq!.OpcoesDeTamanho.Count < 4; i++) await Task.Delay(10);
        Capturador.Rodar(() =>
        {
            etq!.QuantidadeTexto = "3";
            etq.AdicionarTodosOsTamanhosCommand.Execute(null);
            etq.Busca = "Bone";
        });
        await Task.Delay(300);
        Capturador.Rodar(() => etq!.EscolherPrimeiroCommand.Execute(null));
        for (var i = 0; i < 300 && etq!.OpcoesDeTamanho.Count != 1; i++) await Task.Delay(10);
        Capturador.Rodar(() => { etq!.QuantidadeTexto = "2"; etq.AdicionarCommand.Execute(null); });

        Capturador.Desenhar(() => new ClientesView { DataContext = cli }, Path.Combine(saida, "16-clientes.png"), 1400, 820);
        Capturador.Desenhar(() => new RelatoriosView { DataContext = rel }, Path.Combine(saida, "17-relatorios.png"), 1400, 820);
        Capturador.Desenhar(() => new EtiquetasView { DataContext = etq }, Path.Combine(saida, "18-etiquetas-bobina.png"), 1400, 900);
        Capturador.Rodar(() => { etq!.FormatoSelecionado = FormatoEtiqueta.FolhaA4Tres_por_Sete; etq.PrimeiraPosicaoTexto = "4"; });
        Capturador.Desenhar(() => new EtiquetasView { DataContext = etq }, Path.Combine(saida, "19-etiquetas-folha.png"), 1400, 900);
    }
}

/// <summary>Uma thread própria de interface (STA) com o Application do programa carregado, só para desenhar.</summary>
internal static class Capturador
{
    private static readonly Dispatcher Interface = Iniciar();

    private static Dispatcher Iniciar()
    {
        Dispatcher? dispatcher = null;
        using var pronto = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new NexusLS.App.App();
            app.InitializeComponent(); // carrega estilos, conversores e modelos de tela do App.xaml
            dispatcher = Dispatcher.CurrentDispatcher;
            pronto.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        pronto.Wait();
        return dispatcher!;
    }

    /// <summary>Roda na thread da interface, como no programa de verdade (os ViewModels só mexem nas listas ali).</summary>
    public static void Rodar(Action acao) => Interface.Invoke(acao);

    public static void Salvar(PdvViewModel vm, string caminho, int largura = 1400, int altura = 820) =>
        Desenhar(() => new PdvView { DataContext = vm }, caminho, largura, altura);

    public static void SalvarEstoque(EstoqueLojaViewModel vm, string caminho, int aba, int largura = 1400, int altura = 820) =>
        Desenhar(() => new EstoqueLojaView { DataContext = vm }, caminho, largura, altura, tela =>
        {
            // escolhe a aba do painel direito pelo codigo (nao ha teclado nem mouse aqui)
            if (tela is EstoqueLojaView v && v.Content is System.Windows.Controls.Grid g)
                foreach (var filho in g.Children.OfType<System.Windows.Controls.Grid>().SelectMany(x => x.Children.OfType<System.Windows.Controls.TabControl>()))
                    filho.SelectedIndex = aba;
        });

    private static FrameworkElement Ajustada(FrameworkElement tela, Action<FrameworkElement>? ajustar)
    {
        ajustar?.Invoke(tela);
        return tela;
    }

    public static void SalvarCatalogoOnline(CatalogoOnlineViewModel vm, string caminho, int largura = 1400, int altura = 1500) =>
        Desenhar(() => new CatalogoOnlineView { DataContext = vm }, caminho, largura, altura);

    public static void SalvarInicio(InicioViewModel vm, string caminho, int largura = 1400, int altura = 900) =>
        Desenhar(() => new InicioView { DataContext = vm }, caminho, largura, altura);

    public static void SalvarConfiguracoes(ConfiguracoesViewModel vm, string caminho, int largura = 1400, int altura = 820) =>
        Desenhar(() => new ConfiguracoesView { DataContext = vm }, caminho, largura, altura);

    internal static void Desenhar(Func<FrameworkElement> criarTela, string caminho, int largura, int altura, Action<FrameworkElement>? ajustar = null)
    {
        Interface.Invoke(() =>
        {
            var janela = new Window
            {
                Width = largura,
                Height = altura,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,   // não rouba o foco de quem está usando o computador
                Left = -20000,
                Top = 0,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15,
                Background = (Brush)Application.Current.Resources["CorFundo"],
                Content = new Border
                {
                    Padding = new Thickness(20),
                    Child = Ajustada(criarTela(), ajustar)
                }
            };
            janela.Show();
            janela.UpdateLayout();
            Interface.Invoke(DispatcherPriority.ApplicationIdle, () => { }); // deixa os bindings e o foco assentarem

            var imagem = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
            imagem.Render((Visual)janela.Content);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(imagem));
            using (var arquivo = File.Create(caminho)) png.Save(arquivo);

            janela.Close();
        });
    }
}
