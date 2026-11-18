using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Movel.Tests;

public class ConsultaDeProdutoTests
{
    private static ConsultaDeProduto Consulta(BancoDeTeste banco) => new(new ProdutoService(banco.Fabrica));

    [Fact]
    public async Task Codigo_do_tamanho_abre_o_produto_com_o_tamanho_marcado()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "CAMISA GOLA CARECA", "1019", 5990, 3990, null,
            ("P", 1, 4), ("M", 2, 7), ("G", 3, 0));

        var resposta = await Consulta(banco).ConsultarAsync("10192");

        Assert.True(resposta.Encontrado);
        Assert.Equal("CAMISA GOLA CARECA", resposta.Produto!.Nome);
        Assert.Equal("M", resposta.Produto.TamanhoSelecionado);

        var marcado = Assert.Single(resposta.Produto.Tamanhos, t => t.Selecionado);
        Assert.Equal("M", marcado.Nome);
        Assert.Equal(7, marcado.Estoque);
        Assert.Equal("10192", marcado.Codigo);

        Assert.Equal("10193", resposta.Produto.Tamanhos.Single(t => t.Nome == "G").Codigo);
    }

    [Fact]
    public async Task Referencia_encontra_o_produto_sem_marcar_tamanho()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "CALCA JOGGER", "1502", 10000, 0, null,
            ("P", 1, 3), ("M", 2, 5));

        var resposta = await Consulta(banco).ConsultarAsync("1502");

        Assert.True(resposta.Encontrado);
        Assert.Equal("CALCA JOGGER", resposta.Produto!.Nome);
        Assert.Null(resposta.Produto.TamanhoSelecionado);
        Assert.Equal(8, resposta.Produto.EstoqueTotal);
    }

    [Fact]
    public async Task Codigo_de_barras_exato_encontra_o_produto()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "BERMUDA CARGO", "1402", 7000, 6000, "7891234567890",
            ("42", 1, 6), ("44", 2, 8));

        var resposta = await Consulta(banco).ConsultarAsync("7891234567890");

        Assert.True(resposta.Encontrado);
        Assert.Equal("BERMUDA CARGO", resposta.Produto!.Nome);
        Assert.Equal(7000, resposta.Produto.PrecoVarejoCentavos);
        Assert.Equal(6000, resposta.Produto.PrecoAtacadoCentavos);
    }

    [Fact]
    public async Task Produto_desativado_nao_aparece()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "PECA DESATIVADA", "9999", 1000, 0, null, ("M", 2, 3));

        using (var db = banco.CriarContexto())
        {
            var produto = db.Produtos.Single();
            produto.Ativo = false;
            db.SaveChanges();
        }

        var resposta = await Consulta(banco).ConsultarAsync("9999");

        Assert.False(resposta.Encontrado);
        Assert.Empty(resposta.Sugestoes);
    }

    [Fact]
    public async Task Nome_com_varios_resultados_vira_sugestoes()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "CAMISA DRYFIT", "1016", 4990, 0, null, ("M", 2, 5));
        DadosDeTeste.SemearProduto(banco, "CAMISA DRYFIT IMPORTADA", "8606", 6990, 0, null, ("M", 2, 5));

        var resposta = await Consulta(banco).ConsultarAsync("camisa");

        Assert.False(resposta.Encontrado);
        Assert.Equal(2, resposta.Sugestoes.Count);
        Assert.Contains(resposta.Sugestoes, s => s.Referencia == "1016");
        Assert.Contains(resposta.Sugestoes, s => s.Referencia == "8606");
        Assert.NotEmpty(resposta.Mensagem);
    }

    [Fact]
    public async Task Nome_com_um_unico_resultado_abre_o_produto()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "CINTO DE COURO", "2001", 4500, 0, null, ("Unico", 0, 2));

        var resposta = await Consulta(banco).ConsultarAsync("cinto");

        Assert.True(resposta.Encontrado);
        Assert.Equal("CINTO DE COURO", resposta.Produto!.Nome);
    }

    [Fact]
    public async Task Atacado_sem_preco_usa_o_de_varejo()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "MEIA", "3001", 1500, 0, null, ("Unico", 0, 10));

        var resposta = await Consulta(banco).ConsultarAsync("3001");

        Assert.True(resposta.Encontrado);
        Assert.Equal(1500, resposta.Produto!.PrecoAtacadoCentavos);
    }

    [Fact]
    public async Task Codigo_vazio_pede_um_codigo()
    {
        using var banco = new BancoDeTeste();

        var resposta = await Consulta(banco).ConsultarAsync("   ");

        Assert.False(resposta.Encontrado);
        Assert.Null(resposta.Produto);
        Assert.Equal("Digite ou leia um código.", resposta.Mensagem);
    }

    [Fact]
    public async Task Busca_curta_nao_devolve_a_loja_inteira()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "CALCA BRIM", "1511", 11500, 0, null, ("42", 1, 10));

        var itens = await Consulta(banco).BuscarAsync("c");

        Assert.Empty(itens);
    }
}
