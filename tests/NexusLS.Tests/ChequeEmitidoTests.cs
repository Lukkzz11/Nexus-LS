using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Cheque emitido: pagar uma conta a pagar com cheque. Pendente = a conta segue em aberto, mas o motor a trata como saída
/// "em compensação" (não mexe no saldo). Compensar paga a conta; cancelar (com motivo) devolve a conta para "em aberto".
/// </summary>
[Collection("OperadorAtual")]
public sealed class ChequeEmitidoTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);
    private static readonly DateTime Inicio = new(2026, 10, 1);
    private static readonly DateTime Fim = new(2026, 12, 31);

    private readonly BancoDeTeste _banco = new();
    private readonly FinanceiroService _fin;
    private readonly ChequeEmitidoService _cheques;
    private readonly ContaFinanceiraService _contas;
    private readonly PosicaoFinanceiraService _posicao;
    private readonly DemonstrativoService _dre;
    private readonly AtividadeService _atividades;
    private ContaFinanceira _banco1 = null!;

    public ChequeEmitidoTests()
    {
        OperadorAtual.Nome = "Ana";
        _fin = new FinanceiroService(_banco.Fabrica, () => Agora);
        _cheques = new ChequeEmitidoService(_banco.Fabrica, () => Agora);
        _contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        _posicao = new PosicaoFinanceiraService(_banco.Fabrica, () => Agora);
        _dre = new DemonstrativoService(_banco.Fabrica, () => Agora);
        _atividades = new AtividadeService(_banco.Fabrica);
    }

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static DateTime Dia(int d) => new(2026, 10, d);

    private async Task PrepararBancoAsync(long saldoInicial = 100000) =>
        _banco1 = await _contas.CriarAsync(new DadosConta("Banco A", TipoContaFin.Banco, saldoInicial, Dia(1), Padrao: true));

    private async Task<Lancamento> ContaAsync(long valor = 30000, string descricao = "Fornecedor X", int venc = 25) =>
        (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, descricao, valor, Dia(venc), "Fornecedores", "Malhas Ltda", null)))[0];

    private Task<PosicaoFinanceira> PosAsync() => _posicao.CalcularAsync(Inicio, Fim);

    private Task<ChequeEmitido> EmitirAsync(Lancamento l, int bomPara = 28) =>
        _cheques.EmitirAsync(new DadosChequeEmitido(l.Id, _banco1.Id, Dia(bomPara), "000123", "Banco A", null));

    [Fact]
    public async Task Emitir_nao_paga_a_conta_mas_a_coloca_em_compensacao_sem_mexer_no_saldo()
    {
        await PrepararBancoAsync();
        var l = await ContaAsync();
        var antes = await PosAsync();

        var cheque = await EmitirAsync(l);

        Assert.Equal(SituacaoChequeEmitido.Pendente, cheque.Situacao);
        Assert.Equal(30000, cheque.ValorCentavos);
        Assert.Equal("Malhas Ltda", cheque.Favorecido);                 // vem da contraparte da conta
        var pos = await PosAsync();
        Assert.Equal(antes.SaldoDisponivelCentavos, pos.SaldoDisponivelCentavos);   // saldo intacto
        Assert.Equal(30000, pos.EmCompensacao.ChequesEmitidosCentavos);             // saída em compensação
        Assert.Equal(0, pos.EmCompensacao.TotalCentavos);                           // não é entrada
        Assert.Equal(30000, pos.APagar.EmCompensacaoCentavos);
        Assert.Equal(0, pos.APagar.EmDiaCentavos + pos.APagar.VencidoCentavos);     // saiu de "a pagar" comum
        Assert.Equal(0, pos.Periodo.DespesasOperacionaisCentavos);                  // ainda não é despesa realizada
        Assert.Null((await _fin.ListarAsync(new FiltroLancamentos(Inicio, Fim))).Single().PagoEm);
    }

    [Fact]
    public async Task Cheque_pendente_aparece_na_projecao_na_data_do_bom_para()
    {
        await PrepararBancoAsync();
        var l = await ContaAsync(venc: 25);
        await EmitirAsync(l, bomPara: 28);

        var pos = await PosAsync();
        Assert.Equal(100000 - 30000, pos.Projecao.TotalCentavos);
        Assert.Equal(100000 - 30000, pos.Projecao.PorDias[30]);   // hoje 20/10 + 30 dias alcança 28/10
    }

    [Fact]
    public async Task Compensar_paga_a_conta_na_conta_do_cheque_e_baixa_o_saldo_e_vira_despesa()
    {
        await PrepararBancoAsync();
        var l = await ContaAsync();
        var cheque = await EmitirAsync(l);

        await _cheques.CompensarAsync(cheque.Id, Dia(20));

        var conta = (await _fin.ListarAsync(new FiltroLancamentos(Inicio, Fim))).Single();
        Assert.Equal(Dia(20), conta.PagoEm);
        Assert.Equal(FormaPagamento.Cheque, conta.FormaPagamento);
        Assert.Equal(_banco1.Id, conta.ContaId);
        var pos = await PosAsync();
        Assert.Equal(100000 - 30000, pos.SaldoDisponivelCentavos);
        Assert.Equal(70000, pos.Contas.Single(c => c.Id == _banco1.Id).DisponivelCentavos);
        Assert.Equal(0, pos.EmCompensacao.ChequesEmitidosCentavos);
        Assert.Equal(30000, pos.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(SituacaoChequeEmitido.Compensado, (await _cheques.ListarAsync()).Single().Cheque.Situacao);
    }

    [Fact]
    public async Task Cancelar_exige_motivo_devolve_a_conta_para_em_aberto_e_permite_emitir_outro()
    {
        await PrepararBancoAsync();
        var l = await ContaAsync();
        var cheque = await EmitirAsync(l);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.CancelarAsync(cheque.Id, "ab"));
        await _cheques.CancelarAsync(cheque.Id, "cheque sustado");

        var c = (await _cheques.ListarAsync()).Single().Cheque;
        Assert.Equal(SituacaoChequeEmitido.Cancelado, c.Situacao);
        Assert.Equal("cheque sustado", c.MotivoCancelamento);
        var pos = await PosAsync();
        Assert.Equal(0, pos.EmCompensacao.ChequesEmitidosCentavos);
        Assert.Equal(30000, pos.APagar.EmDiaCentavos + pos.APagar.VencidoCentavos);   // voltou a ser "a pagar" comum
        await EmitirAsync(l);                                                         // pode emitir outro
    }

    [Fact]
    public async Task Regras_de_emissao()
    {
        await PrepararBancoAsync();
        var gaveta = await _contas.CriarAsync(new DadosConta("Cofre", TipoContaFin.Caixa));
        var l = await ContaAsync();

        // só conta de banco
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _cheques.EmitirAsync(new DadosChequeEmitido(l.Id, gaveta.Id, Dia(28))));

        await EmitirAsync(l);
        // não duplica enquanto houver um pendente
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => EmitirAsync(l));

        // conta já paga, cancelada ou a receber não recebe cheque
        var paga = await ContaAsync(descricao: "Já paga");
        await _fin.PagarAsync(paga.Id, FormaPagamento.Dinheiro, Dia(20));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => EmitirAsync(paga));

        var cancelada = await ContaAsync(descricao: "Cancelada");
        await _fin.CancelarAsync(cancelada.Id, "engano");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => EmitirAsync(cancelada));

        var receber = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Serviço", 5000, Dia(25), null, null, null)))[0];
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => EmitirAsync(receber));
    }

    [Fact]
    public async Task Conta_com_cheque_pendente_nao_pode_ser_paga_editada_cancelada_retirada_nem_corrigida()
    {
        await PrepararBancoAsync();
        var l = await ContaAsync();
        await EmitirAsync(l);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.PagarAsync(l.Id, FormaPagamento.Pix, Dia(20)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.EditarAsync(l.Id, "x", 1000, Dia(25), null, null, null));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CancelarAsync(l.Id, "engano"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.RetirarAsync(l.Id, "engano"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _fin.CorrigirAsync(l.Id, new DadosCorrecaoLancamento("Novo", 1000, Dia(25), null, null, null), "valor errado"));
    }

    [Fact]
    public async Task Conta_paga_por_cheque_compensado_nao_estorna_nem_cancela_o_cheque()
    {
        await PrepararBancoAsync();
        var l = await ContaAsync();
        var cheque = await EmitirAsync(l);
        await _cheques.CompensarAsync(cheque.Id, Dia(20));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.EstornarPagamentoAsync(l.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.CancelarAsync(cheque.Id, "desfazer"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.CompensarAsync(cheque.Id, Dia(20)));

        // o caminho para desfazer é retirar a conta (fica o histórico) e o saldo volta
        await _fin.RetirarAsync(l.Id, "pagamento lançado errado");
        Assert.Equal(100000, (await PosAsync()).SaldoDisponivelCentavos);
    }

    [Fact]
    public async Task Datas_da_compensacao_sao_validadas()
    {
        await PrepararBancoAsync();
        var cheque = await EmitirAsync(await ContaAsync());

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.CompensarAsync(cheque.Id, Dia(21)));   // futuro
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.CompensarAsync(cheque.Id, Dia(19)));   // antes de emitir
    }

    [Fact]
    public async Task Emitir_compensar_e_cancelar_gravam_a_trilha_em_atividades()
    {
        await PrepararBancoAsync();
        var c1 = await EmitirAsync(await ContaAsync());
        await _cheques.CompensarAsync(c1.Id, Dia(20));
        var c2 = await EmitirAsync(await ContaAsync(descricao: "Outra"));
        await _cheques.CancelarAsync(c2.Id, "emitido por engano");

        var tipos = (await _atividades.ListarAsync()).Select(a => a.Tipo).ToList();
        Assert.Equal(2, tipos.Count(t => t == TipoAtividade.ChequeEmitido));
        Assert.Contains(TipoAtividade.ChequeEmitidoCompensado, tipos);
        Assert.Contains(TipoAtividade.ChequeEmitidoCancelado, tipos);
        var emitido = (await _atividades.ListarAsync()).First(a => a.Tipo == TipoAtividade.ChequeEmitido);
        Assert.Equal("Ana", emitido.Operador);
        Assert.Contains("Cheque emitido #", emitido.Referencia);
    }

    [Fact]
    public async Task Resultado_do_financeiro_e_do_dre_batem_e_cheque_pendente_nao_conta_como_despesa()
    {
        await PrepararBancoAsync();
        var l = await ContaAsync();
        var cheque = await EmitirAsync(l);

        var resumo = await _fin.ResumoAsync(Dia(1), Dia(31));
        var dre = await _dre.MontarAsync(Dia(1), Dia(31));
        Assert.Equal(dre.ResultadoCentavos, resumo.ResultadoCentavos);

        await _cheques.CompensarAsync(cheque.Id, Dia(20));
        resumo = await _fin.ResumoAsync(Dia(1), Dia(31));
        dre = await _dre.MontarAsync(Dia(1), Dia(31));
        Assert.Equal(dre.ResultadoCentavos, resumo.ResultadoCentavos);
        Assert.Equal(-30000, dre.ResultadoCentavos);
    }

    [Fact]
    public async Task Contas_para_cheque_lista_so_o_que_esta_em_aberto_e_sem_cheque_pendente()
    {
        await PrepararBancoAsync();
        var a = await ContaAsync(descricao: "A", venc: 22);
        var b = await ContaAsync(descricao: "B", venc: 21);
        var c = await ContaAsync(descricao: "C", venc: 23);
        await _fin.PagarAsync(c.Id, FormaPagamento.Dinheiro, Dia(20));
        await EmitirAsync(a);

        var lista = await _cheques.ContasParaChequeAsync();

        Assert.Equal(new[] { b.Id }, lista.Select(x => x.Id).ToArray());
    }
}
