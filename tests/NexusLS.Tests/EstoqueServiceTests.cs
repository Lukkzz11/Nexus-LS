using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class EstoqueServiceTests
{
    private static async Task<(BancoDeTeste banco, EstoqueService estoque, int id)> Preparar(int inicial = 5, int minimo = 3)
    {
        var banco = new BancoDeTeste();
        var p = await new ProdutoService(banco.Fabrica)
            .CadastrarAsync(new DadosProduto("Café", null, 1000, 600, minimo), inicial);
        return (banco, new EstoqueService(banco.Fabrica), p.Id);
    }

    [Fact]
    public async Task Entrada_soma_e_registra_movimentacao()
    {
        var (banco, estoque, id) = await Preparar();
        using var _ = banco;

        var p = await estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 4, "Compra");

        Assert.Equal(9, p.EstoqueAtual);
        var movs = await estoque.ListarMovimentacoesAsync(id);
        Assert.Equal(2, movs.Count); // estoque inicial + entrada
        Assert.Equal(4, movs[0].Quantidade);
    }

    [Fact]
    public async Task Perda_subtrai_e_grava_quantidade_negativa()
    {
        var (banco, estoque, id) = await Preparar();
        using var _ = banco;

        var p = await estoque.AjustarAsync(id, TipoMovimentacao.Perda, 2, "Venceu");

        Assert.Equal(3, p.EstoqueAtual);
        Assert.Equal(-2, (await estoque.ListarMovimentacoesAsync(id))[0].Quantidade);
    }

    [Fact]
    public async Task Correcao_aceita_sinal_positivo_e_negativo()
    {
        var (banco, estoque, id) = await Preparar();
        using var _ = banco;

        await estoque.AjustarAsync(id, TipoMovimentacao.Correcao, -1, "Contagem");
        var p = await estoque.AjustarAsync(id, TipoMovimentacao.Correcao, 3, "Contagem");

        Assert.Equal(7, p.EstoqueAtual);
    }

    [Fact]
    public async Task Nao_permite_estoque_negativo_e_nao_grava_nada()
    {
        var (banco, estoque, id) = await Preparar(inicial: 2);
        using var _ = banco;

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => estoque.AjustarAsync(id, TipoMovimentacao.Perda, 3, "Quebrou"));

        Assert.Contains("insuficiente", erro.Message);
        using var db = banco.CriarContexto();
        Assert.Equal(2, db.Produtos.Single().EstoqueAtual);
        Assert.Single(db.Movimentacoes); // só o estoque inicial
    }

    [Theory]
    [InlineData(TipoMovimentacao.Entrada, 0)]
    [InlineData(TipoMovimentacao.Entrada, -1)]
    [InlineData(TipoMovimentacao.Perda, 0)]
    [InlineData(TipoMovimentacao.Correcao, 0)]
    [InlineData(TipoMovimentacao.Venda, 1)]
    public async Task Quantidades_e_tipos_invalidos_falham(TipoMovimentacao tipo, int qtd)
    {
        var (banco, estoque, id) = await Preparar();
        using var _ = banco;
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => estoque.AjustarAsync(id, tipo, qtd, "x"));
    }

    [Theory]
    [InlineData(TipoMovimentacao.Perda)]
    [InlineData(TipoMovimentacao.Correcao)]
    public async Task Perda_e_correcao_exigem_motivo(TipoMovimentacao tipo)
    {
        var (banco, estoque, id) = await Preparar();
        using var _ = banco;
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => estoque.AjustarAsync(id, tipo, 1, "  "));
    }

    [Fact]
    public async Task Lista_abaixo_do_minimo_ignora_inativos()
    {
        var (banco, estoque, id) = await Preparar(inicial: 1, minimo: 3);
        using var _ = banco;
        var produtos = new ProdutoService(banco.Fabrica);
        await produtos.CadastrarAsync(new DadosProduto("Cheio", null, 100, 50, 2), 10);
        var inativo = await produtos.CadastrarAsync(new DadosProduto("Inativo", null, 100, 50, 5), 0);
        await produtos.DesativarAsync(inativo.Id);

        var baixos = await estoque.ListarAbaixoDoMinimoAsync();

        Assert.Equal(id, Assert.Single(baixos).Id);
    }
}
