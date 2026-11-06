using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha o PDV e o Caixa (com o fechamento dentro) em PNG para conferir o visual. Só roda com NEXUSLS_PRINTS
/// apontando para uma pasta (e só usa o banco de teste, nunca o real). NEXUSLS_PRINTS_TAMANHO=1900x1000 força o tamanho.
/// Rode UM teste por vez (--filter).
/// </summary>
public sealed class CapturaCaixaPdvNovo : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-print-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private static string? Saida()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return null;
        Directory.CreateDirectory(saida);
        return saida;
    }

    private static string Sufixo() => (Environment.GetEnvironmentVariable("NEXUSLS_PRINTS_TAMANHO") ?? "1400x820");

    private static async Task AteAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 400 && !condicao(); i++) await Task.Delay(10);
    }

    private async Task<(ProdutoService produtos, Produto camiseta, Produto bone)> LojaAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "7891234567895", 5990, 2000, 2, "CAM-100", 3990), 2);
        foreach (var t in new[] { "P", "M", "G", "GG" }) await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 6, null, "P");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 9, null, "M");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 4, null, "G");
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "7890000000555", 2990, 800, 1, "BON-020"), 10);
        await produtos.CadastrarAsync(new DadosProduto("Camisa Polo Piquet", null, 8990, 3000, 1, "POL-300"), 5);
        return (produtos, camiseta, bone);
    }

    [Fact]
    public async Task Gera_o_pdv()
    {
        var saida = Saida();
        if (saida is null) return;
        var (produtos, _, _) = await LojaAsync();
        var clientes = new ClienteService(_banco.Fabrica);
        var caixa = new CaixaService(_banco.Fabrica);
        await caixa.AbrirAsync(10000);
        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        var vm = new PdvViewModel(produtos, new VendaService(_banco.Fabrica),
            new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "r.json")), _banco.Fabrica), clientes, cupons, TimeSpan.Zero, caixa: caixa);
        void Tecla(params ComandoTecla[] ts) => Capturador.Rodar(() => { foreach (var t in ts) vm.ProcessarTecla(t); });
        await vm.AtualizarCaixaAsync();
        var s = Sufixo();

        Capturador.Salvar(vm, Path.Combine(saida, $"pdv-vazio-{s}.png"));

        Capturador.Rodar(() => vm.Busca = "cam");
        await AteAsync(() => vm.Resultados.Count == 2);
        Capturador.Salvar(vm, Path.Combine(saida, $"pdv-busca-{s}.png"));

        Tecla(ComandoTecla.Baixo, ComandoTecla.Enter);
        await AteAsync(() => vm.Etapa == EtapaPdv.Tamanho);
        Tecla(ComandoTecla.Direita, ComandoTecla.Enter, ComandoTecla.Cima, ComandoTecla.Enter);
        Capturador.Rodar(() => vm.Busca = "7890000000555");
        Tecla(ComandoTecla.Enter);
        await AteAsync(() => vm.Etapa == EtapaPdv.Tamanho);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);
        Capturador.Rodar(() => vm.Busca = "");
        Capturador.Salvar(vm, Path.Combine(saida, $"pdv-carrinho-{s}.png"));

        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);
        Capturador.Rodar(() => vm.ValorRecebidoTexto = "200");
        Capturador.Salvar(vm, Path.Combine(saida, $"pdv-pagamento-{s}.png"));
    }

    [Fact]
    public async Task Gera_o_caixa()
    {
        var saida = Saida();
        if (saida is null) return;
        var (_, camiseta, bone) = await LojaAsync();
        var clientes = new ClienteService(_banco.Fabrica);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria Souza"));
        var servicoCaixa = new CaixaService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica);
        await servicoCaixa.AbrirAsync(10000);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 2, "M"), new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Dinheiro, 20000));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 1, "P") }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 4) }, 0, FormaPagamento.CartaoCredito, Parcelas: 2));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 3, "G") }, 1000, FormaPagamento.APrazo, ClienteId: maria.Id));
        var cancelada = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 3) }, 0, FormaPagamento.Pix));
        await vendas.CancelarAsync(cancelada.Id, "Cliente desistiu");
        await servicoCaixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 5000, "Levou ao banco");

        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        VendasViewModel? vm = null;
        Capturador.Rodar(() => vm = new VendasViewModel(vendas, cupons, servicoCaixa,
            new FechamentoViewModel(servicoCaixa, new ConfiguracaoService(_banco.Fabrica), new RelatorioCaixaPeriodoService(_banco.Fabrica))));
        await AteAsync(() => vm!.Vendas.Count == 5 && vm.Fechamento!.Cabecalho.Length > 0);
        Capturador.Rodar(() => vm!.Selecionada = vm.Vendas[1]);
        var s = Sufixo();

        Capturador.Desenhar(() => new VendasView { DataContext = vm }, Path.Combine(saida, $"caixa-vendas-{s}.png"), 1400, 820);
        Capturador.Desenhar(() => new VendasView { DataContext = vm }, Path.Combine(saida, $"caixa-conferencia-{s}.png"), 1400, 820,
            tela => ((System.Windows.Controls.TabControl)((VendasView)tela).FindName("Abas")).SelectedIndex = 1);
    }

    [Fact]
    public async Task Gera_o_caixa_fechado()
    {
        var saida = Saida();
        if (saida is null) return;
        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        var servicoCaixa = new CaixaService(_banco.Fabrica);
        VendasViewModel? vm = null;
        Capturador.Rodar(() => vm = new VendasViewModel(new VendaService(_banco.Fabrica), cupons, servicoCaixa,
            new FechamentoViewModel(servicoCaixa, new ConfiguracaoService(_banco.Fabrica))));
        await AteAsync(() => vm!.Fechamento!.Cabecalho.Length > 0);
        Capturador.Desenhar(() => new VendasView { DataContext = vm }, Path.Combine(saida, $"caixa-fechado-{Sufixo()}.png"), 1400, 820);
    }
}
