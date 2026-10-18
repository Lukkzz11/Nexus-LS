using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class GradeTamanhosTests
{
    private static DadosProduto Dados(string nome = "Camiseta") => new(nome, null, 5000, 2000, 2);

    private static async Task<(BancoDeTeste banco, ProdutoService produtos, EstoqueService estoque, int id)> Preparar(int inicial = 0)
    {
        var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(Dados(), inicial);
        return (banco, produtos, new EstoqueService(banco.Fabrica), p.Id);
    }

    /// <summary>Regra de ouro: o estoque total do produto é sempre a soma dos tamanhos.</summary>
    private static void ConferirTotal(BancoDeTeste banco)
    {
        using var db = banco.CriarContexto();
        foreach (var p in db.Produtos.ToList())
            Assert.Equal(db.TamanhosProduto.Where(t => t.ProdutoId == p.Id).Sum(t => t.Estoque), p.EstoqueAtual);
    }

    // ---------- Grade ----------

    [Fact]
    public async Task Todo_produto_nasce_com_a_linha_Sem_tamanho_com_o_estoque_inicial()
    {
        var (banco, produtos, _, id) = await Preparar(inicial: 6);
        using var _ = banco;

        var grade = await produtos.ListarTamanhosAsync(id);

        var linha = Assert.Single(grade);
        Assert.Equal(Tamanhos.SemTamanho, linha.Tamanho);
        Assert.Equal(6, linha.Estoque);
        ConferirTotal(banco);
    }

    [Fact]
    public async Task Adiciona_tamanhos_em_maiusculas_na_ordem_de_cadastro()
    {
        var (banco, produtos, _, id) = await Preparar();
        using var _ = banco;

        await produtos.AdicionarTamanhoAsync(id, " p ");
        await produtos.AdicionarTamanhoAsync(id, "m");
        await produtos.AdicionarTamanhoAsync(id, "38");

        var nomes = (await produtos.ListarTamanhosAsync(id)).Select(t => t.Tamanho).ToList();
        Assert.Equal(new[] { Tamanhos.SemTamanho, "P", "M", "38" }, nomes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sem tamanho")]
    [InlineData("Sem Tamanho")]
    public async Task Tamanho_vazio_ou_reservado_e_recusado(string nome)
    {
        var (banco, produtos, _, id) = await Preparar();
        using var _ = banco;
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => produtos.AdicionarTamanhoAsync(id, nome));
    }

    [Fact]
    public async Task Tamanho_repetido_e_recusado_mesmo_com_outra_caixa()
    {
        var (banco, produtos, _, id) = await Preparar();
        using var _ = banco;
        await produtos.AdicionarTamanhoAsync(id, "M");

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => produtos.AdicionarTamanhoAsync(id, "m"));
        Assert.Contains("já tem", erro.Message);
    }

    [Fact]
    public async Task Dois_produtos_podem_ter_o_mesmo_tamanho()
    {
        var (banco, produtos, _, id) = await Preparar();
        using var _ = banco;
        var outro = await produtos.CadastrarAsync(Dados("Calça"));

        await produtos.AdicionarTamanhoAsync(id, "M");
        await produtos.AdicionarTamanhoAsync(outro.Id, "M");

        Assert.Equal(2, (await produtos.ListarTamanhosAsync(outro.Id)).Count);
    }

    [Fact]
    public async Task Remover_tamanho_so_com_estoque_zerado_e_nunca_o_Sem_tamanho()
    {
        var (banco, produtos, estoque, id) = await Preparar(inicial: 3);
        using var _ = banco;
        await produtos.AdicionarTamanhoAsync(id, "M");
        await estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 2, null, "M");

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => produtos.RemoverTamanhoAsync(id, "M")); // tem 2
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => produtos.RemoverTamanhoAsync(id, Tamanhos.SemTamanho));

        await estoque.AjustarAsync(id, TipoMovimentacao.Perda, 2, "Zerar", "M");
        await produtos.RemoverTamanhoAsync(id, "m");

        Assert.Single(await produtos.ListarTamanhosAsync(id));
        ConferirTotal(banco);
    }

    // ---------- Estoque por tamanho ----------

    [Fact]
    public async Task Entrada_no_tamanho_soma_no_tamanho_e_no_total_e_registra_o_tamanho()
    {
        var (banco, produtos, estoque, id) = await Preparar(inicial: 1);
        using var _ = banco;
        await produtos.AdicionarTamanhoAsync(id, "M");

        var p = await estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 5, "Compra", "m");

        Assert.Equal(6, p.EstoqueAtual);
        var m = (await produtos.ListarTamanhosAsync(id)).Single(t => t.Tamanho == "M");
        Assert.Equal(5, m.Estoque);
        var mov = (await estoque.ListarMovimentacoesAsync(id)).First();
        Assert.Equal("M", mov.Tamanho);
        ConferirTotal(banco);
    }

    [Fact]
    public async Task Perda_maior_que_o_estoque_do_tamanho_falha_mesmo_com_total_suficiente()
    {
        var (banco, produtos, estoque, id) = await Preparar(inicial: 10); // 10 em "Sem tamanho"
        using var _ = banco;
        await produtos.AdicionarTamanhoAsync(id, "M");
        await estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 2, null, "M");

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => estoque.AjustarAsync(id, TipoMovimentacao.Perda, 3, "Rasgou", "M"));

        Assert.Contains("tamanho M", erro.Message);
        ConferirTotal(banco);
        using var db = banco.CriarContexto();
        Assert.Equal(12, db.Produtos.Single().EstoqueAtual); // nada mudou
    }

    [Fact]
    public async Task Ajuste_em_tamanho_que_o_produto_nao_tem_falha()
    {
        var (banco, _, estoque, id) = await Preparar();
        using var _ = banco;
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 1, null, "GG"));
        Assert.Contains("não tem o tamanho", erro.Message);
    }

    [Fact]
    public async Task Ajuste_sem_informar_tamanho_usa_Sem_tamanho()
    {
        var (banco, produtos, estoque, id) = await Preparar(inicial: 4);
        using var _ = banco;

        await estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 1, null);

        var linha = (await produtos.ListarTamanhosAsync(id)).Single();
        Assert.Equal(5, linha.Estoque);
        ConferirTotal(banco);
    }

    // ---------- Preços e busca ----------

    [Fact]
    public void Preco_de_atacado_zerado_cai_no_varejo()
    {
        var sem = new Produto { PrecoVendaCentavos = 5000, PrecoAtacadoCentavos = 0 };
        var com = new Produto { PrecoVendaCentavos = 5000, PrecoAtacadoCentavos = 3500 };

        Assert.Equal(5000, sem.PrecoPara(TabelaPreco.Atacado));
        Assert.Equal(5000, com.PrecoPara(TabelaPreco.Varejo));
        Assert.Equal(3500, com.PrecoPara(TabelaPreco.Atacado));
    }

    [Fact]
    public async Task Cadastra_com_referencia_e_preco_de_atacado_e_busca_pela_referencia()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        await produtos.CadastrarAsync(Dados("Camiseta") with { Referencia = " REF-123 ", PrecoAtacadoCentavos = 3500 });
        await produtos.CadastrarAsync(Dados("Bermuda"));

        var achados = await produtos.BuscarAsync("ref-12");

        var p = Assert.Single(achados);
        Assert.Equal("REF-123", p.Referencia);
        Assert.Equal(3500, p.PrecoAtacadoCentavos);
    }

    [Fact]
    public async Task Preco_de_atacado_negativo_e_recusado()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => produtos.CadastrarAsync(Dados() with { PrecoAtacadoCentavos = -1 }));
    }

    // ---------- Venda convivendo com a grade (por enquanto sai de "Sem tamanho") ----------

    [Fact]
    public async Task Venda_baixa_a_linha_Sem_tamanho_e_o_total_e_cancelar_devolve()
    {
        var (banco, produtos, estoque, id) = await Preparar(inicial: 5);
        using var _ = banco;
        await produtos.AdicionarTamanhoAsync(id, "M");
        await estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 4, null, "M");
        var vendas = new VendaService(banco.Fabrica);

        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(id, 2) }, 0, FormaPagamento.Pix));

        var grade = await produtos.ListarTamanhosAsync(id);
        Assert.Equal(3, grade.Single(t => t.EhSemTamanho).Estoque);
        Assert.Equal(4, grade.Single(t => t.Tamanho == "M").Estoque);
        Assert.Equal(Tamanhos.SemTamanho, Assert.Single(venda.Itens).Tamanho);
        ConferirTotal(banco);

        await vendas.CancelarAsync(venda.Id, "Teste");

        Assert.Equal(5, (await produtos.ListarTamanhosAsync(id)).Single(t => t.EhSemTamanho).Estoque);
        ConferirTotal(banco);
    }

    [Fact]
    public async Task Venda_nao_usa_o_estoque_de_outros_tamanhos()
    {
        var (banco, produtos, estoque, id) = await Preparar(inicial: 0); // "Sem tamanho" = 0
        using var _ = banco;
        await produtos.AdicionarTamanhoAsync(id, "M");
        await estoque.AjustarAsync(id, TipoMovimentacao.Entrada, 9, null, "M");
        var vendas = new VendaService(banco.Fabrica);

        await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(id, 1) }, 0, FormaPagamento.Pix)));
        ConferirTotal(banco);
    }
}
