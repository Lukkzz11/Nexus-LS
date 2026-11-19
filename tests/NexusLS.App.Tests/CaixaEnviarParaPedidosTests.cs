using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class CaixaEnviarParaPedidosTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private async Task<(VendasViewModel Caixa, PedidoService Pedidos, ProdutoService Produtos, VendaService Vendas, int ProdutoId)> MontarAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica);
        var pedidos = new PedidoService(_banco.Fabrica);
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 50)).Id;
        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        var caixa = new VendasViewModel(vendas, cupons, pedidos: pedidos);
        return (caixa, pedidos, produtos, vendas, bone);
    }

    [Fact]
    public async Task Enviar_para_pedidos_marca_a_venda_com_o_numero_e_mantem_o_total_do_dia()
    {
        var (caixa, pedidos, _, vendas, bone) = await MontarAsync();
        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 2) }, 0, FormaPagamento.Pix));
        caixa.CarregarCommand.Execute(null);
        await AteAsync(() => caixa.Vendas.Count == 1, "lista de vendas");
        var totalAntes = caixa.TotalDiaTexto;

        caixa.Selecionada = caixa.Vendas[0];
        Assert.True(caixa.PodeEnviarParaPedidos);
        await caixa.EnviarParaPedidosCommand.ExecuteAsync(null);

        var pedido = Assert.Single(await pedidos.ListarAsync());
        Assert.Equal(venda.Id, pedido.VendaId);
        Assert.Equal(StatusPedido.Novo, pedido.Status);
        Assert.Equal($"Enviada para Pedidos nº {pedido.Numero}", caixa.TextoPedido);
        Assert.True(caixa.TemSeloPedido);
        Assert.False(caixa.PodeEnviarParaPedidos);
        Assert.Equal(totalAntes, caixa.TotalDiaTexto);
        Assert.Equal("1", caixa.QuantidadeVendasTexto);
    }

    [Fact]
    public async Task Enviar_de_novo_nao_cria_outro_pedido()
    {
        var (caixa, pedidos, _, vendas, bone) = await MontarAsync();
        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1) }, 0, FormaPagamento.Pix));
        caixa.CarregarCommand.Execute(null);
        await AteAsync(() => caixa.Vendas.Count == 1, "lista de vendas");
        caixa.Selecionada = caixa.Vendas[0];
        await caixa.EnviarParaPedidosCommand.ExecuteAsync(null);

        // outra tela/aparelho tenta enviar a mesma venda
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => pedidos.EnviarVendaAsync(venda.Id));
        Assert.False(caixa.EnviarParaPedidosCommand.CanExecute(null));
        Assert.Single(await pedidos.ListarAsync());
    }

    [Fact]
    public async Task Venda_cancelada_nao_oferece_envio()
    {
        var (caixa, _, _, vendas, bone) = await MontarAsync();
        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1) }, 0, FormaPagamento.Pix));
        await vendas.CancelarAsync(venda.Id, "Desistiu", true);
        caixa.CarregarCommand.Execute(null);
        await AteAsync(() => caixa.Vendas.Count == 1, "lista de vendas");

        caixa.Selecionada = caixa.Vendas[0];

        Assert.False(caixa.PodeEnviarParaPedidos);
    }

    /// <summary>Desenha o Caixa com uma venda enviada e o quadro de Pedidos. Só roda com NEXUSLS_PRINTS.</summary>
    [Fact]
    public async Task Gera_caixa_com_selo_e_pedidos_com_a_venda()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var (caixa, pedidos, produtos, vendas, bone) = await MontarAsync();
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 3) }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1) }, 0, FormaPagamento.Dinheiro, 5000));
        caixa.CarregarCommand.Execute(null);
        await AteAsync(() => caixa.Vendas.Count == 2, "lista de vendas");
        caixa.Selecionada = caixa.Vendas.Last();
        await caixa.EnviarParaPedidosCommand.ExecuteAsync(null);
        Capturador.Rodar(() => caixa.Selecionada = caixa.Vendas.Last());
        Capturador.Desenhar(() => new VendasView { DataContext = caixa }, Path.Combine(saida, "caixa-enviada-pedidos.png"), 1400, 820);

        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", "789", 5990, 2200, 5), 0)).Id;
        await pedidos.CriarAsync(new DadosPedido(OrigemPedido.WhatsApp, new[] { new ItemPedidoDados(camiseta, 3) }, ClienteNome: "Maria Souza"));
        var vm = new PedidosViewModel(pedidos, produtos, new ClienteService(_banco.Fabrica));
        await vm.Carregamento;
        Capturador.Desenhar(() => new PedidosView { DataContext = vm }, Path.Combine(saida, "pedidos-com-venda-do-caixa.png"), 1500, 820);
    }
}
