using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Tamanho, tabela de preço, preço editado, desconto em %, parcelas e venda a prazo.</summary>
public class VendaRegrasNovasTests
{
    private sealed class Cenario : IDisposable
    {
        public BancoDeTeste Banco { get; } = new();
        public ProdutoService Produtos { get; }
        public EstoqueService Estoque { get; }
        public ClienteService Clientes { get; }
        public VendaService Vendas { get; }

        /// <summary>Camiseta: varejo R$ 50,00, atacado R$ 35,00. Sem tamanho: 2; P: 3; M: 5.</summary>
        public int CamisetaId { get; private set; }

        /// <summary>Boné: varejo R$ 20,00, sem preço de atacado. Sem tamanho: 10.</summary>
        public int BoneId { get; private set; }

        public Cenario()
        {
            Produtos = new ProdutoService(Banco.Fabrica);
            Estoque = new EstoqueService(Banco.Fabrica);
            Clientes = new ClienteService(Banco.Fabrica);
            Vendas = new VendaService(Banco.Fabrica);
        }

        public static async Task<Cenario> CriarAsync()
        {
            var c = new Cenario();
            c.CamisetaId = (await c.Produtos.CadastrarAsync(
                new DadosProduto("Camiseta", null, 5000, 2000, 1, PrecoAtacadoCentavos: 3500), 2)).Id;
            await c.Produtos.AdicionarTamanhoAsync(c.CamisetaId, "P");
            await c.Produtos.AdicionarTamanhoAsync(c.CamisetaId, "M");
            await c.Estoque.AjustarAsync(c.CamisetaId, TipoMovimentacao.Entrada, 3, null, "P");
            await c.Estoque.AjustarAsync(c.CamisetaId, TipoMovimentacao.Entrada, 5, null, "M");
            c.BoneId = (await c.Produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 10)).Id;
            return c;
        }

        public int EstoqueDe(int produtoId, string tamanho)
        {
            using var db = Banco.CriarContexto();
            return db.TamanhosProduto.Single(t => t.ProdutoId == produtoId && t.Tamanho == tamanho).Estoque;
        }

        public int EstoqueTotal(int produtoId)
        {
            using var db = Banco.CriarContexto();
            return db.Produtos.Single(p => p.Id == produtoId).EstoqueAtual;
        }

