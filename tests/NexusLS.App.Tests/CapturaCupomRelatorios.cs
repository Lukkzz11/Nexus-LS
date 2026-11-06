using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha em PNG Relatórios (aba Gráficos e aba Vendas) e Configurações → cupom, com dados e com a loja vazia, em 1900x1000 e
/// 1280x760. Só roda com NEXUSLS_PRINTS apontando para uma pasta (e só usa o banco de teste). Rode UM teste por vez (--filter).
/// </summary>
[Collection("OperadorAtual")]
public sealed class CapturaCupomRelatorios : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 10, 20, 15, 0, 0);
    private static readonly (int Largura, int Altura)[] Tamanhos = { (1900, 1000), (1280, 760) };
    private readonly BancoDeTeste _banco = new();

    public CapturaCupomRelatorios() => OperadorAtual.Nome = "Ana";

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static string? Pasta()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return null;
        Directory.CreateDirectory(saida);
        return saida;
    }

    private static async Task AteAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 400 && !condicao(); i++) await Task.Delay(10);
    }

    /// <summary>Uma loja movimentada: outubro (período escolhido) e setembro (período anterior) com vendas variadas.</summary>
    private async Task PovoarVendasAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var catalogo = new (string Nome, string Categoria, long Preco, long Custo)[]
        {
            ("Camiseta Básica Algodão", "Camisetas", 5990, 2000),
            ("Camisa Polo Piquet", "Camisas", 8990, 3000),
            ("Boné Aba Reta", "Acessórios", 2990, 800),
            ("Bermuda Sarja", "Bermudas", 7990, 3500),
            ("Vestido Midi Floral", "Vestidos", 12990, 5000),
            ("Meia Cano Alto", "Acessórios", 1990, 500),
            ("Calça Jeans Slim", "Calças", 15990, 6000),
            ("Jaqueta Corta-vento", "Jaquetas", 19990, 8000),
            ("Short Moletom", "Bermudas", 6990, 2500)
        };
        var ids = new List<int>();
        for (var i = 0; i < catalogo.Length; i++)
        {
            var c = catalogo[i];
            ids.Add((await produtos.CadastrarAsync(new DadosProduto(c.Nome, $"78900{i}", c.Preco, c.Custo, 2, Categoria: c.Categoria), 2000)).Id);
        }

        var formas = new[] { FormaPagamento.Pix, FormaPagamento.Dinheiro, FormaPagamento.CartaoDebito, FormaPagamento.CartaoCredito, FormaPagamento.Pix };
        var sorteio = new Random(7);
        var dias = Enumerable.Range(0, 20).Select(d => new DateTime(2026, 10, 1).AddDays(d))
            .Concat(Enumerable.Range(0, 20).Select(d => new DateTime(2026, 9, 11).AddDays(d))).ToList();
        foreach (var dia in dias)
        {
            var vendas = new VendaService(_banco.Fabrica, () => dia.AddHours(10));
            var doPeriodoAnterior = dia.Month == 9;
            var quantas = sorteio.Next(doPeriodoAnterior ? 1 : 2, doPeriodoAnterior ? 4 : 6);
            for (var k = 0; k < quantas; k++)
            {
                var itens = Enumerable.Range(0, sorteio.Next(1, 4))
                    .Select(_ => new ItemCarrinho(ids[Math.Min(ids.Count - 1, (int)(Math.Pow(sorteio.NextDouble(), 1.6) * ids.Count))], sorteio.Next(1, 4)))
                    .GroupBy(i => i.ProdutoId).Select(g => new ItemCarrinho(g.Key, g.Sum(i => i.Quantidade))).ToList();
                var forma = formas[sorteio.Next(formas.Length)];
                var desconto = sorteio.Next(5) == 0 ? 500 : 0;
                await vendas.FinalizarAsync(new PedidoVenda(itens, desconto, forma, forma == FormaPagamento.Dinheiro ? 100000 : null,
                    Parcelas: forma == FormaPagamento.CartaoCredito ? 2 : 1));
            }
        }
    }

    private static void SelecionarAba(FrameworkElement tela, int aba)
    {
        var grade = LogicalTreeHelper.GetChildren(tela).Cast<object>().OfType<Grid>().First();
        grade.Children.OfType<TabControl>().First().SelectedIndex = aba;
    }

    /// <summary>Rola até o fim a lista da aba aberta (a captura não tem mouse), para mostrar o que fica abaixo da dobra.</summary>
    private static void RolarAteOFim(FrameworkElement tela) =>
        tela.Loaded += (_, _) => tela.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            foreach (var rolagem in Descendentes<ScrollViewer>(tela).Where(r => r.ScrollableHeight > 0)) rolagem.ScrollToEnd();
        });

    private static IEnumerable<T> Descendentes<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = System.Windows.Media.VisualTreeHelper.GetChild(raiz, i);
            if (filho is T t) yield return t;
            foreach (var neto in Descendentes<T>(filho)) yield return neto;
        }
    }

    private async Task<RelatoriosViewModel> CriarRelatoriosAsync(bool esperarVendas)
    {
        RelatoriosViewModel? vm = null;
        Capturador.Rodar(() => vm = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica), Hoje.Date,
            new DemonstrativoService(_banco.Fabrica, () => Hoje)));
        if (esperarVendas) await AteAsync(() => vm!.Vendas.Count > 10 && vm.Dre.Count > 0 && vm.PizzaCategorias.Count > 0);
        else await Task.Delay(500);
        return vm!;
    }

    [Fact]
    public async Task Gera_relatorios_com_dados()
    {
        var saida = Pasta();
        if (saida is null) return;
        await PovoarVendasAsync();
        var vm = await CriarRelatoriosAsync(true);

        foreach (var (l, a) in Tamanhos)
        {
            Capturador.Desenhar(() => new RelatoriosView { DataContext = vm }, Path.Combine(saida, $"relatorios-graficos-{l}x{a}.png"), l, a);
            Capturador.Desenhar(() => new RelatoriosView { DataContext = vm }, Path.Combine(saida, $"relatorios-graficos-rolado-{l}x{a}.png"), l, a, RolarAteOFim);
            Capturador.Desenhar(() => new RelatoriosView { DataContext = vm }, Path.Combine(saida, $"relatorios-vendas-{l}x{a}.png"), l, a, t => SelecionarAba(t, 1));
        }
        // tela comprida: mostra todos os gráficos de uma vez
        Capturador.Desenhar(() => new RelatoriosView { DataContext = vm }, Path.Combine(saida, "relatorios-graficos-completo-1900x1500.png"), 1900, 1500);
        Capturador.Desenhar(() => new RelatoriosView { DataContext = vm }, Path.Combine(saida, "relatorios-graficos-completo-1280x1700.png"), 1280, 1700);
    }

    [Fact]
    public async Task Gera_relatorios_loja_vazia()
    {
        var saida = Pasta();
        if (saida is null) return;
        var vm = await CriarRelatoriosAsync(false);
        foreach (var (l, a) in Tamanhos)
            Capturador.Desenhar(() => new RelatoriosView { DataContext = vm }, Path.Combine(saida, $"relatorios-vazio-{l}x{a}.png"), l, a);
    }

    private async Task<ConfiguracoesViewModel> CriarConfiguracoesAsync(bool comDados)
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        if (comDados)
            await config.SalvarAsync(new DadosLoja("Casa Nexus Moda", "12.345.678/0001-90", "Rua das Flores, 123 - Centro", "(81) 99999-1234",
                "Obrigado pela preferência!\nTrocas em até 7 dias com a etiqueta.", NomeOperador: "Ana", RazaoSocial: "Casa Nexus Confecções LTDA",
                InscricaoEstadual: "123.456.789", Email: "contato@casanexus.com.br", CidadeUf: "Caruaru - PE", ChavePix: "12.345.678/0001-90",
                CupomMostrarPix: true, CupomMostrarEmail: true, CupomMostrarAssinatura: true,
                CupomMensagemCabecalho: "Moda praia e fitness"));
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira())));
        await AteAsync(() => !comDados || vm!.NomeLoja.StartsWith("Casa"));
        await Task.Delay(200);
        return vm!;
    }

    [Fact]
    public async Task Gera_configuracoes_do_cupom()
    {
        var saida = Pasta();
        if (saida is null) return;
        var vm = await CriarConfiguracoesAsync(true);
        foreach (var (l, a) in Tamanhos)
        {
            Capturador.SalvarConfiguracoesAba(vm, 0, Path.Combine(saida, $"cupom-com-dados-{l}x{a}.png"), l, a);
            Capturador.Desenhar(() => new ConfiguracoesView { DataContext = vm }, Path.Combine(saida, $"cupom-com-dados-rolado-{l}x{a}.png"), l, a, RolarAteOFim);
            Capturador.SalvarConfiguracoesAba(vm, 1, Path.Combine(saida, $"cupom-aba-vendas-{l}x{a}.png"), l, a);
        }
        Capturador.SalvarConfiguracoesAba(vm, 0, Path.Combine(saida, "cupom-com-dados-completo-1900x1500.png"), 1900, 1500);
        Capturador.Rodar(() => vm.LarguraSelecionada = vm.Larguras[0]);
        Capturador.SalvarConfiguracoesAba(vm, 0, Path.Combine(saida, "cupom-58mm-1900x1000.png"), 1900, 1000);
    }

    [Fact]
    public async Task Gera_configuracoes_do_cupom_loja_vazia()
    {
        var saida = Pasta();
        if (saida is null) return;
        var vm = await CriarConfiguracoesAsync(false);
        foreach (var (l, a) in Tamanhos)
            Capturador.SalvarConfiguracoesAba(vm, 0, Path.Combine(saida, $"cupom-loja-vazia-{l}x{a}.png"), l, a);
    }
}
