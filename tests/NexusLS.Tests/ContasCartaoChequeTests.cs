using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class ContasCartaoChequeTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);

    private readonly BancoDeTeste _banco = new();
    private readonly ContaFinanceiraService _contas;
    private readonly LiquidacaoCartaoService _cartoes;
    private readonly ChequeService _cheques;
    private readonly VendaService _vendas;
    private readonly ProdutoService _produtos;

    public ContasCartaoChequeTests()
    {
        _contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        _cartoes = new LiquidacaoCartaoService(_banco.Fabrica, () => Agora);
        _cheques = new ChequeService(_banco.Fabrica, () => Agora);
        _vendas = new VendaService(_banco.Fabrica, () => Agora);
        _produtos = new ProdutoService(_banco.Fabrica);
        OperadorAtual.Nome = null;
    }

    public void Dispose() => _banco.Dispose();

    private int? _produtoId;

    private async Task<Venda> VenderAsync(FormaPagamento forma, int quantidade = 2, int parcelas = 1)
    {
        _produtoId ??= (await _produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 50)).Id;
        return await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId.Value, quantidade) }, 0, forma, Parcelas: parcelas));
    }

    // ---------- contas ----------

    [Fact]
    public async Task Garantir_conta_padrao_cria_a_gaveta_uma_vez_so()
    {
        await _contas.GarantirContaPadraoAsync();
        await _contas.GarantirContaPadraoAsync();

        var contas = await _contas.ListarAsync();
        var gaveta = Assert.Single(contas);
        Assert.Equal(ContaFinanceiraService.NomeDaGaveta, gaveta.Nome);
        Assert.Equal(TipoContaFin.Caixa, gaveta.Tipo);
    }

    [Fact]
    public async Task Primeira_conta_de_banco_vira_padrao_e_so_uma_fica_padrao()
    {
        var a = await _contas.CriarAsync(new DadosConta("Banco A", TipoContaFin.Banco));
        var b = await _contas.CriarAsync(new DadosConta("Banco B", TipoContaFin.Banco));
        Assert.True(a.Padrao);
        Assert.False(b.Padrao);

        await _contas.EditarAsync(b.Id, new DadosConta("Banco B", TipoContaFin.Banco, Padrao: true));

        var lista = await _contas.ListarAsync();
        Assert.Equal("Banco B", Assert.Single(lista, c => c.Padrao).Nome);
    }

    [Fact]
    public async Task Nome_repetido_e_recusado_e_conta_de_caixa_nunca_e_padrao_de_banco()
    {
        await _contas.CriarAsync(new DadosConta("Nubank", TipoContaFin.Banco));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.CriarAsync(new DadosConta("nubank", TipoContaFin.Banco)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.CriarAsync(new DadosConta("x", TipoContaFin.Banco)));

        var cofre = await _contas.CriarAsync(new DadosConta("Cofre", TipoContaFin.Caixa, Padrao: true));
        Assert.False(cofre.Padrao);
    }

    [Fact]
    public async Task Desativar_a_padrao_passa_o_padrao_para_outra_e_a_ultima_de_caixa_nao_sai()
    {
        await _contas.GarantirContaPadraoAsync();
        var a = await _contas.CriarAsync(new DadosConta("Banco A", TipoContaFin.Banco));
        var b = await _contas.CriarAsync(new DadosConta("Banco B", TipoContaFin.Banco));

        await _contas.DesativarAsync(a.Id);

        Assert.True((await _contas.ListarAsync()).Single(c => c.Id == b.Id).Padrao);
        var gaveta = (await _contas.ListarAsync()).Single(c => c.Tipo == TipoContaFin.Caixa);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.DesativarAsync(gaveta.Id));
    }

    [Fact]
    public async Task Alterar_conta_deixa_rastro_na_trilha()
    {
        var a = await _contas.CriarAsync(new DadosConta("Banco A", TipoContaFin.Banco, 1000));
        await _contas.EditarAsync(a.Id, new DadosConta("Banco A", TipoContaFin.Banco, 2500));

        var atividades = await new AtividadeService(_banco.Fabrica).ListarAsync();
        Assert.Contains(atividades, x => x.Tipo == TipoAtividade.ContaAlterada && x.Descricao.Contains("R$ 25,00"));
    }

    // ---------- transferências ----------

    [Fact]
    public async Task Transferir_valida_valor_contas_e_data()
    {
        var a = await _contas.CriarAsync(new DadosConta("Gaveta", TipoContaFin.Caixa));
        var b = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.TransferirAsync(new DadosTransferencia(a.Id, b.Id, 0, Agora)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.TransferirAsync(new DadosTransferencia(a.Id, a.Id, 100, Agora)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.TransferirAsync(new DadosTransferencia(a.Id, b.Id, 100, Agora.AddDays(1))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.TransferirAsync(new DadosTransferencia(a.Id, 999, 100, Agora)));

        var t = await _contas.TransferirAsync(new DadosTransferencia(a.Id, b.Id, 5000, Agora, "Depósito"));
        Assert.Equal(5000, t.ValorCentavos);
        Assert.Equal(Agora.Date, t.Data);
    }

    [Fact]
    public async Task Cancelar_transferencia_exige_motivo_registra_e_nao_repete()
    {
        var a = await _contas.CriarAsync(new DadosConta("Gaveta", TipoContaFin.Caixa));
        var b = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        var t = await _contas.TransferirAsync(new DadosTransferencia(a.Id, b.Id, 5000, Agora));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.CancelarTransferenciaAsync(t.Id, "x"));
        await _contas.CancelarTransferenciaAsync(t.Id, "lançada errada");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _contas.CancelarTransferenciaAsync(t.Id, "de novo"));

        Assert.Contains(await new AtividadeService(_banco.Fabrica).ListarAsync(), x => x.Tipo == TipoAtividade.TransferenciaCancelada);
        Assert.True((await _contas.ListarTransferenciasAsync(Agora.AddDays(-1), Agora)).Single().Cancelada);
    }

    // ---------- cartão ----------

    [Fact]
    public async Task Venda_no_cartao_aparece_a_liquidar_com_a_previsao_certa()
    {
        var deb = await VenderAsync(FormaPagamento.CartaoDebito);
        var cred = await VenderAsync(FormaPagamento.CartaoCredito, parcelas: 3);
        await VenderAsync(FormaPagamento.Pix);

        var lista = await _cartoes.ListarAliquidarAsync();

        Assert.Equal(2, lista.Count);
        Assert.Equal(Agora.Date.AddDays(1), lista.Single(c => c.VendaId == deb.Id).PrevisaoDeCaida);
        Assert.Equal(Agora.Date.AddDays(30), lista.Single(c => c.VendaId == cred.Id).PrevisaoDeCaida);
    }

    [Fact]
    public async Task Liquidar_tira_da_lista_e_estornar_devolve()
    {
        var banco = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        var v = await VenderAsync(FormaPagamento.CartaoCredito);

        await _cartoes.LiquidarAsync(v.Id, Agora, banco.Id, 300);
        Assert.Empty(await _cartoes.ListarAliquidarAsync());
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cartoes.LiquidarAsync(v.Id, Agora, banco.Id)); // já liquidada

        await _cartoes.EstornarAsync(v.Id, "caiu valor errado");
        Assert.Single(await _cartoes.ListarAliquidarAsync());
        Assert.Contains(await new AtividadeService(_banco.Fabrica).ListarAsync(), x => x.Tipo == TipoAtividade.LiquidacaoEstornada);
    }

    [Fact]
    public async Task Liquidar_valida_forma_taxa_data_e_conta()
    {
        var banco = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        var pix = await VenderAsync(FormaPagamento.Pix);
        var cartao = await VenderAsync(FormaPagamento.CartaoDebito); // total R$ 100,00

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cartoes.LiquidarAsync(pix.Id, Agora, banco.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cartoes.LiquidarAsync(cartao.Id, Agora, banco.Id, -1));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cartoes.LiquidarAsync(cartao.Id, Agora, banco.Id, 10001));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cartoes.LiquidarAsync(cartao.Id, Agora.AddDays(-1), banco.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cartoes.LiquidarAsync(cartao.Id, Agora.AddDays(1), banco.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cartoes.LiquidarAsync(cartao.Id, Agora, 999));
    }

    [Fact]
    public async Task Nao_cancela_venda_de_cartao_ja_liquidada_ate_estornar()
    {
        var banco = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        var v = await VenderAsync(FormaPagamento.CartaoDebito);
        await _cartoes.LiquidarAsync(v.Id, Agora, banco.Id);

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.CancelarAsync(v.Id, "cliente desistiu"));
        Assert.Contains("liquidado", erro.Message);

        await _cartoes.EstornarAsync(v.Id, "vou cancelar a venda");
        await _vendas.CancelarAsync(v.Id, "cliente desistiu");
        Assert.Empty(await _cartoes.ListarAliquidarAsync()); // venda cancelada não é "a liquidar"
    }

    // ---------- cheque ----------

    [Fact]
    public async Task Venda_no_cheque_cria_cheque_pendente_com_bom_para_em_30_dias()
    {
        var v = await VenderAsync(FormaPagamento.Cheque);

        var c = Assert.Single(await _cheques.ListarAsync());
        Assert.Equal(SituacaoCheque.Pendente, c.Situacao);
        Assert.Equal(v.TotalCentavos, c.ValorCentavos);
        Assert.Equal(v.Id, c.VendaId);
        Assert.Equal(Agora.Date.AddDays(30), c.BomPara);
    }

    [Fact]
    public async Task Pagamento_de_cliente_no_cheque_cria_cheque_ligado_ao_pagamento()
    {
        var clientes = new ClienteService(_banco.Fabrica, () => Agora);
        var cliente = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        var p = await _produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 50);
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2) }, 0, FormaPagamento.APrazo, ClienteId: cliente.Id));

        var pagamento = await clientes.RegistrarPagamentoAsync(cliente.Id, 4000, FormaPagamento.Cheque);

        var c = Assert.Single(await _cheques.ListarAsync());
        Assert.Equal(pagamento.Id, c.PagamentoClienteId);
        Assert.Equal("Maria", c.Emitente);
        // E esse cheque não pode ser "devolvido": a dívida já foi abatida.
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.DevolverAsync(c.Id, "sem fundos"));
    }

    [Fact]
    public async Task Compensar_marca_a_conta_e_a_data_e_nao_deixa_repetir()
    {
        var banco = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        await VenderAsync(FormaPagamento.Cheque);
        var c = (await _cheques.ListarAsync()).Single();

        await _cheques.CompensarAsync(c.Id, banco.Id, Agora);

        var depois = (await _cheques.ListarAsync()).Single();
        Assert.Equal(SituacaoCheque.Compensado, depois.Situacao);
        Assert.Equal(banco.Id, depois.ContaId);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.CompensarAsync(c.Id, banco.Id, Agora));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.CancelarAsync(c.Id, "tarde demais"));
    }

    [Fact]
    public async Task Devolver_exige_motivo_e_deixa_rastro()
    {
        await VenderAsync(FormaPagamento.Cheque);
        var c = (await _cheques.ListarAsync()).Single();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.DevolverAsync(c.Id, "ab"));
        await _cheques.DevolverAsync(c.Id, "sem fundos");

        Assert.Equal(SituacaoCheque.Devolvido, (await _cheques.ListarAsync()).Single().Situacao);
        Assert.Contains(await new AtividadeService(_banco.Fabrica).ListarAsync(), x => x.Tipo == TipoAtividade.ChequeDevolvido);
    }

    [Fact]
    public async Task Cancelar_venda_no_cheque_pendente_cancela_o_cheque_mas_compensado_trava()
    {
        var banco = await _contas.CriarAsync(new DadosConta("Banco", TipoContaFin.Banco));
        var v1 = await VenderAsync(FormaPagamento.Cheque);
        var v2 = await VenderAsync(FormaPagamento.Cheque);

        await _vendas.CancelarAsync(v1.Id, "cliente desistiu");
        Assert.Equal(SituacaoCheque.Cancelado, (await _cheques.ListarAsync()).Single(c => c.VendaId == v1.Id).Situacao);

        var c2 = (await _cheques.ListarAsync()).Single(c => c.VendaId == v2.Id);
        await _cheques.CompensarAsync(c2.Id, banco.Id, Agora);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.CancelarAsync(v2.Id, "cliente desistiu"));
    }

    [Fact]
    public async Task Cheque_avulso_so_edita_enquanto_pendente_e_o_valor_nao_muda()
    {
        var c = await _cheques.RegistrarAsync(new DadosCheque(8000, Agora.AddDays(10), "123", "Itaú", "João"));

        await _cheques.EditarAsync(c.Id, new DadosCheque(999999, Agora.AddDays(20), "124", "Itaú", "João S."));
        var depois = (await _cheques.ListarAsync()).Single();
        Assert.Equal(8000, depois.ValorCentavos);     // valor intocado
        Assert.Equal("124", depois.Numero);
        Assert.Equal(Agora.Date.AddDays(20), depois.BomPara);

        await _cheques.CancelarAsync(c.Id, "foi engano");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.EditarAsync(c.Id, new DadosCheque(8000, Agora, "1")));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cheques.RegistrarAsync(new DadosCheque(0, Agora)));
    }
}