        public void Dispose() => Banco.Dispose();
    }

    private static ItemCarrinho I(int produtoId, int quantidade, string? tamanho = null, long? PrecoUnitarioCentavos = null, long? TotalLinhaCentavos = null) =>
        new(produtoId, quantidade, tamanho, PrecoUnitarioCentavos, TotalLinhaCentavos);

    private static PedidoVenda Pix(params ItemCarrinho[] itens) => new(itens, 0, FormaPagamento.Pix);

    // ---------- Tamanho ----------

    [Fact]
    public async Task Venda_baixa_o_tamanho_escolhido_e_o_total_e_registra_o_tamanho()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CamisetaId, 2, "m")));

        Assert.Equal(3, c.EstoqueDe(c.CamisetaId, "M"));
        Assert.Equal(3, c.EstoqueDe(c.CamisetaId, "P"));       // não mexeu nos outros
        Assert.Equal(2, c.EstoqueDe(c.CamisetaId, Tamanhos.SemTamanho));
        Assert.Equal(8, c.EstoqueTotal(c.CamisetaId));          // 10 - 2
        Assert.Equal("M", Assert.Single(venda.Itens).Tamanho);

        using var db = c.Banco.CriarContexto();
        var mov = db.Movimentacoes.Single(m => m.Tipo == TipoMovimentacao.Venda);
        Assert.Equal("M", mov.Tamanho);
        Assert.Equal(-2, mov.Quantidade);
    }

    [Fact]
    public async Task Cancelar_devolve_o_estoque_ao_mesmo_tamanho()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CamisetaId, 2, "P"), I(c.CamisetaId, 1, "M")));

        await c.Vendas.CancelarAsync(venda.Id);

        Assert.Equal(3, c.EstoqueDe(c.CamisetaId, "P"));
        Assert.Equal(5, c.EstoqueDe(c.CamisetaId, "M"));
        Assert.Equal(10, c.EstoqueTotal(c.CamisetaId));
    }

    [Fact]
    public async Task Tamanhos_diferentes_do_mesmo_produto_viram_itens_separados()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CamisetaId, 1, "P"), I(c.CamisetaId, 1, "M")));

        Assert.Equal(2, venda.Itens.Count);
    }

    [Fact]
    public async Task Tamanho_que_o_produto_nao_tem_falha()
    {
        using var c = await Cenario.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => c.Vendas.FinalizarAsync(Pix(I(c.CamisetaId, 1, "GG"))));
        Assert.Contains("não tem o tamanho GG", erro.Message);
    }

    [Fact]
    public async Task Estoque_do_tamanho_insuficiente_bloqueia_mesmo_com_total_suficiente_e_nao_grava_nada()
    {
        using var c = await Cenario.CriarAsync();

        // Total do produto = 10, mas o P só tem 3.
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 1), I(c.CamisetaId, 4, "P"))));

        Assert.Contains("tamanho P", erro.Message);
        Assert.Equal(10, c.EstoqueDe(c.BoneId, Tamanhos.SemTamanho)); // o boné, que estava ok, também não foi vendido
        Assert.Equal(3, c.EstoqueDe(c.CamisetaId, "P"));
        using var db = c.Banco.CriarContexto();
        Assert.Empty(db.Vendas);
    }

    [Fact]
    public async Task Linhas_do_mesmo_tamanho_com_precos_diferentes_somam_na_checagem_de_estoque()
    {
        using var c = await Cenario.CriarAsync();

        // 2 + 2 = 4 unidades do tamanho P, que só tem 3.
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(Pix(
            I(c.CamisetaId, 2, "P"),
            I(c.CamisetaId, 2, "P", PrecoUnitarioCentavos: 4000))));
        Assert.Equal(3, c.EstoqueDe(c.CamisetaId, "P"));
    }

    // ---------- Tabela de preço ----------

    [Fact]
    public async Task Atacado_usa_o_preco_de_atacado_e_fica_registrado_na_venda()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.CamisetaId, 2, "M")) with { TabelaPreco = TabelaPreco.Atacado });

        Assert.Equal(TabelaPreco.Atacado, venda.TabelaPreco);
        Assert.Equal(3500, venda.Itens.Single().PrecoUnitarioCentavos);
        Assert.Equal(7000, venda.TotalCentavos);
    }

    [Fact]
    public async Task Atacado_sem_preco_definido_cai_no_varejo_e_varejo_ignora_o_atacado()
    {
        using var c = await Cenario.CriarAsync();

        var atacado = await c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 1)) with { TabelaPreco = TabelaPreco.Atacado });
        var varejo = await c.Vendas.FinalizarAsync(Pix(I(c.CamisetaId, 1, "M")));

        Assert.Equal(2000, atacado.TotalCentavos);
        Assert.Equal(5000, varejo.TotalCentavos);
        Assert.Equal(TabelaPreco.Varejo, varejo.TabelaPreco);
    }

    // ---------- Preço editado na linha ----------

    [Fact]
    public async Task Preco_editado_por_unidade_vale_para_a_linha()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 3, PrecoUnitarioCentavos: 1500)));

        var item = venda.Itens.Single();
        Assert.Equal(1500, item.PrecoUnitarioCentavos);
        Assert.Equal(4500, item.TotalCentavos);
        Assert.Equal(4500, venda.TotalCentavos);
        using var db = c.Banco.CriarContexto();
        Assert.Equal(2000, db.Produtos.Single(p => p.Id == c.BoneId).PrecoVendaCentavos); // cadastro intacto
    }

    [Fact]
    public async Task Total_da_linha_editado_vale_exato_e_o_unitario_e_arredondado()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 3, TotalLinhaCentavos: 1000)));

        var item = venda.Itens.Single();
        Assert.Equal(1000, item.TotalCentavos);         // R$ 10,00 pelas 3 unidades, sem perder centavo
        Assert.Equal(333, item.PrecoUnitarioCentavos);  // 10,00 / 3, arredondado
        Assert.Equal(1000, venda.TotalCentavos);
    }

    [Fact]
    public async Task Preco_editado_negativo_ou_unitario_e_total_juntos_falham()
    {
        using var c = await Cenario.CriarAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 1, PrecoUnitarioCentavos: -1))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 1, TotalLinhaCentavos: -1))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 1, PrecoUnitarioCentavos: 100, TotalLinhaCentavos: 100))));
    }

    [Fact]
    public async Task Preco_zero_e_permitido_para_brinde()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 1, PrecoUnitarioCentavos: 0)));
        Assert.Equal(0, venda.TotalCentavos);
    }

    // ---------- Desconto em % ----------

    [Fact]
    public async Task Desconto_percentual_vira_reais_na_venda()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 2)) with { DescontoPercentual = 10m }); // 10% de R$ 40,00

        Assert.Equal(4000, venda.SubtotalCentavos);
        Assert.Equal(400, venda.DescontoCentavos);
        Assert.Equal(3600, venda.TotalCentavos);
    }

    [Fact]
    public async Task Desconto_percentual_e_arredondado_ao_centavo()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 1)) with { DescontoPercentual = 33.33m }); // 33,33% de R$ 20,00 = 6,666

        Assert.Equal(667, venda.DescontoCentavos);
        Assert.Equal(1333, venda.TotalCentavos);
    }

    [Fact]
    public async Task Desconto_de_100_por_cento_zera_o_total()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 1)) with { DescontoPercentual = 100m });
        Assert.Equal(0, venda.TotalCentavos);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public async Task Desconto_percentual_fora_de_0_a_100_falha(double percentual)
    {
        using var c = await Cenario.CriarAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 1)) with { DescontoPercentual = (decimal)percentual }));
    }

    [Fact]
    public async Task Desconto_em_reais_e_em_percentual_juntos_falha()
    {
        using var c = await Cenario.CriarAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 1)) with { DescontoCentavos = 100, DescontoPercentual = 5m }));
    }

    [Fact]
    public async Task Troco_considera_o_desconto_percentual()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.BoneId, 1) }, 0, FormaPagamento.Dinheiro, 2000, DescontoPercentual: 10m));

        Assert.Equal(1800, venda.TotalCentavos);
        Assert.Equal(200, venda.TrocoCentavos);
    }

    // ---------- Formas de pagamento ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Credito_aceita_de_1_a_4_parcelas(int parcelas)
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.BoneId, 1) }, 0, FormaPagamento.CartaoCredito, Parcelas: parcelas));

        Assert.Equal(parcelas, venda.Parcelas);
        Assert.Equal(FormaPagamento.CartaoCredito, venda.FormaPagamento);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Credito_fora_de_1_a_4_parcelas_falha(int parcelas)
    {
        using var c = await Cenario.CriarAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.BoneId, 1) }, 0, FormaPagamento.CartaoCredito, Parcelas: parcelas)));
        Assert.Equal(10, c.EstoqueDe(c.BoneId, Tamanhos.SemTamanho));
    }

    [Theory]
    [InlineData(FormaPagamento.Pix)]
    [InlineData(FormaPagamento.CartaoDebito)]
    [InlineData(FormaPagamento.Cheque)]
    public async Task So_o_credito_pode_ser_parcelado(FormaPagamento forma)
    {
        using var c = await Cenario.CriarAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.BoneId, 1) }, 0, forma, Parcelas: 2)));
    }

    [Theory]
    [InlineData(FormaPagamento.Pix)]
    [InlineData(FormaPagamento.CartaoDebito)]
    [InlineData(FormaPagamento.Cheque)]
    public async Task Pix_debito_e_cheque_pagam_o_total_sem_troco(FormaPagamento forma)
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.BoneId, 1) }, 0, forma, ValorRecebidoCentavos: 99999));

        Assert.Equal(forma, venda.FormaPagamento);
        Assert.Equal(1, venda.Parcelas);
        Assert.Equal(0, venda.TrocoCentavos);
        Assert.Null(venda.ValorRecebidoCentavos);
    }

    // ---------- A prazo e cliente ----------

    [Fact]
    public async Task Venda_a_prazo_exige_cliente()
    {
        using var c = await Cenario.CriarAsync();

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(c.BoneId, 1) }, 0, FormaPagamento.APrazo)));

        Assert.Contains("cliente", erro.Message);
        Assert.Equal(10, c.EstoqueDe(c.BoneId, Tamanhos.SemTamanho));
    }

    [Fact]
    public async Task Venda_a_prazo_com_cliente_grava_o_cliente_sem_troco()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.Clientes.CadastrarAsync(new DadosCliente("Maria"));

        var venda = await c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.BoneId, 2) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));

        Assert.Equal(maria.Id, venda.ClienteId);
        Assert.Equal(FormaPagamento.APrazo, venda.FormaPagamento);
        Assert.Equal(0, venda.TrocoCentavos);
        Assert.Equal(8, c.EstoqueDe(c.BoneId, Tamanhos.SemTamanho));

        var doDia = await c.Vendas.ListarDoDiaAsync();
        Assert.Equal("Maria", doDia.Single().Cliente!.Nome);
    }

    [Fact]
    public async Task Cliente_inexistente_ou_desativado_falha_em_qualquer_forma_de_pagamento()
    {
        using var c = await Cenario.CriarAsync();
        var pedro = await c.Clientes.CadastrarAsync(new DadosCliente("Pedro"));
        await c.Clientes.DesativarAsync(pedro.Id);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 1)) with { ClienteId = 999 }));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(
            Pix(new ItemCarrinho(c.BoneId, 1)) with { ClienteId = pedro.Id }));
    }

    [Fact]
    public async Task Cliente_e_opcional_nas_outras_formas_de_pagamento()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.Clientes.CadastrarAsync(new DadosCliente("Maria"));

        var semCliente = await c.Vendas.FinalizarAsync(Pix(I(c.BoneId, 1)));
        var comCliente = await c.Vendas.FinalizarAsync(Pix(new ItemCarrinho(c.BoneId, 1)) with { ClienteId = maria.Id });

        Assert.Null(semCliente.ClienteId);
        Assert.Equal(maria.Id, comCliente.ClienteId);
    }
}
