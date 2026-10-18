using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class ProdutoServiceTests
{
    private static DadosProduto Dados(string nome = "Café", string? codigo = null) =>
        new(nome, codigo, 1250, 800, 3);

    [Fact]
    public async Task Cadastrar_com_estoque_inicial_cria_movimentacao_de_entrada()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);

        var p = await servico.CadastrarAsync(Dados(), estoqueInicial: 10);

        using var db = banco.CriarContexto();
        Assert.Equal(10, db.Produtos.Single().EstoqueAtual);
        var mov = db.Movimentacoes.Single();
        Assert.Equal(TipoMovimentacao.Entrada, mov.Tipo);
        Assert.Equal(10, mov.Quantidade);
        Assert.Equal(p.Id, mov.ProdutoId);
    }

    [Fact]
    public async Task Cadastrar_sem_nome_falha()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CadastrarAsync(Dados(nome: "  ")));
    }

    [Fact]
    public async Task Cadastrar_com_codigo_repetido_falha_com_mensagem_amigavel()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);
        await servico.CadastrarAsync(Dados("A", "789"));

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CadastrarAsync(Dados("B", "789")));
        Assert.Contains("código de barras", erro.Message);
    }

    [Fact]
    public async Task Editar_nao_mexe_no_estoque_e_aceita_o_proprio_codigo()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);
        var p = await servico.CadastrarAsync(Dados("A", "789"), 5);

        await servico.EditarAsync(p.Id, Dados("A novo", "789") with { PrecoVendaCentavos = 2000 });

        using var db = banco.CriarContexto();
        var salvo = db.Produtos.Single();
        Assert.Equal("A novo", salvo.Nome);
        Assert.Equal(2000, salvo.PrecoVendaCentavos);
        Assert.Equal(5, salvo.EstoqueAtual);
    }

    [Fact]
    public async Task Desativado_some_da_busca_mas_continua_no_banco()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);
        var p = await servico.CadastrarAsync(Dados("Suco"));

        await servico.DesativarAsync(p.Id);

        Assert.Empty(await servico.BuscarAsync("Suco"));
        Assert.Single(await servico.BuscarAsync("Suco", incluirInativos: true));
    }

    [Fact]
    public async Task Busca_por_nome_ou_codigo()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);
        await servico.CadastrarAsync(Dados("Café Torrado", "111"));
        await servico.CadastrarAsync(Dados("Açúcar", "222"));

        Assert.Single(await servico.BuscarAsync("torrado"));
        Assert.Single(await servico.BuscarAsync("222"));
        Assert.Equal(2, (await servico.BuscarAsync("")).Count);
        Assert.Equal("Açúcar", (await servico.ObterPorCodigoBarrasAsync("222"))!.Nome);
    }
}
