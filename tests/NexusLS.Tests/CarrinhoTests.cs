using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

/// <summary>O carrinho não usa banco nem janela: estes testes rodam só com objetos na memória.</summary>
public class CarrinhoTests
{
    private static Produto Camiseta() => new() { Id = 1, Nome = "Camiseta", PrecoVendaCentavos = 5000, PrecoAtacadoCentavos = 3500, Ativo = true };
    private static Produto Bone() => new() { Id = 2, Nome = "Boné", PrecoVendaCentavos = 2000, Ativo = true };
    private static ProdutoTamanho Tam(Produto p, string tamanho, int estoque) => new() { ProdutoId = p.Id, Tamanho = tamanho, Estoque = estoque };

    // ---------- Adicionar ----------

    [Fact]
    public void Adicionar_cria_linha_e_soma_no_mesmo_produto_e_tamanho()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        var m = Tam(camiseta, "M", 10);

        c.Adicionar(camiseta, m, 2);
        c.Adicionar(camiseta, m, 3);

        var linha = Assert.Single(c.Linhas);
        Assert.Equal(5, linha.Quantidade);
        Assert.Equal(25000, c.SubtotalCentavos);
    }

    [Fact]
    public void Tamanhos_diferentes_ficam_em_linhas_separadas()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();

        c.Adicionar(camiseta, Tam(camiseta, "P", 5), 1);
        c.Adicionar(camiseta, Tam(camiseta, "M", 5), 1);

        Assert.Equal(2, c.Linhas.Count);
        Assert.Equal(2, c.QuantidadeTotal);
    }

    [Fact]
    public void Nao_passa_do_estoque_do_tamanho_somando_o_que_ja_esta_no_carrinho()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        var p = Tam(camiseta, "P", 3);
        c.Adicionar(camiseta, p, 2);

        var erro = Assert.Throws<RegraDeNegocioException>(() => c.Adicionar(camiseta, p, 2));

        Assert.Contains("tamanho P", erro.Message);
        Assert.Equal(2, c.Linhas.Single().Quantidade); // nada mudou
    }

    [Fact]
    public void Tamanho_sem_estoque_e_produto_desativado_nao_entram()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        Assert.Throws<RegraDeNegocioException>(() => c.Adicionar(camiseta, Tam(camiseta, "G", 0), 1));

        camiseta.Ativo = false;
        Assert.Throws<RegraDeNegocioException>(() => c.Adicionar(camiseta, Tam(camiseta, "M", 5), 1));
        Assert.True(c.Vazio);
    }

    [Fact]
    public void Tamanho_de_outro_produto_e_quantidade_zero_sao_recusados()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        var bone = Bone();
        Assert.Throws<RegraDeNegocioException>(() => c.Adicionar(camiseta, Tam(bone, "M", 5), 1));
        Assert.Throws<RegraDeNegocioException>(() => c.Adicionar(camiseta, Tam(camiseta, "M", 5), 0));
    }

    // ---------- Quantidade ----------

    [Fact]
    public void Altera_a_quantidade_dentro_do_estoque()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        var linha = c.Adicionar(camiseta, Tam(camiseta, "M", 4), 1);

        c.AlterarQuantidade(linha, 4);
        Assert.Equal(4, linha.Quantidade);

        Assert.Throws<RegraDeNegocioException>(() => c.AlterarQuantidade(linha, 5));
        Assert.Throws<RegraDeNegocioException>(() => c.AlterarQuantidade(linha, 0));
        Assert.Equal(4, linha.Quantidade);
    }

    [Fact]
    public void Quantidade_considera_as_outras_linhas_do_mesmo_tamanho()
    {
        var c = new Carrinho();
        var bone = Bone();
        var sem = Tam(bone, Tamanhos.SemTamanho, 5);
        var a = c.Adicionar(bone, sem, 2);
        c.DefinirTotalLinha(a, 3000);               // linha com total editado não recebe somas...
        var b = c.Adicionar(bone, sem, 2);          // ...então esta é uma linha nova

        Assert.Equal(2, c.Linhas.Count);
        Assert.Throws<RegraDeNegocioException>(() => c.AlterarQuantidade(b, 4)); // 2 (outra linha) + 4 > 5
        c.AlterarQuantidade(b, 3);
        Assert.Equal(5, c.QuantidadeDe(bone.Id, Tamanhos.SemTamanho));
    }

    // ---------- Preço editado ----------

    [Fact]
    public void Preco_unitario_editado_vale_para_todas_as_unidades()
    {
        var c = new Carrinho();
        var bone = Bone();
        var linha = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 10), 3);

        c.DefinirPrecoUnitario(linha, 1500);

        Assert.True(linha.PrecoEditado);
        Assert.Equal(1500, linha.PrecoUnitarioCentavos);
        Assert.Equal(4500, linha.TotalCentavos);
        Assert.Equal(4500, c.SubtotalCentavos);
    }

    [Fact]
    public void Total_da_linha_editado_nao_perde_centavo_e_o_unitario_e_arredondado()
    {
        var c = new Carrinho();
        var bone = Bone();
        var linha = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 10), 3);

        c.DefinirTotalLinha(linha, 1000);

        Assert.Equal(1000, linha.TotalCentavos);
        Assert.Equal(333, linha.PrecoUnitarioCentavos);
        Assert.Equal(1000, c.SubtotalCentavos);
    }

    [Fact]
    public void Editar_o_unitario_apaga_a_edicao_do_total_e_vice_versa()
    {
        var c = new Carrinho();
        var bone = Bone();
        var linha = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 10), 2);

        c.DefinirTotalLinha(linha, 3000);
        c.DefinirPrecoUnitario(linha, 1000);
        Assert.Null(linha.TotalEditadoCentavos);
        Assert.Equal(2000, linha.TotalCentavos);

        c.DefinirTotalLinha(linha, 3000);
        Assert.Null(linha.PrecoUnitarioEditadoCentavos);
    }

    [Fact]
    public void Mudar_a_quantidade_de_linha_com_total_editado_mantem_o_preco_da_unidade()
    {
        var c = new Carrinho();
        var bone = Bone();
        var linha = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 10), 2);
        c.DefinirTotalLinha(linha, 3000); // R$ 15,00 cada

        c.AlterarQuantidade(linha, 4);

        Assert.Null(linha.TotalEditadoCentavos);
        Assert.Equal(1500, linha.PrecoUnitarioCentavos);
        Assert.Equal(6000, linha.TotalCentavos);
    }

    [Fact]
    public void Restaurar_volta_ao_preco_da_tabela()
    {
        var c = new Carrinho();
        var bone = Bone();
        var linha = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 10), 2);
        c.DefinirPrecoUnitario(linha, 1);

        c.RestaurarPreco(linha);

        Assert.False(linha.PrecoEditado);
        Assert.Equal(4000, linha.TotalCentavos);
    }

    [Fact]
    public void Preco_negativo_e_recusado_e_zero_e_permitido()
    {
        var c = new Carrinho();
        var bone = Bone();
        var linha = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 10), 1);

        Assert.Throws<RegraDeNegocioException>(() => c.DefinirPrecoUnitario(linha, -1));
        Assert.Throws<RegraDeNegocioException>(() => c.DefinirTotalLinha(linha, -1));
        c.DefinirPrecoUnitario(linha, 0);
        Assert.Equal(0, c.SubtotalCentavos);
    }

    // ---------- Tabela de preço ----------

    [Fact]
    public void Mudar_para_atacado_reprecifica_as_linhas_mas_respeita_o_preco_editado()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        var bone = Bone();
        var a = c.Adicionar(camiseta, Tam(camiseta, "M", 10), 2);
        var b = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 10), 1);
        var editada = c.Adicionar(camiseta, Tam(camiseta, "P", 10), 1);
        c.DefinirPrecoUnitario(editada, 4000);

        c.MudarTabela(TabelaPreco.Atacado);

        Assert.Equal(3500, a.PrecoUnitarioCentavos);   // atacado da camiseta
        Assert.Equal(2000, b.PrecoUnitarioCentavos);   // boné sem atacado: varejo
        Assert.Equal(4000, editada.PrecoUnitarioCentavos);
        Assert.Equal(2 * 3500 + 2000 + 4000, c.SubtotalCentavos);

        c.MudarTabela(TabelaPreco.Varejo);
        Assert.Equal(5000, a.PrecoUnitarioCentavos);
    }

    [Fact]
    public void Linha_nova_nasce_na_tabela_atual()
    {
        var c = new Carrinho();
        c.MudarTabela(TabelaPreco.Atacado);
        var camiseta = Camiseta();

        var linha = c.Adicionar(camiseta, Tam(camiseta, "M", 5), 1);

        Assert.Equal(3500, linha.PrecoUnitarioCentavos);
    }

    // ---------- Remover, esvaziar e pedido ----------

    [Fact]
    public void Remove_linha_e_esvazia()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        var bone = Bone();
        var a = c.Adicionar(camiseta, Tam(camiseta, "M", 5), 1);
        c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 5), 1);

        c.Remover(a);
        Assert.Single(c.Linhas);
        Assert.Throws<RegraDeNegocioException>(() => c.Remover(a)); // já saiu

        c.Limpar();
        Assert.True(c.Vazio);
        Assert.Equal(0, c.SubtotalCentavos);
    }

    [Fact]
    public void ParaPedido_leva_tamanho_e_precos_editados()
    {
        var c = new Carrinho();
        var camiseta = Camiseta();
        var bone = Bone();
        c.Adicionar(camiseta, Tam(camiseta, "M", 5), 2);
        var editada = c.Adicionar(bone, Tam(bone, Tamanhos.SemTamanho, 5), 3);
        c.DefinirTotalLinha(editada, 5000);

        var pedido = c.ParaPedido();

        Assert.Equal(new ItemCarrinho(1, 2, "M", null, null), pedido[0]);
        Assert.Equal(new ItemCarrinho(2, 3, Tamanhos.SemTamanho, null, 5000), pedido[1]);
    }

    [Fact]
    public async Task O_carrinho_vira_uma_venda_com_o_mesmo_total()
    {
        using var banco = new Infra.BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1, PrecoAtacadoCentavos: 3500));
        await produtos.AdicionarTamanhoAsync(camiseta.Id, "M");
        await new EstoqueService(banco.Fabrica).AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 10, null, "M");
        var grade = await produtos.ListarTamanhosAsync(camiseta.Id);
        var produto = (await produtos.BuscarAsync("Camiseta")).Single();

        var c = new Carrinho();
        c.MudarTabela(TabelaPreco.Atacado);
        var linha = c.Adicionar(produto, grade.Single(t => t.Tamanho == "M"), 3);
        c.DefinirTotalLinha(linha, 9000);

        var venda = await new VendaService(banco.Fabrica).FinalizarAsync(new PedidoVenda(
            c.ParaPedido(), 0, FormaPagamento.Pix, TabelaPreco: c.Tabela));

        Assert.Equal(c.SubtotalCentavos, venda.TotalCentavos);
        Assert.Equal(9000, venda.TotalCentavos);
    }
}
