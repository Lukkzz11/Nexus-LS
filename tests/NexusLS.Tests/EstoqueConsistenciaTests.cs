using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// O estoque total do produto (<c>EstoqueAtual</c>) e o estoque por tamanho ficam guardados em dois lugares.
/// Estes testes garantem que, depois de CADA operação que mexe em estoque, os dois continuam iguais.
/// Se alguém criar uma operação nova e esquecer um dos lados, o teste que a incluir aqui acusa.
/// </summary>
public class EstoqueConsistenciaTests
{
    private static void Conferir(BancoDeTeste banco)
    {
        using var db = banco.CriarContexto();
        foreach (var p in db.Produtos.ToList())
            Assert.Equal(db.TamanhosProduto.Where(t => t.ProdutoId == p.Id).Sum(t => t.Estoque), p.EstoqueAtual);
    }

    [Fact]
    public async Task Total_e_tamanhos_batem_depois_de_cada_operacao_de_estoque()
    {
        var agora = new DateTime(2026, 10, 21, 10, 0, 0);
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var estoque = new EstoqueService(banco.Fabrica, () => agora);
        var vendas = new VendaService(banco.Fabrica, () => agora);
        var compras = new CompraService(banco.Fabrica, () => agora);

        var p = await produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 2), 10);
        await produtos.AdicionarTamanhoAsync(p.Id, "M");
        Conferir(banco);

        await estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 5, null, "M");
        Conferir(banco);

        await estoque.AjustarAsync(p.Id, TipoMovimentacao.Perda, 1, "rasgou", "M");
        Conferir(banco);

        await estoque.AjustarAsync(p.Id, TipoMovimentacao.Correcao, -2, "contagem");
        Conferir(banco);

        var compra = await compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(p.Id, 3, 1500, "M") }));
        Conferir(banco);

        var venda = await vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(p.Id, 2, "M") }, 0, FormaPagamento.Pix));
        Conferir(banco);

        await vendas.CancelarAsync(venda.Id, "cliente desistiu");
        Conferir(banco);

        await compras.CancelarAsync(compra.Id, "nota errada");
        Conferir(banco);
    }

    [Fact]
    public async Task Mesma_chave_de_venda_nao_grava_duas_vezes_nem_baixa_o_estoque_de_novo()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var vendas = new VendaService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1), 10);
        var pedido = new PedidoVenda(new[] { new ItemCarrinho(p.Id, 3) }, 0, FormaPagamento.Pix, ChaveUnica: Guid.NewGuid());

        var primeira = await vendas.FinalizarAsync(pedido);
        var repetida = await vendas.FinalizarAsync(pedido);

        Assert.Equal(primeira.Id, repetida.Id);
        using var db = banco.CriarContexto();
        Assert.Equal(1, db.Vendas.Count());
        Assert.Equal(7, db.Produtos.Single().EstoqueAtual);
        Conferir(banco);
    }

    [Fact]
    public async Task Chaves_diferentes_sao_vendas_diferentes()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var vendas = new VendaService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1), 10);

        var a = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix, ChaveUnica: Guid.NewGuid()));
        var b = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix, ChaveUnica: Guid.NewGuid()));

        Assert.NotEqual(a.Id, b.Id);
        using var db = banco.CriarContexto();
        Assert.Equal(8, db.Produtos.Single().EstoqueAtual);
    }

    [Fact]
    public async Task Movimentacao_de_ajuste_usa_o_relogio_injetado()
    {
        var agora = new DateTime(2026, 1, 2, 3, 4, 5);
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var estoque = new EstoqueService(banco.Fabrica, () => agora);
        var p = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 3000, 1000, 1), 0);

        await estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 4, null);

        var mov = Assert.Single(await estoque.ListarMovimentacoesAsync(p.Id), m => m.Tipo == TipoMovimentacao.Entrada && m.Quantidade == 4);
        Assert.Equal(agora, mov.DataHora);
    }
}
