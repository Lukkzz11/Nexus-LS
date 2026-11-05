using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Fase 2.7: DRE / demonstrativo do período, com fonte única no MotorFinanceiro. Uma loja conferida à mão.
/// Produto: custo R$ 20,00, preço R$ 50,00.
/// </summary>
[Collection("OperadorAtual")]
public sealed class DemonstrativoTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);
    private static readonly DateTime Inicio = new(2026, 10, 1);
    private static readonly DateTime Fim = new(2026, 10, 31);

    private readonly BancoDeTeste _banco = new();
    private readonly ContaFinanceiraService _contas;
    private readonly VendaService _vendas;
    private readonly FinanceiroService _fin;
    private readonly DemonstrativoService _dre;
    private int _produtoId;
    private int _bancoId;

    public DemonstrativoTests()
    {
        _contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        _vendas = new VendaService(_banco.Fabrica, () => Agora);
        _fin = new FinanceiroService(_banco.Fabrica, () => Agora);
        _dre = new DemonstrativoService(_banco.Fabrica, () => Agora);
        OperadorAtual.Nome = null;
    }

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync(long custoProduto = 2000)
    {
        await _contas.GarantirContaPadraoAsync();
        var gaveta = (await _contas.ListarAsync()).Single(c => c.Tipo == TipoContaFin.Caixa).Id;
        await _contas.EditarAsync(gaveta, new DadosConta(ContaFinanceiraService.NomeDaGaveta, TipoContaFin.Caixa, 10000, Inicio));
        _bancoId = (await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco, 50000, Inicio))).Id;
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, custoProduto, 1), 100)).Id;
    }

    private Task<Venda> VenderAsync(FormaPagamento forma, int quantidade, int? clienteId = null) =>
        _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, quantidade) }, 0, forma,
            ValorRecebidoCentavos: forma == FormaPagamento.Dinheiro ? 5000L * quantidade : null, ClienteId: clienteId));

    [Fact]
    public async Task Loja_de_ponta_a_ponta_bate_com_as_contas_feitas_a_mao()
    {
        await PrepararAsync();
        var caixa = new CaixaService(_banco.Fabrica, () => Agora);
        var clientes = new ClienteService(_banco.Fabrica, () => Agora);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        await caixa.AbrirAsync(5000); // troco inicial: já estava na gaveta, não é receita

        // Compra de mercadoria paga na hora: sai dinheiro, mas NÃO é despesa (o custo entra quando a peça é vendida)
        await new CompraService(_banco.Fabrica, () => Agora).RegistrarAsync(new DadosCompra(
            new[] { new ItemCompraDados(_produtoId, 10, 2000) },
            Pagamento: new PagamentoCompra(1, Agora, JaPago: true, Forma: FormaPagamento.Pix)));

        await VenderAsync(FormaPagamento.Dinheiro, 2);        // 100,00 (custo 40,00)
        await VenderAsync(FormaPagamento.APrazo, 3, maria.Id); // 150,00 (custo 60,00): receita HOJE, mesmo sem receber
        var debito = await VenderAsync(FormaPagamento.CartaoDebito, 1); // 50,00 (custo 20,00)
        await new LiquidacaoCartaoService(_banco.Fabrica, () => Agora).LiquidarAsync(debito.Id, Agora, _bancoId, 150); // taxa 1,50 = despesa

        // Recebimento de dívida: entra dinheiro, mas NÃO é receita de novo
        await clientes.RegistrarPagamentoAsync(maria.Id, 4000, FormaPagamento.Dinheiro);

        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel", 8000, Agora, "Aluguel", JaPago: true, Forma: FormaPagamento.Pix));
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Dinheiro do dono", 30000, Agora, JaPago: true, Forma: FormaPagamento.Dinheiro,
            Classificacao: ClassificacaoLancamento.Aporte));
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Retirada do dono", 5000, Agora, JaPago: true, Forma: FormaPagamento.Dinheiro,
            Classificacao: ClassificacaoLancamento.RetiradaProprietario));
        await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 2000, "Levou ao banco");

        var d = await _dre.MontarAsync(Inicio, Fim);

        // Resultado: receita 300,00 − custo 120,00 = lucro bruto 180,00 ; despesas 80,00 + taxa 1,50 ; resultado 98,50
        Assert.Equal(30000, d.ReceitaBrutaCentavos);
        Assert.Equal(0, d.DevolucoesCentavos);
        Assert.Equal(30000, d.ReceitaLiquidaCentavos);
        Assert.Equal(12000, d.CustoMercadoriasCentavos);
        Assert.Equal(18000, d.LucroBrutoCentavos);
        Assert.Equal(60.0, d.MargemBrutaPercentual, 3);
        Assert.Equal(0, d.OutrasReceitasCentavos);
        Assert.Equal(8150, d.DespesasOperacionaisCentavos);
        Assert.Equal(9850, d.ResultadoCentavos);
        Assert.Equal(new[] { ("Aluguel", 8000L), ("Taxas de cartão", 150L) },
            d.DespesasPorCategoria.Select(c => (c.Categoria, c.TotalCentavos)));

        // Fora do resultado
        Assert.Equal(4000, d.RecebimentosDeDividasCentavos);
        Assert.Equal(20000, d.ComprasMercadoriaCentavos);
        Assert.Equal(30000, d.AportesCentavos);
        Assert.Equal(5000, d.RetiradasProprietarioCentavos);
        Assert.Equal(2000, d.RetiradasCaixaCentavos);
        Assert.Equal(0, d.OutrasEntradasCentavos);

        // Fluxo de caixa: entrou 100 + 50 + 40 + 300 = 490 ; saiu 200 + 80 + 1,50 + 50 + 20 = 351,50
        Assert.Equal(49000, d.EntradasRealizadasCentavos);
        Assert.Equal(35150, d.SaidasRealizadasCentavos);
        Assert.Equal(13850, d.FluxoCaixaCentavos);

        // Vendas
        Assert.Equal(3, d.QuantidadeVendas);
        Assert.Equal(10000, d.TicketMedioCentavos);
        Assert.Equal(0, d.PecasSemCusto);
    }

    [Fact]
    public async Task Compra_de_mercadoria_nao_e_despesa_e_o_custo_so_entra_na_venda()
    {
        await PrepararAsync();
        await new CompraService(_banco.Fabrica, () => Agora).RegistrarAsync(new DadosCompra(
            new[] { new ItemCompraDados(_produtoId, 10, 2000) },
            Pagamento: new PagamentoCompra(1, Agora, JaPago: true, Forma: FormaPagamento.Pix)));

        var antes = await _dre.MontarAsync(Inicio, Fim);
        Assert.Equal(20000, antes.ComprasMercadoriaCentavos);
        Assert.Equal(0, antes.DespesasOperacionaisCentavos);
        Assert.Equal(0, antes.CustoMercadoriasCentavos);
        Assert.Equal(0, antes.ResultadoCentavos);

        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Pix));
        var depois = await _dre.MontarAsync(Inicio, Fim);
        Assert.Equal(2000, depois.CustoMercadoriasCentavos); // só agora o custo da peça vendida
        Assert.Equal(3000, depois.ResultadoCentavos);
    }

    [Fact]
    public async Task Venda_a_prazo_e_receita_na_data_da_venda_e_o_recebimento_nao_conta_de_novo()
    {
        await PrepararAsync();
        var clientes = new ClienteService(_banco.Fabrica, () => Agora);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        await VenderAsync(FormaPagamento.APrazo, 2, maria.Id);

        var semRecebimento = await _dre.MontarAsync(Inicio, Fim);
        Assert.Equal(10000, semRecebimento.ReceitaBrutaCentavos);
        Assert.Equal(0, semRecebimento.RecebimentosDeDividasCentavos);

        await clientes.RegistrarPagamentoAsync(maria.Id, 10000, FormaPagamento.Pix);
        var comRecebimento = await _dre.MontarAsync(Inicio, Fim);

        Assert.Equal(10000, comRecebimento.ReceitaBrutaCentavos);        // não dobrou
        Assert.Equal(10000, comRecebimento.RecebimentosDeDividasCentavos);
        Assert.Equal(6000, comRecebimento.ResultadoCentavos);            // 100,00 − custo 40,00, e só
    }

    [Fact]
    public async Task Recebimento_em_outro_periodo_aparece_no_periodo_em_que_o_dinheiro_entrou()
    {
        await PrepararAsync();
        var clientes = new ClienteService(_banco.Fabrica, () => Agora);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        await VenderAsync(FormaPagamento.APrazo, 2, maria.Id);
        await clientes.RegistrarPagamentoAsync(maria.Id, 3000, FormaPagamento.Dinheiro);

        var outroDia = await _dre.MontarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 10)); // antes da venda
        var hoje = await _dre.MontarAsync(Agora.Date, Agora.Date);

        Assert.Equal(0, outroDia.ReceitaBrutaCentavos);
        Assert.Equal(0, outroDia.RecebimentosDeDividasCentavos);
        Assert.Equal(10000, hoje.ReceitaBrutaCentavos);
        Assert.Equal(3000, hoje.RecebimentosDeDividasCentavos);
    }

    [Fact]
    public async Task Venda_cancelada_nao_entra_no_demonstrativo()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, 2);
        await _vendas.CancelarAsync(venda.Id, "cliente desistiu");

        var d = await _dre.MontarAsync(Inicio, Fim);

        Assert.Equal(0, d.ReceitaBrutaCentavos);
        Assert.Equal(0, d.QuantidadeVendas);
        Assert.Equal(0, d.ResultadoCentavos);
    }

    [Fact]
    public async Task Peca_sem_custo_cadastrado_gera_aviso_e_o_resultado_sai_antes_do_custo()
    {
        await PrepararAsync(custoProduto: 0);
        await VenderAsync(FormaPagamento.Pix, 3);

        var d = await _dre.MontarAsync(Inicio, Fim);

        Assert.Equal(3, d.PecasSemCusto);
        Assert.Equal(15000, d.ResultadoCentavos);
        Assert.Contains(d.Avisos!, a => a.Contains("3 peça(s)") && a.Contains("sem custo"));
    }

    [Fact]
    public async Task O_resultado_do_demonstrativo_e_o_do_resumo_do_Financeiro_sao_o_mesmo_numero()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, 4);
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Energia", 7000, Agora, "Energia", JaPago: true, Forma: FormaPagamento.Pix));
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Serviço", 2500, Agora, JaPago: true, Forma: FormaPagamento.Pix));
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Aporte", 90000, Agora, JaPago: true, Forma: FormaPagamento.Pix,
            Classificacao: ClassificacaoLancamento.Aporte));

        var d = await _dre.MontarAsync(Inicio, Fim);
        var resumo = await _fin.ResumoAsync(Inicio, Fim);

        Assert.Equal(resumo.ResultadoCentavos, d.ResultadoCentavos);
        Assert.Equal(resumo.LucroBrutoCentavos, d.LucroBrutoCentavos);
        Assert.Equal(resumo.DespesasPagasCentavos, d.DespesasOperacionaisCentavos);
        Assert.Equal(resumo.ReceitasRecebidasCentavos, d.OutrasReceitasCentavos);
        Assert.Equal(20000 - 8000 - 7000 + 2500, d.ResultadoCentavos);
    }

    [Fact]
    public async Task Taxa_do_cartao_e_despesa_tanto_no_demonstrativo_quanto_no_resumo()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.CartaoDebito, 2);
        await new LiquidacaoCartaoService(_banco.Fabrica, () => Agora).LiquidarAsync(venda.Id, Agora, _bancoId, 300);

        var d = await _dre.MontarAsync(Inicio, Fim);
        var resumo = await _fin.ResumoAsync(Inicio, Fim);

        Assert.Equal(300, d.DespesasOperacionaisCentavos);
        Assert.Equal(300, resumo.DespesasPagasCentavos);
        Assert.Equal(d.ResultadoCentavos, resumo.ResultadoCentavos);
        Assert.Contains(resumo.DespesasPorCategoria, c => c.Categoria == "Taxas de cartão" && c.TotalCentavos == 300);
    }

    [Fact]
    public async Task Periodo_sem_movimento_zera_tudo_e_nao_divide_por_zero()
    {
        await PrepararAsync();

        var d = await _dre.MontarAsync(Inicio, Fim);

        Assert.Equal(0, d.ReceitaBrutaCentavos);
        Assert.Equal(0, d.MargemBrutaPercentual);
        Assert.Equal(0, d.TicketMedioCentavos);
        Assert.Equal(0, d.ResultadoCentavos);
        Assert.Empty(d.DespesasPorCategoria);
    }

    [Fact]
    public async Task Data_final_antes_da_inicial_e_recusada()
    {
        await PrepararAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _dre.MontarAsync(Fim, Inicio));
    }

    [Fact]
    public void Montar_puro_calcula_margem_e_separa_o_que_nao_e_resultado()
    {
        var periodo = new PeriodoFin(Inicio, Fim, 0, 0, 20000, 2000, 6000, 1000, 4000,
            new Dictionary<string, long> { ["Aluguel"] = 3000, ["Energia"] = 1000 },
            AportesCentavos: 500, RecebimentosClientesCentavos: 700, ComprasMercadoriaCentavos: 900);

        var d = Demonstrativo.Montar(periodo, quantidadeVendas: 4);

        Assert.Equal(18000, d.ReceitaLiquidaCentavos);
        Assert.Equal(12000, d.LucroBrutoCentavos);
        Assert.Equal(66.667, d.MargemBrutaPercentual, 3);
        Assert.Equal(9000, d.ResultadoCentavos);           // 12000 + 1000 − 4000
        Assert.Equal(5000, d.TicketMedioCentavos);
        Assert.Equal("Aluguel", d.DespesasPorCategoria[0].Categoria);
        Assert.Equal(1200, d.EntradasForaDoResultadoCentavos); // 700 + 500
        Assert.Equal(900, d.SaidasForaDoResultadoCentavos);
    }
}
