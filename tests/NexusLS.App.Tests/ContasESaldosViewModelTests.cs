using NexusLS.App.ViewModels;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class ContasESaldosViewModelTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);
    private readonly BancoDeTeste _banco = new();
    private readonly ContaFinanceiraService _contas;
    private readonly LiquidacaoCartaoService _cartoes;
    private readonly ChequeService _cheques;
    private readonly PosicaoFinanceiraService _posicao;
    private readonly VendaService _vendas;
    private int _produtoId;

    public ContasESaldosViewModelTests()
    {
        _contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        _cartoes = new LiquidacaoCartaoService(_banco.Fabrica, () => Agora);
        _cheques = new ChequeService(_banco.Fabrica, () => Agora);
        _posicao = new PosicaoFinanceiraService(_banco.Fabrica, () => Agora);
        _vendas = new VendaService(_banco.Fabrica, () => Agora);
        OperadorAtual.Nome = null;
    }

    public void Dispose() => _banco.Dispose();

    private async Task<Venda> VenderAsync(FormaPagamento forma, int quantidade = 1)
    {
        if (_produtoId == 0)
            _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 50)).Id;
        return await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, quantidade) }, 0, forma,
            ValorRecebidoCentavos: forma == FormaPagamento.Dinheiro ? 5000L * quantidade : null));
    }

    private ContasESaldosViewModel Criar() => new(_posicao, _contas, _cartoes, _cheques, () => Agora);

    private static async Task EsperarTudoAsync(ContasESaldosViewModel vm)
    {
        await vm.Saldos.Carregamento;
        await vm.Contas.Carregamento;
        await vm.Cartoes.Carregamento;
        await vm.Cheques.Carregamento;
    }

    [Fact]
    public async Task Visao_geral_mostra_saldo_a_receber_e_resultado()
    {
        await VenderAsync(FormaPagamento.Dinheiro, 2);   // R$ 100,00 na gaveta
        await VenderAsync(FormaPagamento.CartaoCredito); // R$ 50,00 + 5% de acréscimo = R$ 52,50 a liquidar
        var vm = Criar();
        await EsperarTudoAsync(vm);

        Assert.Equal(Dinheiro.Formatar(10000), vm.Saldos.SaldoDisponivelTexto);
        Assert.Equal(Dinheiro.Formatar(5250), vm.Saldos.EmCompensacaoTexto);
        Assert.Equal(Dinheiro.Formatar(5250), vm.Saldos.AReceberTexto);
        Assert.Equal(Dinheiro.Formatar(15250), vm.Saldos.ReceitaBrutaTexto);
        Assert.Equal(Dinheiro.Formatar(15250 - 6000), vm.Saldos.ResultadoTexto);
        Assert.False(vm.Saldos.ResultadoNegativo);
        Assert.Single(vm.Saldos.Contas);
    }

    [Fact]
    public async Task Cheque_emitido_pela_tela_fica_em_compensacao_e_ao_compensar_baixa_o_saldo()
    {
        var banco = await _contas.CriarAsync(new DadosConta("Banco A", TipoContaFin.Banco, 100000, Agora.Date.AddDays(-5), Padrao: true));
        var fin = new FinanceiroService(_banco.Fabrica, () => Agora);
        var conta = (await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Tecidos", 30000, Agora.Date.AddDays(5), "Fornecedores", "Malhas Ltda")))[0];
        var vm = new ContasESaldosViewModel(_posicao, _contas, _cartoes, _cheques, () => Agora, new ChequeEmitidoService(_banco.Fabrica, () => Agora));
        await EsperarTudoAsync(vm);
        var emitidos = vm.ChequesEmitidos!;
        await emitidos.Carregamento;
        Assert.True(vm.TemChequesEmitidos);

        // sem escolher a conta a pagar, mostra erro
        await emitidos.EmitirCommand.ExecuteAsync(null);
        Assert.True(emitidos.MensagemEhErro);

        emitidos.FormConta = emitidos.ContasAPagar.Single(o => o.Conta.Id == conta.Id);
        Assert.Equal("Malhas Ltda", emitidos.FormFavorecido);
        emitidos.FormNumero = "000123";
        await emitidos.EmitirCommand.ExecuteAsync(null);
        Assert.False(emitidos.MensagemEhErro, emitidos.Mensagem);
        var linha = Assert.Single(emitidos.Cheques);
        Assert.Equal("Pode compensar", linha.Situacao);
        Assert.Empty(emitidos.ContasAPagar);                         // a conta já tem cheque pendente

        await vm.Saldos.Carregamento;
        await vm.Saldos.CarregarAsync();
        Assert.Equal(Dinheiro.Formatar(100000), vm.Saldos.SaldoDisponivelTexto);
        Assert.Contains("Cheques emitidos", vm.Saldos.EmCompensacaoDetalhe);

        emitidos.Selecionado = emitidos.Cheques.Single();
        emitidos.DataCompensacao = Agora.Date;
        await emitidos.CompensarCommand.ExecuteAsync(null);
        Assert.False(emitidos.MensagemEhErro, emitidos.Mensagem);
        await vm.Saldos.CarregarAsync();
        Assert.Equal(Dinheiro.Formatar(70000), vm.Saldos.SaldoDisponivelTexto);

        // cancelar pede o motivo
        var outra = (await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aviamentos", 5000, Agora.Date.AddDays(5))))[0];
        await emitidos.CarregarCommand.ExecuteAsync(null);
        emitidos.FormConta = emitidos.ContasAPagar.Single(o => o.Conta.Id == outra.Id);
        await emitidos.EmitirCommand.ExecuteAsync(null);
        emitidos.Selecionado = emitidos.Cheques.Single(c => c.Situacao == "Pode compensar");
        emitidos.PedirMotivo = _ => null;
        await emitidos.CancelarCommand.ExecuteAsync(null);
        Assert.Contains(emitidos.Cheques, c => c.Situacao == "Pode compensar");
        emitidos.PedirMotivo = _ => "emitido por engano";
        await emitidos.CancelarCommand.ExecuteAsync(null);
        Assert.DoesNotContain(emitidos.Cheques, c => c.Situacao == "Pode compensar");
        Assert.Contains(emitidos.ContasAPagar, o => o.Conta.Id == outra.Id);
    }

    [Fact]
    public async Task Periodo_invertido_mostra_erro_em_vez_de_numeros_errados()
    {
        var vm = Criar();
        await EsperarTudoAsync(vm);

        vm.Saldos.Ate = Agora.AddDays(-40);
        await vm.Saldos.CarregarAsync();

        Assert.True(vm.Saldos.MensagemEhErro);
        Assert.Contains("data final", vm.Saldos.Mensagem);
    }

    [Fact]
    public async Task Venda_pix_sem_conta_de_banco_mostra_aviso_na_visao_geral()
    {
        await VenderAsync(FormaPagamento.Pix);
        var vm = Criar();
        await EsperarTudoAsync(vm);

        Assert.True(vm.Saldos.TemAvisos);
        Assert.Contains("conta", vm.Saldos.Avisos.Single());
    }

    [Fact]
    public async Task Criar_conta_e_transferir_atualiza_a_visao_geral()
    {
        var vm = Criar();
        await EsperarTudoAsync(vm);

        vm.Contas.NovaCommand.Execute(null);
        vm.Contas.Nome = "Banco";
        vm.Contas.EhBanco = true;
        vm.Contas.SaldoInicialTexto = "500,00";
        await vm.Contas.SalvarCommand.ExecuteAsync(null);
        Assert.False(vm.Contas.MensagemEhErro, vm.Contas.Mensagem);
        await vm.Saldos.CarregarAsync();

        Assert.Equal(2, vm.Saldos.Contas.Count);
        Assert.Equal(Dinheiro.Formatar(50000), vm.Saldos.SaldoDisponivelTexto);

        await VenderAsync(FormaPagamento.Dinheiro);     // R$ 50,00 na gaveta
        vm.Contas.TransfOrigem = vm.Contas.ContasAtivas.Single(c => c.Tipo == TipoContaFin.Caixa);
        vm.Contas.TransfDestino = vm.Contas.ContasAtivas.Single(c => c.Tipo == TipoContaFin.Banco);
        vm.Contas.TransfValorTexto = "30,00";
        await vm.Contas.TransferirCommand.ExecuteAsync(null);
        Assert.False(vm.Contas.MensagemEhErro, vm.Contas.Mensagem);
        await vm.Saldos.CarregarAsync();

        Assert.Equal(Dinheiro.Formatar(50000 + 5000), vm.Saldos.SaldoDisponivelTexto); // transferir não muda o total
        Assert.Single(vm.Contas.Transferencias);
    }

    [Fact]
    public async Task Transferencia_com_dados_faltando_mostra_mensagem_clara()
    {
        var vm = Criar();
        await EsperarTudoAsync(vm);

        await vm.Contas.TransferirCommand.ExecuteAsync(null);
        Assert.True(vm.Contas.MensagemEhErro);
        Assert.Contains("conta", vm.Contas.Mensagem);

        vm.Contas.TransfOrigem = vm.Contas.ContasAtivas[0];
        vm.Contas.TransfDestino = vm.Contas.ContasAtivas[0];
        vm.Contas.TransfValorTexto = "abc";
        await vm.Contas.TransferirCommand.ExecuteAsync(null);
        Assert.Contains("valor", vm.Contas.Mensagem);
    }

    [Fact]
    public async Task Liquidar_cartao_pela_tela_tira_da_lista_e_entra_no_saldo()
    {
        await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        var venda = await VenderAsync(FormaPagamento.CartaoDebito);
        var vm = Criar();
        await EsperarTudoAsync(vm);
        Assert.Single(vm.Cartoes.Aliquidar);

        vm.Cartoes.Selecionado = vm.Cartoes.Aliquidar.Single();
        vm.Cartoes.TaxaTexto = "1,50";
        await vm.Cartoes.LiquidarCommand.ExecuteAsync(null);
        Assert.False(vm.Cartoes.MensagemEhErro, vm.Cartoes.Mensagem);
        await vm.Saldos.CarregarAsync();

        Assert.Empty(vm.Cartoes.Aliquidar);
        Assert.Equal(venda.Id, vm.Cartoes.Liquidados.Single().VendaId);
        Assert.Equal(Dinheiro.Formatar(5000 - 150), vm.Saldos.SaldoDisponivelTexto);
        Assert.Equal(Dinheiro.Formatar(0), vm.Saldos.EmCompensacaoTexto);
    }

    [Fact]
    public async Task Liquidar_sem_escolher_a_venda_orienta_a_pessoa()
    {
        await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        await VenderAsync(FormaPagamento.CartaoDebito);
        var vm = Criar();
        await EsperarTudoAsync(vm);

        await vm.Cartoes.LiquidarCommand.ExecuteAsync(null);

        Assert.True(vm.Cartoes.MensagemEhErro);
        Assert.Contains("venda", vm.Cartoes.Mensagem);
    }

    [Fact]
    public async Task Compensar_cheque_pela_tela_entra_na_conta_escolhida()
    {
        var banco = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        await VenderAsync(FormaPagamento.Cheque);
        var vm = Criar();
        await EsperarTudoAsync(vm);
        Assert.Single(vm.Cheques.Cheques);
        Assert.Equal(banco.Id, vm.Cheques.ContaCompensacao!.Id);

        vm.Cheques.Selecionado = vm.Cheques.Cheques.Single();
        await vm.Cheques.CompensarCommand.ExecuteAsync(null);
        Assert.False(vm.Cheques.MensagemEhErro, vm.Cheques.Mensagem);
        await vm.Saldos.CarregarAsync();

        Assert.Empty(vm.Cheques.Cheques); // saiu dos pendentes
        Assert.Equal(Dinheiro.Formatar(5000), vm.Saldos.SaldoDisponivelTexto);
    }

    [Fact]
    public async Task Devolver_cheque_pede_motivo_e_cancelar_a_pergunta_nao_faz_nada()
    {
        await VenderAsync(FormaPagamento.Cheque);
        var vm = Criar();
        await EsperarTudoAsync(vm);
        vm.Cheques.Selecionado = vm.Cheques.Cheques.Single();

        vm.Cheques.PedirMotivo = _ => null; // a pessoa clicou em Voltar
        await vm.Cheques.DevolverCommand.ExecuteAsync(null);
        Assert.Single(vm.Cheques.Cheques);

        vm.Cheques.PedirMotivo = _ => "sem fundos";
        await vm.Cheques.DevolverCommand.ExecuteAsync(null);
        Assert.Empty(vm.Cheques.Cheques);
        Assert.Equal(SituacaoCheque.Devolvido, (await _cheques.ListarAsync()).Single().Situacao);
    }

    [Fact]
    public async Task Cheque_avulso_novo_aparece_na_lista_e_valor_invalido_e_recusado()
    {
        var vm = Criar();
        await EsperarTudoAsync(vm);

        vm.Cheques.NovoCommand.Execute(null);
        vm.Cheques.FormValorTexto = "xx";
        await vm.Cheques.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.Cheques.MensagemEhErro);

        vm.Cheques.FormValorTexto = "120,00";
        vm.Cheques.FormEmitente = "João";
        await vm.Cheques.SalvarCommand.ExecuteAsync(null);

        Assert.False(vm.Cheques.MensagemEhErro, vm.Cheques.Mensagem);
        Assert.Equal("João", vm.Cheques.Cheques.Single().Emitente);
    }
}
