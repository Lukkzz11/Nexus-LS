using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Fase 2.8: corrigir/retirar lançamento com motivo (nunca apaga: cancela e cria o corrigido, com trilha na mesma transação)
/// e editar título a prazo (a venda não muda; a dívida continua recalculada por FIFO).
/// </summary>
[Collection("OperadorAtual")]
public sealed class CorrecaoFinanceiraTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);
    private static readonly DateTime Inicio = new(2026, 10, 1);
    private static readonly DateTime Fim = new(2026, 10, 31);

    private readonly BancoDeTeste _banco = new();
    private readonly FinanceiroService _fin;
    private readonly PosicaoFinanceiraService _posicao;
    private readonly TituloPrazoService _titulos;
    private readonly ClienteService _clientes;
    private readonly VendaService _vendas;
    private readonly DemonstrativoService _dre;
    private readonly AtividadeService _atividades;
    private int _produtoId;

    public CorrecaoFinanceiraTests()
    {
        OperadorAtual.Nome = "Ana";
        _fin = new FinanceiroService(_banco.Fabrica, () => Agora);
        _posicao = new PosicaoFinanceiraService(_banco.Fabrica, () => Agora);
        _titulos = new TituloPrazoService(_banco.Fabrica, () => Agora);
        _clientes = new ClienteService(_banco.Fabrica, () => Agora);
        _vendas = new VendaService(_banco.Fabrica, () => Agora);
        _dre = new DemonstrativoService(_banco.Fabrica, () => Agora);
        _atividades = new AtividadeService(_banco.Fabrica);
    }

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static DateTime Dia(int d) => new(2026, 10, d);

    private async Task<Lancamento> LancarAsync(long valor, bool pago, string descricao = "Conta de teste",
        TipoLancamento tipo = TipoLancamento.Despesa, int dia = 10, int parcelas = 1) =>
        (await _fin.CriarAsync(new DadosLancamento(tipo, descricao, valor, Dia(dia), "Outros", null, null, parcelas, pago, pago ? FormaPagamento.Dinheiro : null)))[0];

    private async Task<IReadOnlyList<Lancamento>> TodosAsync() => await _fin.ListarAsync(new FiltroLancamentos(Inicio, Fim));

    private async Task<long> SaldoAsync() => (await _posicao.CalcularAsync(Inicio, Fim)).SaldoDisponivelCentavos;

    // =================== retirar lançamento ===================

    [Fact]
    public async Task Retirar_lancamento_pago_cancela_com_motivo_quem_e_quando_e_o_valor_sai_do_saldo()
    {
        var l = await LancarAsync(30000, pago: true);
        Assert.Equal(-30000, await SaldoAsync());

        await _fin.RetirarAsync(l.Id, "lançamento de teste");

        var depois = (await TodosAsync()).Single();
        Assert.True(depois.Cancelado);
        Assert.Equal("lançamento de teste", depois.MotivoCancelamento);
        Assert.Equal("Ana", depois.CanceladoPor);
        Assert.Equal(Agora, depois.CanceladoEm);
        Assert.NotNull(depois.PagoEm); // o histórico do pagamento fica; só deixa de contar
        Assert.Equal(0, await SaldoAsync());
        Assert.Equal(0, (await _fin.ResumoAsync(Inicio, Fim)).DespesasPagasCentavos);
    }

    [Fact]
    public async Task Retirar_grava_a_trilha_em_Atividades()
    {
        var l = await LancarAsync(12345, pago: true, descricao: "Duplicado");

        await _fin.RetirarAsync(l.Id, "lançado duas vezes");

        var a = (await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.LancamentoRetirado))).Single();
        Assert.Equal("Ana", a.Operador);
        Assert.Contains("Duplicado", a.Descricao);
        Assert.Contains("lançado duas vezes", a.Descricao);
        Assert.Contains("já estava pago", a.Descricao);
        Assert.Equal($"Lançamento #{l.Id}", a.Referencia);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public async Task Retirar_sem_motivo_de_3_letras_e_recusado_e_nada_muda(string motivo)
    {
        var l = await LancarAsync(1000, pago: true);

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.RetirarAsync(l.Id, motivo));

        Assert.Contains("motivo", erro.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False((await TodosAsync()).Single().Cancelado);
        Assert.Empty(await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.LancamentoRetirado)));
    }

    [Fact]
    public async Task Retirar_duas_vezes_e_recusado()
    {
        var l = await LancarAsync(1000, pago: false);
        await _fin.RetirarAsync(l.Id, "engano");

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.RetirarAsync(l.Id, "de novo"));
        Assert.Single(await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.LancamentoRetirado)));
    }

    [Fact]
    public async Task Conta_de_compra_de_mercadoria_nao_se_retira_nem_se_corrige_por_aqui()
    {
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 10)).Id;
        await new CompraService(_banco.Fabrica, () => Agora).RegistrarAsync(new DadosCompra(
            new[] { new ItemCompraDados(_produtoId, 2, 2000) }, Pagamento: new PagamentoCompra(1, Dia(25))));
        var daCompra = (await TodosAsync()).Single(x => x.CompraId is not null);

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.RetirarAsync(daCompra.Id, "engano"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _fin.CorrigirAsync(daCompra.Id, new DadosCorrecaoLancamento("x", 100, Dia(25)), "engano"));

        Assert.Contains("compra", erro.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False((await TodosAsync()).Single(x => x.CompraId is not null).Cancelado);
    }

    // =================== corrigir lançamento ===================

    [Fact]
    public async Task Corrigir_cancela_o_original_e_cria_o_corrigido_ligado_a_ele()
    {
        var original = await LancarAsync(30000, pago: false, descricao: "Energia");

        var novo = await _fin.CorrigirAsync(original.Id,
            new DadosCorrecaoLancamento("Energia de outubro", 32050, Dia(12), "Energia", "CPFL"), "valor da fatura estava errado");

        var todos = await TodosAsync();
        Assert.Equal(2, todos.Count);
        var velho = todos.Single(l => l.Id == original.Id);
        Assert.True(velho.Cancelado);
        Assert.Equal("Corrigido: valor da fatura estava errado", velho.MotivoCancelamento);
        Assert.Equal("Ana", velho.CanceladoPor);
        Assert.Equal(30000, velho.ValorCentavos); // o original não é reescrito

        Assert.False(novo.Cancelado);
        Assert.Equal(original.Id, novo.CorrigeLancamentoId);
        Assert.Equal("Energia de outubro", novo.Descricao);
        Assert.Equal(32050, novo.ValorCentavos);
        Assert.Equal(Dia(12), novo.Vencimento);
        Assert.Equal("CPFL", novo.Contraparte);
        Assert.Equal("Ana", novo.Operador);
        Assert.Equal(32050, (await _fin.ResumoAsync(Inicio, Fim)).APagarCentavos); // só o corrigido conta
    }

    [Fact]
    public async Task Corrigir_lancamento_ja_pago_mantem_o_pagamento_e_o_saldo_reflete_so_o_valor_novo()
    {
        var original = await LancarAsync(30000, pago: true);
        Assert.Equal(-30000, await SaldoAsync());

        var novo = await _fin.CorrigirAsync(original.Id, new DadosCorrecaoLancamento("Conta de teste", 25000, Dia(10)), "valor digitado errado");

        Assert.Equal(original.PagoEm, novo.PagoEm);
        Assert.Equal(FormaPagamento.Dinheiro, novo.FormaPagamento);
        Assert.Equal(-25000, await SaldoAsync()); // não ficou -55.000 nem -30.000
        Assert.Equal(25000, (await _fin.ResumoAsync(Inicio, Fim)).DespesasPagasCentavos);
    }

    [Fact]
    public async Task Corrigir_pode_trocar_a_data_do_pagamento_e_a_classificacao()
    {
        var original = await LancarAsync(50000, pago: true, tipo: TipoLancamento.Receita, descricao: "Entrada");

        var novo = await _fin.CorrigirAsync(original.Id, new DadosCorrecaoLancamento("Entrada", 50000, Dia(10),
            Classificacao: ClassificacaoLancamento.Aporte, PagoEm: Dia(8)), "era aporte do dono");

        Assert.Equal(ClassificacaoLancamento.Aporte, novo.Classificacao);
        Assert.Equal(Dia(8), novo.PagoEm);
        var r = await _fin.ResumoAsync(Inicio, Fim);
        Assert.Equal(0, r.ReceitasRecebidasCentavos);
        Assert.Equal(50000, r.AportesCentavos);
    }

    [Fact]
    public async Task Corrigir_uma_parcela_mantem_o_grupo_e_o_numero_da_parcela()
    {
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Estoque", 9000, Dia(10), Parcelas: 3));
        var segunda = (await _fin.ListarAsync(new FiltroLancamentos(Inicio, new DateTime(2026, 12, 31)))).Single(l => l.Parcela == 2);

        var novo = await _fin.CorrigirAsync(segunda.Id, new DadosCorrecaoLancamento("Estoque (2/3)", 4000, Dia(15)), "desconto do fornecedor");

        Assert.Equal(segunda.GrupoId, novo.GrupoId);
        Assert.Equal(2, novo.Parcela);
        Assert.Equal(3, novo.TotalParcelas);
    }

    [Fact]
    public async Task Corrigir_grava_a_trilha_com_o_de_para_na_mesma_transacao()
    {
        var original = await LancarAsync(30000, pago: false, descricao: "Aluguel");

        await _fin.CorrigirAsync(original.Id, new DadosCorrecaoLancamento("Aluguel", 35000, Dia(10)), "reajuste");

        var a = (await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.LancamentoCorrigido))).Single();
        Assert.Equal("Ana", a.Operador);
        Assert.Contains(Dinheiro.Formatar(30000), a.Descricao);
        Assert.Contains(Dinheiro.Formatar(35000), a.Descricao);
        Assert.Contains("reajuste", a.Descricao);
    }

    [Fact]
    public async Task Correcao_invalida_nao_cancela_o_original_nem_cria_o_novo_nem_grava_trilha()
    {
        var original = await LancarAsync(30000, pago: true);

        // data de pagamento no futuro: recusada depois de montar tudo, e mesmo assim nada pode ter sido gravado
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CorrigirAsync(original.Id,
            new DadosCorrecaoLancamento("Conta de teste", 100, Dia(10), PagoEm: Agora.AddDays(5)), "teste"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CorrigirAsync(original.Id,
            new DadosCorrecaoLancamento("Conta de teste", 0, Dia(10)), "valor zero"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CorrigirAsync(original.Id,
            new DadosCorrecaoLancamento("Conta de teste", 100, Dia(10), Classificacao: ClassificacaoLancamento.Aporte), "classificação errada"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fin.CorrigirAsync(original.Id,
            new DadosCorrecaoLancamento("Conta de teste", 100, Dia(10)), "ab"));

        var todos = await TodosAsync();
        Assert.Single(todos);
        Assert.False(todos[0].Cancelado);
        Assert.Empty(await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.LancamentoCorrigido)));
        Assert.Equal(-30000, await SaldoAsync());
    }

    [Fact]
    public async Task Se_a_trilha_nao_puder_ser_gravada_a_correcao_inteira_e_desfeita()
    {
        var original = await LancarAsync(30000, pago: false);
        // sabotagem: a tabela de atividades some, então o registro da trilha falha dentro do mesmo SaveChanges
        await using (var db = _banco.CriarContexto())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE Atividades");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _fin.CorrigirAsync(original.Id, new DadosCorrecaoLancamento("Outro", 100, Dia(10)), "teste"));

        var todos = await TodosAsync();
        Assert.Single(todos);                 // o corrigido não existe
        Assert.False(todos[0].Cancelado);     // o original não foi cancelado
        Assert.Equal(30000, todos[0].ValorCentavos);
    }

    // =================== editar título a prazo ===================

    private async Task<(Cliente Maria, Venda V1, Venda V2)> CenarioDividaAsync()
    {
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 100)).Id;
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));
        var v1 = await Prazo(2, maria.Id); // 100,00
        var v2 = await Prazo(1, maria.Id); //  50,00
        return (maria, v1, v2);
    }

    private Task<Venda> Prazo(int qtd, int clienteId) =>
        _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, qtd) }, 0, FormaPagamento.APrazo, ClienteId: clienteId));

    [Fact]
    public async Task Editar_valor_do_titulo_recalcula_a_divida_do_cliente_e_nao_mexe_na_venda()
    {
        var (maria, v1, _) = await CenarioDividaAsync();
        await _clientes.RegistrarPagamentoAsync(maria.Id, 3000, FormaPagamento.Dinheiro);
        Assert.Equal(12000, (await _clientes.ContaAsync(maria.Id)).SaldoDevedorCentavos); // 150 − 30

        await _titulos.EditarAsync(v1.Id, 8000, null, "desconto combinado");

        var conta = await _clientes.ContaAsync(maria.Id);
        Assert.Equal(10000, conta.SaldoDevedorCentavos);                   // 80 + 50 − 30
        var t1 = conta.Titulos.Single(t => t.VendaId == v1.Id);
        Assert.Equal(8000, t1.TotalCentavos);
        Assert.Equal(3000, t1.PagoCentavos);                               // o pagamento continua abatendo o mais antigo
        Assert.Equal(10000, t1.ValorOriginalCentavos);
        Assert.Equal(-2000, t1.AjusteCentavos);
        Assert.True(t1.Ajustado);

        await using var db = _banco.CriarContexto();
        Assert.Equal(10000, db.Vendas.Single(v => v.Id == v1.Id).TotalCentavos); // a venda é imutável
    }

    [Fact]
    public async Task Editar_vencimento_muda_quando_o_titulo_vence()
    {
        var (maria, v1, _) = await CenarioDividaAsync();
        var original = (await _clientes.ContaAsync(maria.Id)).Titulos.First(t => t.VendaId == v1.Id).Vencimento;
        Assert.Equal(Agora.AddDays(30).Date, original.Date);

        await _titulos.EditarAsync(v1.Id, null, Dia(25), "cliente pediu mais prazo");

        var t = (await _clientes.ContaAsync(maria.Id)).Titulos.First(x => x.VendaId == v1.Id);
        Assert.Equal(Dia(25), t.Vencimento);
        Assert.Equal(10000, t.TotalCentavos);
        Assert.False(t.VencidoEm(Dia(25)));
        Assert.True(t.VencidoEm(Dia(26)));
    }

    [Fact]
    public async Task Ajuste_grava_a_trilha_e_o_historico_mostra_de_para_motivo_quem_e_quando()
    {
        var (maria, v1, _) = await CenarioDividaAsync();

        await _titulos.EditarAsync(v1.Id, 9000, Dia(28), "negociação");
        await _titulos.EditarAsync(v1.Id, 8500, null, "mais um abatimento");

        var historico = await _titulos.HistoricoDoClienteAsync(maria.Id);
        Assert.Equal(2, historico.Count);
        var maisNovo = historico[0];                    // do mais recente para o mais antigo
        Assert.Equal(9000, maisNovo.ValorAnteriorCentavos);   // o segundo ajuste parte do vigente, não do original
        Assert.Equal(8500, maisNovo.ValorNovoCentavos);
        Assert.False(maisNovo.MudouVencimento);
        Assert.Equal("mais um abatimento", maisNovo.Motivo);
        Assert.Equal("Ana", maisNovo.Operador);
        Assert.Equal(Agora, maisNovo.Quando);
        Assert.Equal(10000, historico[1].ValorAnteriorCentavos);
        Assert.True(historico[1].MudouVencimento);

        var trilha = await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.TituloAPrazoEditado));
        Assert.Equal(2, trilha.Count);
        Assert.All(trilha, a => Assert.Equal("Ana", a.Operador));
        Assert.Contains(trilha, a => a.Descricao.Contains("mais um abatimento"));
        Assert.Equal(8500, (await _clientes.ContaAsync(maria.Id)).Titulos.First(t => t.VendaId == v1.Id).TotalCentavos);
    }

    [Fact]
    public async Task Valor_abaixo_do_que_o_FIFO_ja_abateu_naquele_titulo_e_recusado()
    {
        var (maria, v1, _) = await CenarioDividaAsync();
        await _clientes.RegistrarPagamentoAsync(maria.Id, 6000, FormaPagamento.Pix); // abate 60,00 do título 1

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(v1.Id, 5000, null, "tentativa"));

        Assert.Contains("já recebeu", erro.Message);
        await _titulos.EditarAsync(v1.Id, 6000, null, "exatamente o já recebido"); // igual ao abatido: pode (título quitado)
        Assert.True((await _clientes.ContaAsync(maria.Id)).Titulos.First(t => t.VendaId == v1.Id).Quitado);
        Assert.Empty(await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.TituloAPrazoEditado, Texto: "tentativa")));
    }

    [Fact]
    public async Task Pagamento_depois_do_ajuste_continua_abatendo_por_FIFO_com_o_valor_ajustado()
    {
        var (maria, v1, v2) = await CenarioDividaAsync();
        await _titulos.EditarAsync(v1.Id, 4000, null, "abatimento");

        await _clientes.RegistrarPagamentoAsync(maria.Id, 7000, FormaPagamento.Dinheiro);

        var conta = await _clientes.ContaAsync(maria.Id);
        Assert.True(conta.Titulos.Single(t => t.VendaId == v1.Id).Quitado);        // 40 abatido por inteiro
        Assert.Equal(3000, conta.Titulos.Single(t => t.VendaId == v2.Id).PagoCentavos); // os 30 que sobraram vão para o seguinte
        Assert.Equal(2000, conta.SaldoDevedorCentavos);
        // e o pagamento não pode passar da dívida ajustada
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _clientes.RegistrarPagamentoAsync(maria.Id, 2100, FormaPagamento.Dinheiro));
    }

    [Theory]
    [InlineData(0L, "maior que zero")]
    [InlineData(-5L, "maior que zero")]
    public async Task Valor_zero_ou_negativo_e_recusado(long valor, string trecho)
    {
        var (_, v1, _) = await CenarioDividaAsync();

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(v1.Id, valor, null, "teste"));
        Assert.Contains(trecho, erro.Message);
    }

    [Fact]
    public async Task Vencimento_antes_da_venda_motivo_curto_e_nada_mudou_sao_recusados()
    {
        var (_, v1, _) = await CenarioDividaAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(v1.Id, null, Dia(1), "antes da venda"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(v1.Id, 9000, null, "ab"));
        var semMudanca = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(v1.Id, 10000, null, "igual"));
        Assert.Contains("Nada mudou", semMudanca.Message);
    }

    [Fact]
    public async Task Venda_que_nao_e_a_prazo_ou_esta_cancelada_nao_tem_titulo_para_editar()
    {
        var (maria, v1, _) = await CenarioDividaAsync();
        var avista = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Pix));
        await _vendas.CancelarAsync(v1.Id, "cliente desistiu");

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(avista.Id, 100, null, "teste"));
        var cancelada = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(v1.Id, 100, null, "teste"));
        Assert.Contains("cancelada", cancelada.Message);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _titulos.EditarAsync(9999, 100, null, "teste"));
        Assert.Equal(5000, (await _clientes.ContaAsync(maria.Id)).SaldoDevedorCentavos);
    }

    [Fact]
    public async Task Venda_com_ajuste_mas_sem_pagamento_cancela_e_o_ajuste_deixa_de_valer_junto()
    {
        var (maria, v1, _) = await CenarioDividaAsync();
        await _titulos.EditarAsync(v1.Id, 7000, null, "abatimento");

        await _vendas.CancelarAsync(v1.Id, "cliente desistiu");

        var conta = await _clientes.ContaAsync(maria.Id);
        Assert.Equal(5000, conta.SaldoDevedorCentavos);              // só a outra venda
        Assert.DoesNotContain(conta.Titulos, t => t.VendaId == v1.Id);
        Assert.Single(await _titulos.HistoricoDoClienteAsync(maria.Id)); // o histórico do ajuste permanece
        Assert.Equal(0, (await _dre.MontarAsync(Inicio, Fim)).AjustesTitulosCentavos - 0); // ajuste de venda cancelada não entra na receita
    }

    [Fact]
    public async Task A_trava_de_cancelar_venda_a_prazo_com_pagamento_continua_igual()
    {
        var (maria, v1, _) = await CenarioDividaAsync();
        await _titulos.EditarAsync(v1.Id, 7000, null, "abatimento");
        await _clientes.RegistrarPagamentoAsync(maria.Id, 1000, FormaPagamento.Dinheiro);

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.CancelarAsync(v1.Id, "teste"));

        Assert.Contains("já recebeu", erro.Message);
    }

    // =================== fonte única da dívida ===================

    [Fact]
    public async Task A_receber_do_motor_e_o_indicador_do_Inicio_usam_o_valor_devido_ajustado()
    {
        var (_, v1, _) = await CenarioDividaAsync();
        await _titulos.EditarAsync(v1.Id, 6000, Dia(22), "abatimento e novo prazo");

        // dez dias depois: o título 1 (vence em 22/10) já está vencido; o 2 (vence em 19/11) ainda não
        var depois = Dia(30);
        var p = await new PosicaoFinanceiraService(_banco.Fabrica, () => depois).CalcularAsync(Inicio, Fim);
        Assert.Equal(5000, p.AReceber.EmDiaCentavos);   // título 2 (50,00) ainda em dia
        Assert.Equal(6000, p.AReceber.VencidoCentavos); // título 1 ajustado (60,00) e vencido

        var inicio = await new InicioService(_banco.Fabrica, () => depois).ObterAsync();
        Assert.Equal(11000, inicio.DividaTotalCentavos);
        Assert.Equal(6000, inicio.DividaVencidaCentavos);
        Assert.Equal(1, inicio.ClientesComDividaVencida);
    }

    [Fact]
    public async Task O_limite_de_credito_do_PDV_considera_a_divida_ajustada()
    {
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 100)).Id;
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria", LimiteCreditoCentavos: 12000));
        var v = await Prazo(2, maria.Id); // 100,00 de 120,00

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Prazo(1, maria.Id)); // 100 + 50 > 120

        await _titulos.EditarAsync(v.Id, 6000, null, "abatimento");
        await Prazo(1, maria.Id); // 60 + 50 = 110 ≤ 120: agora passa
        Assert.Equal(11000, (await _clientes.ContaAsync(maria.Id)).SaldoDevedorCentavos);
    }

    [Fact]
    public async Task O_ajuste_aparece_no_DRE_dentro_da_receita_liquida_e_o_Resumo_do_Financeiro_continua_igual_ao_DRE()
    {
        var (_, v1, _) = await CenarioDividaAsync();
        await _titulos.EditarAsync(v1.Id, 8000, null, "abatimento de 20,00");   // receita −20,00
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Energia", 3000, Dia(10), "Energia", JaPago: true, Forma: FormaPagamento.Pix));

        var d = await _dre.MontarAsync(Inicio, Fim);
        var resumo = await _fin.ResumoAsync(Inicio, Fim);

        Assert.Equal(15000, d.ReceitaBrutaCentavos);                // o total das vendas não muda
        Assert.Equal(-2000, d.AjustesTitulosCentavos);
        Assert.Equal(13000, d.ReceitaLiquidaCentavos);              // 150 − 20
        Assert.Equal(6000, d.CustoMercadoriasCentavos);
        Assert.Equal(7000, d.LucroBrutoCentavos);
        Assert.Equal(4000, d.ResultadoCentavos);                    // 70 − 30 de energia
        Assert.Equal(d.ResultadoCentavos, resumo.ResultadoCentavos);
        Assert.Equal(d.LucroBrutoCentavos, resumo.LucroBrutoCentavos);
        Assert.Equal(-2000, resumo.AjustesTitulosCentavos);

        await _titulos.EditarAsync(v1.Id, 12000, null, "acréscimo de juros");  // agora +20,00 sobre a venda
        var depois = await _dre.MontarAsync(Inicio, Fim);
        Assert.Equal(2000, depois.AjustesTitulosCentavos);
        Assert.Equal((await _fin.ResumoAsync(Inicio, Fim)).ResultadoCentavos, depois.ResultadoCentavos);
    }

    [Fact]
    public async Task Ajuste_vale_no_periodo_da_venda_e_nao_no_do_ajuste()
    {
        var (_, v1, _) = await CenarioDividaAsync();
        await _titulos.EditarAsync(v1.Id, 8000, null, "abatimento");

        var antes = await _dre.MontarAsync(Dia(1), Dia(10));    // antes da venda
        var dia = await _dre.MontarAsync(Agora.Date, Agora.Date);

        Assert.Equal(0, antes.AjustesTitulosCentavos);
        Assert.Equal(-2000, dia.AjustesTitulosCentavos);
    }
}
