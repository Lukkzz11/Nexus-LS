using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Uma loja de ponta a ponta, com os números conferidos à mão. Produto: custo R$ 20,00, preço R$ 50,00.
/// </summary>
[Collection("OperadorAtual")] // mexe no OperadorAtual (estático): não pode rodar junto das outras que também mexem
public class PosicaoFinanceiraServiceTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);
    private static readonly DateTime Inicio = new(2026, 10, 1);

    private readonly BancoDeTeste _banco = new();
    private readonly ContaFinanceiraService _contas;
    private readonly VendaService _vendas;
    private readonly ClienteService _clientes;
    private readonly PosicaoFinanceiraService _posicao;
    private int _produtoId;
    private int _gavetaId;
    private int _bancoId;

    public PosicaoFinanceiraServiceTests()
    {
        _contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        _vendas = new VendaService(_banco.Fabrica, () => Agora);
        _clientes = new ClienteService(_banco.Fabrica, () => Agora);
        _posicao = new PosicaoFinanceiraService(_banco.Fabrica, () => Agora);
        OperadorAtual.Nome = null;
    }

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync(long saldoGaveta = 10000, long saldoBanco = 50000)
    {
        await _contas.GarantirContaPadraoAsync();
        _gavetaId = (await _contas.ListarAsync()).Single(c => c.Tipo == TipoContaFin.Caixa).Id;
        await _contas.EditarAsync(_gavetaId, new DadosConta(ContaFinanceiraService.NomeDaGaveta, TipoContaFin.Caixa, saldoGaveta, Inicio));
        _bancoId = (await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco, saldoBanco, Inicio))).Id;
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 100)).Id;
    }

    private Task<Venda> VenderAsync(FormaPagamento forma, int quantidade, int? clienteId = null, int parcelas = 1) =>
        _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, quantidade) }, 0, forma,
            // venda em dinheiro exige o valor recebido: aqui o cliente entrega o valor exato (troco zero)
            ValorRecebidoCentavos: forma == FormaPagamento.Dinheiro ? 5000L * quantidade : null,
            Parcelas: parcelas, ClienteId: clienteId));

    private static long Saldo(PosicaoFinanceira p, int contaId) => p.Contas.Single(c => c.Id == contaId).DisponivelCentavos;

    private Task<PosicaoFinanceira> CalcularAsync() => _posicao.CalcularAsync(Inicio, Agora);

    [Fact]
    public async Task Loja_de_ponta_a_ponta_bate_com_as_contas_feitas_a_mao()
    {
        await PrepararAsync();
        var caixa = new CaixaService(_banco.Fabrica, () => Agora);
        var financeiro = new FinanceiroService(_banco.Fabrica, () => Agora);
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));

        await caixa.AbrirAsync(5000);
        await VenderAsync(FormaPagamento.Dinheiro, 2);                // 100,00 -> gaveta
        await VenderAsync(FormaPagamento.Pix, 1);                     //  50,00 -> banco
        await VenderAsync(FormaPagamento.CartaoDebito, 1);            //  50,00 -> cartão a liquidar
        await VenderAsync(FormaPagamento.CartaoCredito, 2, parcelas: 2); // 100,00 -> cartão a liquidar
        await VenderAsync(FormaPagamento.Cheque, 1);                  //  50,00 -> cheque pendente
        await VenderAsync(FormaPagamento.APrazo, 3, maria.Id);        // 150,00 -> dívida
        await _clientes.RegistrarPagamentoAsync(maria.Id, 4000, FormaPagamento.Dinheiro); // 40,00 -> gaveta

        await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 2000, "Levou ao banco"); // -20,00 da gaveta
        await _contas.TransferirAsync(new DadosTransferencia(_gavetaId, _bancoId, 6000, Agora)); // 60,00 gaveta -> banco

        await financeiro.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Energia", 12000, Agora, "Energia", JaPago: true, Forma: FormaPagamento.Pix));
        await financeiro.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel", 8000, Agora.AddDays(5), "Aluguel"));
        await financeiro.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Serviço", 3000, Agora.AddDays(-1), "Serviços"));

        var p = await CalcularAsync();

        // Saldos: gaveta 100 + 100 + 40 − 20 − 60 = 160 ; banco 500 + 50 − 120 + 60 = 490
        Assert.Equal(16000, Saldo(p, _gavetaId));
        Assert.Equal(49000, Saldo(p, _bancoId));
        Assert.Equal(65000, p.SaldoDisponivelCentavos);

        // Em compensação: cartões 50 + 107 (100 + 7% do crédito em 2x) ; cheque 50
        Assert.Equal(15700, p.EmCompensacao.CartoesCentavos);
        Assert.Equal(5000, p.EmCompensacao.ChequesCentavos);

        // A receber: dívida 150 − 40 = 110 (em dia) ; serviço 30 (vencido) ; em compensação 200
        Assert.Equal(11000, p.AReceber.EmDiaCentavos);
        Assert.Equal(3000, p.AReceber.VencidoCentavos);
        Assert.Equal(20700, p.AReceber.EmCompensacaoCentavos);
        Assert.Equal(34700, p.AReceber.TotalCentavos);

        // A pagar: aluguel 80
        Assert.Equal(8000, p.APagar.TotalCentavos);

        // Resultado: 10 peças vendidas (500) − custo 200 = lucro bruto 300 ; energia 120 ; troco inicial e sangria não entram
        // (a venda no crédito em 2x entra com o acréscimo de 7%: 107,00 em vez de 100,00)
        Assert.Equal(50700, p.Periodo.ReceitaBrutaCentavos);
        Assert.Equal(20000, p.Periodo.CustoMercadoriasCentavos);
        Assert.Equal(30700, p.Periodo.LucroBrutoCentavos);
        Assert.Equal(12000, p.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(18700, p.Periodo.ResultadoCentavos);
        Assert.Empty(p.Avisos);

        // Projeção a 30 dias: disponível 650 + a receber (dívida vence em 30 dias, serviço vencido, cheque em 30, débito amanhã,
        // crédito em 30) − aluguel 80  =  650 + 110 + 30 + 50 + 50 + 100 − 80
        Assert.Equal(65000 + 11000 + 3000 + 5000 + 5000 + 10700 - 8000, p.Projecao.PorDias[30]);
    }

    [Fact]
    public async Task Liquidar_o_cartao_move_o_dinheiro_para_a_conta_e_a_taxa_vira_despesa()
    {
        await PrepararAsync();
        var v = await VenderAsync(FormaPagamento.CartaoCredito, 2, parcelas: 2); // 100,00 + 7% de acréscimo do crédito em 2x = 107,00
        Assert.Equal(10700, (await CalcularAsync()).EmCompensacao.CartoesCentavos);

        await new LiquidacaoCartaoService(_banco.Fabrica, () => Agora).LiquidarAsync(v.Id, Agora, _bancoId, 300);

        var p = await CalcularAsync();
        Assert.Equal(0, p.EmCompensacao.CartoesCentavos);
        Assert.Equal(50000 + 10700 - 300, Saldo(p, _bancoId)); // a taxa já saiu da conta
        Assert.Equal(300, p.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(300, p.Periodo.DespesasPorCategoria["Taxas de cartão"]);
        Assert.Equal(10700 - 4000 - 300, p.Periodo.ResultadoCentavos); // 107 de venda (com o acréscimo) − 40 de custo − 3 de taxa
    }

    [Fact]
    public async Task Compensar_cheque_entra_na_conta_e_devolver_volta_a_ser_a_receber_vencido()
    {
        await PrepararAsync();
        var cheques = new ChequeService(_banco.Fabrica, () => Agora);
        await VenderAsync(FormaPagamento.Cheque, 1);
        await VenderAsync(FormaPagamento.Cheque, 2);
        var lista = await cheques.ListarAsync();

        await cheques.CompensarAsync(lista[0].Id, _bancoId, Agora);
        await cheques.DevolverAsync(lista[1].Id, "sem fundos");

        var p = await CalcularAsync();
        Assert.Equal(50000 + 5000, Saldo(p, _bancoId));
        Assert.Equal(0, p.EmCompensacao.ChequesCentavos);
        Assert.Equal(10000, p.AReceber.VencidoCentavos + p.AReceber.EmDiaCentavos); // o devolvido: 100,00 a cobrar
        Assert.Equal(15000, p.Periodo.ReceitaBrutaCentavos);
    }

    [Fact]
    public async Task Cheque_pago_pelo_cliente_conta_como_recebimento_e_nao_como_receita_de_novo()
    {
        await PrepararAsync();
        var cheques = new ChequeService(_banco.Fabrica, () => Agora);
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));
        await VenderAsync(FormaPagamento.APrazo, 2, maria.Id); // 100,00 de dívida, receita já reconhecida
        await _clientes.RegistrarPagamentoAsync(maria.Id, 10000, FormaPagamento.Cheque);
        var c = (await cheques.ListarAsync()).Single();

        var antes = await CalcularAsync();
        Assert.Equal(0, antes.AReceber.EmDiaCentavos);           // a dívida foi abatida
        Assert.Equal(10000, antes.EmCompensacao.ChequesCentavos); // mas o dinheiro ainda não veio

        await cheques.CompensarAsync(c.Id, _bancoId, Agora);

        var depois = await CalcularAsync();
        Assert.Equal(60000, Saldo(depois, _bancoId));
        Assert.Equal(10000, depois.Periodo.ReceitaBrutaCentavos);   // só a venda, uma vez
        Assert.Equal(6000, depois.Periodo.ResultadoCentavos);        // 100 − custo 40
    }

    [Fact]
    public async Task Compra_de_mercadoria_sai_do_caixa_mas_nao_pesa_no_resultado_duas_vezes()
    {
        await PrepararAsync();
        var compras = new CompraService(_banco.Fabrica, () => Agora);
        await compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(_produtoId, 10, 2000) },
            Pagamento: new PagamentoCompra(1, Agora, JaPago: true, Forma: FormaPagamento.Pix))); // 200,00 pagos no Pix
        await VenderAsync(FormaPagamento.Dinheiro, 2); // 100,00, custo 40,00

        var p = await CalcularAsync();

        Assert.Equal(50000 - 20000, Saldo(p, _bancoId));          // o dinheiro da compra saiu
        Assert.Equal(0, p.Periodo.DespesasOperacionaisCentavos);   // mas não é despesa do período
        Assert.Equal(6000, p.Periodo.ResultadoCentavos);           // lucro bruto 100 − 40
    }

    [Fact]
    public async Task Conta_cancelada_e_venda_cancelada_nao_entram()
    {
        await PrepararAsync();
        var financeiro = new FinanceiroService(_banco.Fabrica, () => Agora);
        var v = await VenderAsync(FormaPagamento.Dinheiro, 1);
        var lancs = await financeiro.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Errada", 9000, Agora.AddDays(2)));
        await _vendas.CancelarAsync(v.Id, "cliente desistiu");
        await financeiro.CancelarAsync(lancs[0].Id, "lançada errada");

        var p = await CalcularAsync();

        Assert.Equal(10000, Saldo(p, _gavetaId));
        Assert.Equal(0, p.APagar.TotalCentavos);
        Assert.Equal(0, p.Periodo.ReceitaBrutaCentavos);
    }

    [Fact]
    public async Task Pix_sem_conta_de_banco_cadastrada_vira_aviso_em_vez_de_sumir()
    {
        await _contas.GarantirContaPadraoAsync();
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 100)).Id;
        await VenderAsync(FormaPagamento.Pix, 1);

        var p = await CalcularAsync();

        var aviso = Assert.Single(p.Avisos);
        Assert.Equal("sem_conta", aviso.Codigo);
        Assert.Contains("R$ 50,00", aviso.Mensagem);
    }

    [Fact]
    public async Task Movimento_do_caixa_despesa_e_entrada_avulsa_entram_no_resultado()
    {
        await PrepararAsync();
        var caixa = new CaixaService(_banco.Fabrica, () => Agora);
        await caixa.AbrirAsync(5000);
        await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Despesa, 1500, "Café da equipe");
        await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.EntradaAvulsa, 2500, "Venda de sacola");
        await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Reforco, 3000, "Troco");

        var p = await CalcularAsync();

        Assert.Equal(10000 - 1500 + 2500 + 3000, Saldo(p, _gavetaId));
        Assert.Equal(1500, p.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(2500, p.Periodo.OutrasReceitasCentavos);
        Assert.Equal(2500 - 1500, p.Periodo.ResultadoCentavos); // reforço não é receita
    }

    [Fact]
    public async Task Transferencia_cancelada_desfaz_o_efeito_nos_saldos()
    {
        await PrepararAsync();
        var t = await _contas.TransferirAsync(new DadosTransferencia(_gavetaId, _bancoId, 4000, Agora));
        Assert.Equal(6000, Saldo(await CalcularAsync(), _gavetaId));

        await _contas.CancelarTransferenciaAsync(t.Id, "lançada por engano");

        var p = await CalcularAsync();
        Assert.Equal(10000, Saldo(p, _gavetaId));
        Assert.Equal(50000, Saldo(p, _bancoId));
    }

    [Fact]
    public async Task Loja_nova_sem_nada_cadastrado_tem_a_gaveta_zerada_e_nenhum_aviso()
    {
        var p = await CalcularAsync();

        Assert.Single(p.Contas);
        Assert.Equal(0, p.SaldoDisponivelCentavos);
        Assert.Empty(p.Avisos);
        Assert.Equal(0, p.Periodo.ResultadoCentavos);
    }
}
