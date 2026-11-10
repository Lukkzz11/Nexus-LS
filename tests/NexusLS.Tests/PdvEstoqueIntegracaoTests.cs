using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Conversa PDV x estoque nos dois sentidos, na mesma sessão (mesma fábrica de contexto, sem cache):
/// o que o estoque muda aparece na próxima consulta do PDV, e o que o PDV vende/devolve baixa o tamanho certo com histórico.
/// </summary>
public class PdvEstoqueIntegracaoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly VendaService _vendas;

    public PdvEstoqueIntegracaoTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _estoque = new EstoqueService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private async Task<Produto> CamisaAsync()
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto("Camisa", "5555", 5000, 2000, 0, Referencia: "5555"));
        await _produtos.AdicionarGradeAsync(p.Id, new[] { "P", "M" });
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 5, null, "P");
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 3, null, "M");
        return p;
    }

    private static PedidoVenda Pedido(int produtoId, int qtd, string tamanho) =>
        new(new[] { new ItemCarrinho(produtoId, qtd, tamanho) }, 0, FormaPagamento.Pix);

    [Fact]
    public async Task Venda_pelo_codigo_com_numero_do_tamanho_baixa_o_tamanho_certo_e_cancelamento_devolve()
    {
        var p = await CamisaAsync();
        var lido = await _produtos.ObterPorCodigoDeTamanhoAsync("55552"); // o leitor do PDV
        Assert.Equal("M", lido!.Value.Tamanho.Tamanho);

        var venda = await _vendas.FinalizarAsync(Pedido(lido.Value.Produto.Id, 2, lido.Value.Tamanho.Tamanho));

        var grade = await _produtos.ListarTamanhosAsync(p.Id);
        Assert.Equal(5, grade.Single(t => t.Tamanho == "P").Estoque);
        Assert.Equal(1, grade.Single(t => t.Tamanho == "M").Estoque);
        Assert.Equal(6, (await _produtos.ObterAsync(p.Id))!.EstoqueAtual);
        var mov = (await _estoque.ListarMovimentacoesAsync(p.Id)).First(m => m.VendaId == venda.Id);
        Assert.Equal((TipoMovimentacao.Venda, -2, "M"), (mov.Tipo, mov.Quantidade, mov.Tamanho));

        await _vendas.CancelarAsync(venda.Id, "cliente desistiu");
        grade = await _produtos.ListarTamanhosAsync(p.Id);
        Assert.Equal(3, grade.Single(t => t.Tamanho == "M").Estoque);
        Assert.Equal(8, (await _produtos.ObterAsync(p.Id))!.EstoqueAtual);
        Assert.Contains(await _estoque.ListarMovimentacoesAsync(p.Id),
            m => m.Tipo == TipoMovimentacao.CancelamentoVenda && m.Tamanho == "M" && m.Quantidade == 2 && !string.IsNullOrEmpty(m.Motivo));
    }

    [Fact]
    public async Task Mudancas_do_estoque_aparecem_no_PDV_na_hora()
    {
        var p = await CamisaAsync();

        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 4, null, "P");
        await _produtos.AdicionarTamanhoAsync(p.Id, "G");
        await _produtos.AplicarPrecoVendaAsync(p.Id, 6000);
        var grade = await _produtos.ListarTamanhosAsync(p.Id);
        Assert.Equal(9, grade.Single(t => t.Tamanho == "P").Estoque);
        Assert.Equal("55553", grade.Single(t => t.Tamanho == "G").CodigoBarras);
        Assert.Equal("G", (await _produtos.ObterPorCodigoDeTamanhoAsync("55553"))!.Value.Tamanho.Tamanho);
        Assert.Equal(6000, (await _produtos.EscolherParaVendaAsync("5555"))!.PrecoVendaCentavos);

        var novo = await _produtos.CadastrarAsync(new DadosProduto("Bermuda", null, 3000, 1000, 0));
        Assert.Contains(await _produtos.BuscarAsync("Bermuda"), x => x.Id == novo.Id);

        // sem baixa: vende além do estoque e não mexe nos saldos
        await _produtos.DefinirVenderSemBaixaAsync(new[] { p.Id }, true);
        await _vendas.FinalizarAsync(Pedido(p.Id, 50, "P"));
        Assert.Equal(9, (await _produtos.ListarTamanhosAsync(p.Id)).Single(t => t.Tamanho == "P").Estoque);

        // desativar some da busca, do leitor e da venda
        await _produtos.DesativarAsync(p.Id);
        Assert.Empty(await _produtos.BuscarAsync("Camisa"));
        Assert.Null(await _produtos.ObterPorCodigoDeTamanhoAsync("55551"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.FinalizarAsync(Pedido(p.Id, 1, "P")));
    }

    [Fact]
    public async Task Venda_alem_do_estoque_de_um_tamanho_e_recusada_mesmo_se_outro_tamanho_tem()
    {
        var p = await CamisaAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.FinalizarAsync(Pedido(p.Id, 4, "M")));
        await using var db = _banco.CriarContexto();
        Assert.Equal(8, (await db.Produtos.SingleAsync(x => x.Id == p.Id)).EstoqueAtual);
    }
}
