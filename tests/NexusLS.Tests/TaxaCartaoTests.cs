using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Acréscimo do cartão de crédito, igual ao do ERP: 1x 5%, 2x 7%, 3x 9%, 4x 10%.</summary>
public class TaxaCartaoTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 7)]
    [InlineData(3, 9)]
    [InlineData(4, 10)]
    public void Percentual_por_parcela_e_o_mesmo_do_ERP(int parcelas, int esperado) =>
        Assert.Equal(esperado, (int)TaxasCartao.Percentual(parcelas));

    [Fact]
    public void Acrescimo_e_arredondado_ao_centavo_e_parcelas_fora_de_1_a_4_sao_recusadas()
    {
        Assert.Equal(450, TaxasCartao.Acrescimo(9000, 1));   // 5% de 90,00
        Assert.Equal(1, TaxasCartao.Acrescimo(10, 4));        // 10% de 0,10 = 0,01
        Assert.Equal(0, TaxasCartao.Acrescimo(0, 2));
        Assert.Throws<RegraDeNegocioException>(() => TaxasCartao.Acrescimo(1000, 5));
        Assert.Throws<RegraDeNegocioException>(() => TaxasCartao.Acrescimo(1000, 0));
    }

    [Fact]
    public void O_total_da_tela_de_pagamento_acompanha_a_forma_e_as_parcelas()
    {
        var pag = new SelecaoPagamento(10000);
        Assert.Equal(10000, pag.TotalCentavos);                 // dinheiro: sem acréscimo

        pag.Proxima(); pag.Proxima(); pag.Proxima();            // dinheiro → pix → débito → crédito
        Assert.Equal(FormaPagamento.CartaoCredito, pag.Forma);
        Assert.Equal(10500, pag.TotalCentavos);                 // 1x: +5%
        pag.MaisParcelas();
        Assert.Equal(10700, pag.TotalCentavos);                 // 2x: +7%
        pag.MaisParcelas(); pag.MaisParcelas();
        Assert.Equal(11000, pag.TotalCentavos);                 // 4x: +10%
        Assert.Equal(1000, pag.AcrescimoCentavos);

        pag.Anterior();                                         // volta ao débito
        Assert.Equal(10000, pag.TotalCentavos);
        Assert.Equal(0, pag.AcrescimoCentavos);
    }

    [Fact]
    public async Task Venda_no_credito_grava_o_acrescimo_e_o_total_com_ele_e_o_debito_nao_tem()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var item = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 10000, 4000, 1), 5);
        var carrinho = new Carrinho();
        carrinho.Adicionar((await produtos.BuscarAsync("Boné")).Single(), (await produtos.ListarTamanhosAsync(item.Id)).Single(), 1);
        var vendas = new VendaService(banco.Fabrica);

        var credito = new SelecaoPagamento(10000);
        credito.Proxima(); credito.Proxima(); credito.Proxima(); credito.MaisParcelas(); // crédito 2x
        var v = await vendas.FinalizarAsync(credito.ParaPedido(carrinho, new DescontoResolvido(0, null, 0)) with { ChaveUnica = Guid.NewGuid() });

        Assert.Equal(10000, v.SubtotalCentavos);
        Assert.Equal(700, v.AcrescimoCentavos);
        Assert.Equal(10700, v.TotalCentavos);

        var debito = new SelecaoPagamento(10000);
        debito.Proxima(); debito.Proxima();
        var d = await vendas.FinalizarAsync(debito.ParaPedido(carrinho, new DescontoResolvido(0, null, 0)) with { ChaveUnica = Guid.NewGuid() });
        Assert.Equal(0, d.AcrescimoCentavos);
        Assert.Equal(10000, d.TotalCentavos);
    }

    [Fact]
    public async Task O_desconto_vem_antes_do_acrescimo()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var item = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 10000, 4000, 1), 5);
        var carrinho = new Carrinho();
        carrinho.Adicionar((await produtos.BuscarAsync("Boné")).Single(), (await produtos.ListarTamanhosAsync(item.Id)).Single(), 1);

        var credito = new SelecaoPagamento(9000); // R$ 100 − R$ 10 de desconto
        credito.Proxima(); credito.Proxima(); credito.Proxima(); credito.MaisParcelas(); credito.MaisParcelas(); // 3x: +9%
        var v = await new VendaService(banco.Fabrica).FinalizarAsync(
            credito.ParaPedido(carrinho, new DescontoResolvido(1000, null, 0)) with { ChaveUnica = Guid.NewGuid() });

        Assert.Equal(1000, v.DescontoCentavos);
        Assert.Equal(810, v.AcrescimoCentavos);   // 9% de 90,00
        Assert.Equal(9810, v.TotalCentavos);
    }

    [Fact]
    public async Task Venda_do_ERP_no_credito_importa_desconto_e_acrescimo_separados()
    {
        using var banco = new BancoDeTeste();
        var fonte = new FonteErpFalsa();
        fonte.Por("vendas", "v1", """
            {"dataHoraVenda":"2026-10-30T14:00:00Z","itens":[{"id":"x","nome":"X","quantidade":1,"precoUnitario":100,"subtotal":100}],
             "subtotal":100,"desconto":10,"total":98.1,"formaPagamento":"cartao_credito","infoPagamento":{"parcelas":3,"taxaPercentual":9},
             "criadoEm":"2026-10-30T14:00:00Z"}
            """);

        await new ImportadorErp(banco.Fabrica, fonte, () => new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc)).ImportarAsync();

        await using var db = banco.CriarContexto();
        var v = await db.Vendas.SingleAsync();
        Assert.Equal(10000, v.SubtotalCentavos);
        Assert.Equal(1000, v.DescontoCentavos);
        Assert.Equal(810, v.AcrescimoCentavos);
        Assert.Equal(9810, v.TotalCentavos);
        Assert.Equal(3, v.Parcelas);
    }
}
