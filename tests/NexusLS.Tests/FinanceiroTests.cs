using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class FinanceiroTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private DateTime _agora = new(2026, 10, 20, 10, 0, 0);
    private readonly FinanceiroService _fin;

    public FinanceiroTests() => _fin = new FinanceiroService(_banco.Fabrica, () => _agora);

    public void Dispose() => _banco.Dispose();

    private static DateTime Dia(int d, int m = 10) => new(2026, m, d);

    private static FiltroLancamentos Mes(SituacaoLancamento? sit = null, TipoLancamento? tipo = null) =>
        new(Dia(1), Dia(31), tipo, sit);

    // ---------- criar ----------

    [Fact]
    public async Task Parcelamento_divide_em_centavos_inteiros_e_a_ultima_leva_a_sobra()
    {
        var criados = await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Estoque", 10000, Dia(30), Parcelas: 3));

        Assert.Equal(new long[] { 3333, 3333, 3334 }, criados.Select(l => l.ValorCentavos));
        Assert.Equal(10000, criados.Sum(l => l.ValorCentavos));
        Assert.Equal(new[] { "Estoque (1/3)", "Estoque (2/3)", "Estoque (3/3)" }, criados.Select(l => l.Descricao));
        Assert.Equal(new[] { Dia(30), Dia(30, 11), Dia(30, 12) }, criados.Select(l => l.Vencimento));
        Assert.Single(criados.Select(l => l.GrupoId).Distinct());
    }

    [Fact]
    public async Task Vencimento_no_dia_31_nao_estoura_nos_meses_curtos()
    {
        var criados = await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel", 5000, Dia(31), Parcelas: 2));

        Assert.Equal(new DateTime(2026, 11, 30), criados[1].Vencimento);
    }

    [Theory]
    [InlineData("", 100, 1, "descrição")]
    [InlineData("Luz", 0, 1, "maior que zero")]
    [InlineData("Luz", -5, 1, "maior que zero")]
    [InlineData("Luz", 100, 0, "parcelas")]
    [InlineData("Luz", 100, 61, "parcelas")]
    [InlineData("Luz", 2, 3, "pequeno")]
    public async Task Dados_invalidos_sao_recusados_com_explicacao(string descricao, long valor, int parcelas, string trecho)
    {
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, descricao, valor, Dia(25), Parcelas: parcelas)));

        Assert.Contains(trecho, erro.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _fin.ListarAsync(Mes()));
    }

    [Fact]
    public async Task Criar_ja_pago_marca_a_data_e_a_forma()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Café", 1500, Dia(20), JaPago: true, Forma: FormaPagamento.Pix))).Single();

        Assert.Equal(_agora, l.PagoEm);
        Assert.Equal(FormaPagamento.Pix, l.FormaPagamento);
    }

    [Fact]
    public async Task Parcelado_nao_pode_ser_criado_ja_pago()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "X", 1000, Dia(20), Parcelas: 2, JaPago: true)));
    }

    // ---------- situação, pagar, estornar, cancelar ----------

    [Fact]
    public async Task Situacao_vai_de_aberto_para_atrasado_quando_o_vencimento_passa()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Luz", 20000, Dia(22)))).Single();

        Assert.Equal(SituacaoLancamento.Aberto, RegrasFinanceiras.Situacao(l, _agora));
        Assert.Equal(SituacaoLancamento.Aberto, RegrasFinanceiras.Situacao(l, Dia(22))); // no dia ainda não está atrasado
        Assert.Equal(SituacaoLancamento.Atrasado, RegrasFinanceiras.Situacao(l, Dia(23)));
    }

    [Fact]
    public async Task Pagar_e_estornar_voltam_o_lancamento_ao_normal()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Luz", 20000, Dia(22)))).Single();

        await _fin.PagarAsync(l.Id, FormaPagamento.Dinheiro);
        var pago = (await _fin.ListarAsync(Mes(SituacaoLancamento.Pago))).Single();
        Assert.Equal(FormaPagamento.Dinheiro, pago.FormaPagamento);

        await _fin.EstornarPagamentoAsync(l.Id);
        Assert.Empty(await _fin.ListarAsync(Mes(SituacaoLancamento.Pago)));
        Assert.Single(await _fin.ListarAsync(Mes(SituacaoLancamento.Aberto)));
    }

    [Fact]
    public async Task Nao_paga_duas_vezes_nem_no_futuro_nem_a_prazo()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Luz", 20000, Dia(22)))).Single();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.PagarAsync(l.Id, FormaPagamento.APrazo));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.PagarAsync(l.Id, FormaPagamento.Pix, Dia(25)));
        await _fin.PagarAsync(l.Id, FormaPagamento.Pix);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.PagarAsync(l.Id, FormaPagamento.Pix));
    }

    [Fact]
    public async Task Cancelar_exige_motivo_nao_apaga_e_nao_vale_para_pago()
    {
        var a = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "A", 1000, Dia(22)))).Single();
        var b = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "B", 1000, Dia(22)))).Single();
        await _fin.PagarAsync(b.Id, FormaPagamento.Pix);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CancelarAsync(a.Id, "x"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CancelarAsync(b.Id, "lançado errado"));
        await _fin.CancelarAsync(a.Id, "lançado em duplicidade");

        var cancelado = (await _fin.ListarAsync(Mes(SituacaoLancamento.Cancelado))).Single();
        Assert.Equal("lançado em duplicidade", cancelado.MotivoCancelamento);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CancelarAsync(a.Id, "de novo"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.PagarAsync(a.Id, FormaPagamento.Pix));
    }

    [Fact]
    public async Task Editar_so_enquanto_nao_foi_pago()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Luz", 20000, Dia(22)))).Single();

        await _fin.EditarAsync(l.Id, "Luz de outubro", 21000, Dia(25), "Energia", "Neoenergia", null);
        var editado = (await _fin.ListarAsync(Mes())).Single();
        Assert.Equal((21000L, "Luz de outubro", "Energia"), (editado.ValorCentavos, editado.Descricao, editado.Categoria));

        await _fin.PagarAsync(l.Id, FormaPagamento.Pix);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.EditarAsync(l.Id, "x", 1, Dia(25), null, null, null));
    }

    // ---------- listar ----------

    [Fact]
    public async Task Lista_filtra_por_periodo_tipo_e_texto_e_ordena_por_vencimento()
    {
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel", 100000, Dia(10), Contraparte: "Imobiliária"));
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Conserto", 5000, Dia(5)));
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Luz", 20000, Dia(3, 11))); // fora do mês

        Assert.Equal(new[] { "Conserto", "Aluguel" }, (await _fin.ListarAsync(Mes())).Select(l => l.Descricao));
        Assert.Equal("Aluguel", (await _fin.ListarAsync(Mes(tipo: TipoLancamento.Despesa))).Single().Descricao);
        Assert.Equal("Aluguel", (await _fin.ListarAsync(Mes() with { Texto = "imobili" })).Single().Descricao);
    }

    // ---------- resumo ----------

    [Fact]
    public async Task Resumo_soma_vendas_receitas_e_despesas_pagas_e_separa_o_que_esta_em_aberto()
    {
        // A venda é gravada com o relógio real; por isso este teste usa datas relativas a hoje.
        _agora = DateTime.Now;
        var hoje = _agora.Date;
        var inicio = hoje.AddDays(-10);
        var fim = hoje.AddDays(10);

        var produtos = new ProdutoService(_banco.Fabrica);
        var produto = await produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 10);
        await new VendaService(_banco.Fabrica).FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(produto.Id, 2) }, 0, FormaPagamento.Dinheiro, 4000));
        // venda de R$ 40,00 com custo de R$ 16,00

        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel", 1000, hoje.AddDays(-5), Categoria: "Aluguel", JaPago: true, Forma: FormaPagamento.Pix));
        var luz = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Luz", 500, hoje.AddDays(-2), Categoria: "Energia"))).Single(); // atrasada
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Internet", 300, hoje.AddDays(5), Categoria: "Internet e telefone"));      // a vencer
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Conserto", 700, hoje.AddDays(-8), JaPago: true));
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Consultoria", 900, hoje.AddDays(4)));                                      // a receber

        var antes = await _fin.ResumoAsync(inicio, fim);
        Assert.Equal(1, antes.QuantidadeAtrasados);
        Assert.Equal(500, antes.AtrasadosCentavos);
        Assert.Equal(800, antes.APagarCentavos);      // luz + internet

        await _fin.CancelarAsync(luz.Id, "teste de cancelamento");
        var r = await _fin.ResumoAsync(inicio, fim);

        Assert.Equal(4000, r.VendasCentavos);
        Assert.Equal(1600, r.CustoMercadoriasCentavos);
        Assert.Equal(2400, r.LucroBrutoCentavos);
        Assert.Equal(700, r.ReceitasRecebidasCentavos);
        Assert.Equal(1000, r.DespesasPagasCentavos);
        Assert.Equal(300, r.APagarCentavos);          // só a internet (a luz foi cancelada)
        Assert.Equal(900, r.AReceberCentavos);
        Assert.Equal(0, r.QuantidadeAtrasados);
        Assert.Equal(2400 + 700 - 1000, r.ResultadoCentavos);
        Assert.Equal("Aluguel", Assert.Single(r.DespesasPorCategoria).Categoria);
    }

    [Fact]
    public async Task Resumo_conta_atrasados_de_qualquer_periodo()
    {
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Velha", 800, Dia(1, 8)));   // agosto, em aberto
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Nova", 200, Dia(30)));

        var r = await _fin.ResumoAsync(Dia(1), Dia(31));

        Assert.Equal(1, r.QuantidadeAtrasados);
        Assert.Equal(800, r.AtrasadosCentavos);
        Assert.Equal(1000, r.APagarCentavos); // a atrasada de agosto continua a pagar
    }
}
