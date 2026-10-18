using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Produto "vender sem baixa": não confere nem baixa estoque, e o cancelamento não devolve o que nunca saiu.</summary>
public class VenderSemBaixaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;

    public VenderSemBaixaTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private Task<Produto> ServicoAsync(int estoque = 0) =>
        _produtos.CadastrarAsync(new DadosProduto("Conserto", null, 5000, 0, 0, VenderSemBaixa: true), estoque);

    private Task<Produto> NormalAsync(int estoque) =>
        _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0), estoque);

    private static PedidoVenda Pix(params ItemCarrinho[] itens) => new(itens, 0, FormaPagamento.Pix);

    [Fact]
    public async Task Vende_mesmo_com_estoque_zero_e_nao_baixa_nem_cria_movimentacao()
    {
        var servico = await ServicoAsync(estoque: 0);

        var venda = await _vendas.FinalizarAsync(Pix(new ItemCarrinho(servico.Id, 3)));

        Assert.Equal(15000, venda.TotalCentavos);
        Assert.False(venda.Itens.Single().BaixouEstoque);
        using var db = _banco.CriarContexto();
        Assert.Equal(0, db.Produtos.Single().EstoqueAtual);
        Assert.Empty(db.Movimentacoes.Where(m => m.Tipo == TipoMovimentacao.Venda));
    }

    [Fact]
    public async Task Produto_normal_continua_exigindo_estoque_na_mesma_venda()
    {
        var servico = await ServicoAsync();
        var bone = await NormalAsync(1);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _vendas.FinalizarAsync(Pix(new ItemCarrinho(servico.Id, 1), new ItemCarrinho(bone.Id, 2))));
    }

    [Fact]
    public async Task Venda_mista_baixa_so_o_produto_normal()
    {
        var servico = await ServicoAsync();
        var bone = await NormalAsync(5);

        await _vendas.FinalizarAsync(Pix(new ItemCarrinho(servico.Id, 2), new ItemCarrinho(bone.Id, 2)));

        using var db = _banco.CriarContexto();
        Assert.Equal(3, db.Produtos.Single(p => p.Id == bone.Id).EstoqueAtual);
        Assert.Equal(0, db.Produtos.Single(p => p.Id == servico.Id).EstoqueAtual);
    }

    [Fact]
    public async Task Cancelar_nao_devolve_estoque_de_item_que_nao_baixou()
    {
        var servico = await ServicoAsync(estoque: 4);          // tem estoque, mas vende sem baixa
        var venda = await _vendas.FinalizarAsync(Pix(new ItemCarrinho(servico.Id, 2)));

        await _vendas.CancelarAsync(venda.Id, "Engano");

        using var db = _banco.CriarContexto();
        Assert.Equal(4, db.Produtos.Single().EstoqueAtual);     // continua 4, não vira 6
        Assert.Empty(db.Movimentacoes.Where(m => m.Tipo == TipoMovimentacao.CancelamentoVenda));
    }

    [Fact]
    public async Task Mudar_a_opcao_depois_nao_estraga_vendas_antigas()
    {
        var bone = await NormalAsync(5);
        var venda = await _vendas.FinalizarAsync(Pix(new ItemCarrinho(bone.Id, 2)));     // baixou: 3 sobraram
        await _produtos.EditarAsync(bone.Id, new DadosProduto("Boné", null, 2000, 800, 0, VenderSemBaixa: true));

        await _vendas.CancelarAsync(venda.Id, "Engano");

        using var db = _banco.CriarContexto();
        Assert.Equal(5, db.Produtos.Single().EstoqueAtual);     // a venda antiga baixou, então devolve
    }

    [Fact]
    public async Task A_opcao_e_salva_ao_cadastrar_e_ao_editar()
    {
        var p = await ServicoAsync();
        Assert.True(p.VenderSemBaixa);

        await _produtos.EditarAsync(p.Id, new DadosProduto("Conserto", null, 5000, 0, 0, VenderSemBaixa: false));
        Assert.False((await _produtos.BuscarAsync("Conserto")).Single().VenderSemBaixa);
    }

    [Fact]
    public async Task Configuracao_guarda_se_pergunta_o_tamanho()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        Assert.True((await config.ObterAsync()).PerguntarTamanho);   // padrão: pergunta

        await config.SalvarAsync(new DadosLoja("Loja", PerguntarTamanho: false));

        Assert.False((await config.ObterAsync()).PerguntarTamanho);
    }
}
