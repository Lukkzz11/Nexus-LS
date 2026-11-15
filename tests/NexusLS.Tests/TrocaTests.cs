using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Troca de mercadoria e vale-troca: a peça volta ao estoque do tamanho, o valor pago vira vale, o vale paga venda
/// (inteira ou em parte) e os números do caixa e do financeiro contam só o dinheiro que entrou.
/// Camisa: preço R$ 50,00, custo R$ 20,00, tamanhos P (10) e M (10).
/// </summary>
[Collection("OperadorAtual")]
public class TrocaTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 15, 15, 0, 0);

    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly TrocaService _trocas;
    private Produto _camisa = null!;
    private Produto _bone = null!;

    public TrocaTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica, () => Agora);
        _trocas = new TrocaService(_banco.Fabrica, () => Agora);
        OperadorAtual.Nome = null;
    }

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync()
    {
        _camisa = await _produtos.CadastrarAsync(new DadosProduto("Camisa", "CAM", 5000, 2000, 0));
        await _produtos.AdicionarGradeAsync(_camisa.Id, new[] { "P", "M" });
        var estoque = new EstoqueService(_banco.Fabrica);
        await estoque.AjustarAsync(_camisa.Id, TipoMovimentacao.Entrada, 10, "Inicial", "P");
        await estoque.AjustarAsync(_camisa.Id, TipoMovimentacao.Entrada, 10, "Inicial", "M");
        _bone = await _produtos.CadastrarAsync(new DadosProduto("Boné", "BON", 3000, 1000, 0), 10);
    }

    private Task<Venda> VenderAsync(FormaPagamento forma, long desconto, params ItemCarrinho[] itens) =>
        _vendas.FinalizarAsync(new PedidoVenda(itens, desconto, forma,
            ValorRecebidoCentavos: forma == FormaPagamento.Dinheiro ? 1_000_000 : null));

    private async Task<int> EstoqueAsync(int produtoId, string tamanho)
    {
        await using var db = _banco.CriarContexto();
        return db.TamanhosProduto.Single(t => t.ProdutoId == produtoId && t.Tamanho == tamanho).Estoque;
    }

    private static int ItemDe(Venda v, int produtoId, string tamanho) =>
        v.Itens.Single(i => i.ProdutoId == produtoId && i.Tamanho == tamanho).Id;

    // ================= devolução =================

    [Fact]
    public async Task Devolucao_volta_a_peca_ao_tamanho_e_gera_vale_com_o_valor_pago()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_camisa.Id, 2, "P"));
        Assert.Equal(8, await EstoqueAsync(_camisa.Id, "P"));

        var r = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "P"), 1) }, "Tamanho errado");

        Assert.Equal(9, await EstoqueAsync(_camisa.Id, "P"));
        Assert.NotNull(r.Vale);
        Assert.Equal(5000, r.Vale!.ValorCentavos);
        Assert.StartsWith("VT", r.Vale.Codigo);
        Assert.Equal(10, r.Vale.Codigo.Length);
        var saldo = await _trocas.ConsultarValeAsync(r.Vale.Codigo.ToLowerInvariant().Insert(2, "-"));
        Assert.Equal(5000, saldo!.SaldoCentavos);
        Assert.Equal(venda.Id, saldo.VendaOrigemId);

        await using var db = _banco.CriarContexto();
        Assert.Contains(db.Movimentacoes, m => m.Tipo == TipoMovimentacao.Devolucao && m.Quantidade == 1 && m.Tamanho == "P");
        Assert.Contains(db.Atividades, a => a.Tipo == TipoAtividade.DevolucaoRegistrada);
    }

    [Fact]
    public async Task Desconto_da_venda_e_repartido_e_o_acrescimo_do_cartao_nao_volta()
    {
        await PrepararAsync();
        // 50 + 30 = 80, desconto 16 -> base 64: camisa vale 40, boné 24. Crédito em 2x tem acréscimo, que não volta.
        var venda = await _vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(_camisa.Id, 1, "M"), new ItemCarrinho(_bone.Id, 1) }, 1600, FormaPagamento.CartaoCredito, Parcelas: 2));
        Assert.True(venda.AcrescimoCentavos > 0);

        var r = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "M"), 1) }, "Não serviu");

        Assert.Equal(4000, r.Vale!.ValorCentavos);
    }

    [Fact]
    public async Task Devolver_aos_poucos_soma_exatamente_o_valor_da_linha()
    {
        await PrepararAsync();
        // 3 camisas = 150,00 com 10,00 de desconto: a linha vale 140,00, que não divide por 3 sem sobra.
        var venda = await VenderAsync(FormaPagamento.Dinheiro, 1000, new ItemCarrinho(_camisa.Id, 3, "P"));
        var item = ItemDe(venda, _camisa.Id, "P");

        var a = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 1) }, "Troca 1");
        var b = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 1) }, "Troca 2");
        var c = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 1) }, "Troca 3");

        Assert.Equal(14000, a.Vale!.ValorCentavos + b.Vale!.ValorCentavos + c.Vale!.ValorCentavos);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 1) }, "Troca 4"));
        Assert.Equal(0, (await _trocas.VendaParaTrocaAsync(venda.Id)).Itens.Single().Disponiveis);
    }

    [Fact]
    public async Task Nao_devolve_mais_do_que_foi_vendido_nem_sem_motivo()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_camisa.Id, 1, "P"));
        var item = ItemDe(venda, _camisa.Id, "P");

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 2) }, "Troca"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 1) }, "x"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _trocas.DevolverAsync(venda.Id, Array.Empty<ItemADevolver>(), "Troca"));
        Assert.Equal(9, await EstoqueAsync(_camisa.Id, "P")); // nada mudou
    }

    [Fact]
    public async Task Venda_cancelada_e_venda_a_prazo_em_aberto_nao_trocam()
    {
        await PrepararAsync();
        var cancelada = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_camisa.Id, 1, "P"));
        await _vendas.CancelarAsync(cancelada.Id, "Desistiu");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _trocas.VendaParaTrocaAsync(cancelada.Id));

        var clientes = new ClienteService(_banco.Fabrica, () => Agora);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        var prazo = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_camisa.Id, 1, "P") }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _trocas.DevolverAsync(prazo.Id, new[] { new ItemADevolver(ItemDe(prazo, _camisa.Id, "P"), 1) }, "Troca"));
        Assert.Contains("a prazo", erro.Message);

        await clientes.RegistrarPagamentoAsync(maria.Id, 5000, FormaPagamento.Dinheiro); // quitou: agora troca
        var r = await _trocas.DevolverAsync(prazo.Id, new[] { new ItemADevolver(ItemDe(prazo, _camisa.Id, "P"), 1) }, "Troca");
        Assert.Equal(5000, r.Vale!.ValorCentavos);
    }

    [Fact]
    public async Task Venda_com_troca_nao_cancela()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_camisa.Id, 2, "P"));
        await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "P"), 1) }, "Troca");

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.CancelarAsync(venda.Id, "Errei"));
        Assert.Contains("troca", erro.Message);
    }

    // ================= vale-troca no pagamento =================

    [Fact]
    public async Task Vale_que_cobre_tudo_vira_a_forma_e_o_saldo_que_sobra_continua_no_vale()
    {
        await PrepararAsync();
        var original = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_camisa.Id, 1, "P"));
        var vale = (await _trocas.DevolverAsync(original.Id, new[] { new ItemADevolver(ItemDe(original, _camisa.Id, "P"), 1) }, "Troca")).Vale!;

        // leva um boné (30,00) pagando com o vale de 50,00: sobra 20,00
        var nova = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_bone.Id, 1) }, 0, FormaPagamento.ValeTroca,
            ValeTrocaCodigo: vale.Codigo));

        Assert.Equal(FormaPagamento.ValeTroca, nova.FormaPagamento);
        Assert.Equal(3000, nova.TotalCentavos);
        Assert.Equal(3000, nova.ValeTrocaCentavos);
        Assert.Equal(0, nova.ValorPagoCentavos);
        Assert.Equal(2000, (await _trocas.ConsultarValeAsync(vale.Codigo))!.SaldoCentavos);

        // cancelar a venda devolve o saldo ao vale
        await _vendas.CancelarAsync(nova.Id, "Desistiu");
        Assert.Equal(5000, (await _trocas.ConsultarValeAsync(vale.Codigo))!.SaldoCentavos);
    }

    [Fact]
    public async Task Vale_parcial_com_dinheiro_calcula_o_troco_sobre_o_resto()
    {
        await PrepararAsync();
        var original = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_bone.Id, 1));
        var vale = (await _trocas.DevolverAsync(original.Id, new[] { new ItemADevolver(ItemDe(original, _bone.Id, Tamanhos.SemTamanho), 1) }, "Troca")).Vale!;

        // camisa 50,00 - vale 30,00 = 20,00; cliente entrega 50,00 -> troco 30,00
        var nova = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_camisa.Id, 1, "M") }, 0, FormaPagamento.Dinheiro,
            ValorRecebidoCentavos: 5000, ValeTrocaCodigo: vale.Codigo));

        Assert.Equal(5000, nova.TotalCentavos);
        Assert.Equal(3000, nova.ValeTrocaCentavos);
        Assert.Equal(2000, nova.ValorPagoCentavos);
        Assert.Equal(3000, nova.TrocoCentavos);
        Assert.Equal(0, (await _trocas.ConsultarValeAsync(vale.Codigo))!.SaldoCentavos);

        // vale sem saldo não paga mais nada
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(_bone.Id, 1) }, 0, FormaPagamento.Pix, ValeTrocaCodigo: vale.Codigo)));
    }

    [Fact]
    public async Task Acrescimo_do_credito_e_sobre_o_que_falta_depois_do_vale()
    {
        await PrepararAsync();
        var original = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_bone.Id, 1));
        var vale = (await _trocas.DevolverAsync(original.Id, new[] { new ItemADevolver(ItemDe(original, _bone.Id, Tamanhos.SemTamanho), 1) }, "Troca")).Vale!;

        var nova = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_camisa.Id, 1, "M") }, 0, FormaPagamento.CartaoCredito,
            Parcelas: 2, ValeTrocaCodigo: vale.Codigo));

        Assert.Equal(TaxasCartao.Acrescimo(2000, 2), nova.AcrescimoCentavos);
        Assert.Equal(2000 + nova.AcrescimoCentavos, nova.ValorPagoCentavos);
    }

    [Fact]
    public async Task Vale_invalido_ou_resto_a_prazo_sao_recusados()
    {
        await PrepararAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(_bone.Id, 1) }, 0, FormaPagamento.Pix, ValeTrocaCodigo: "VTNAOEXISTE")));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(_bone.Id, 1) }, 0, FormaPagamento.ValeTroca)));

        var original = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_bone.Id, 1));
        var vale = (await _trocas.DevolverAsync(original.Id, new[] { new ItemADevolver(ItemDe(original, _bone.Id, Tamanhos.SemTamanho), 1) }, "Troca")).Vale!;
        var maria = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Maria"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(_camisa.Id, 1, "M") }, 0, FormaPagamento.APrazo, ClienteId: maria.Id, ValeTrocaCodigo: vale.Codigo)));
        // vale que não cobre e forma "vale-troca": falta escolher como pagar o resto
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(_camisa.Id, 1, "M") }, 0, FormaPagamento.ValeTroca, ValeTrocaCodigo: vale.Codigo)));
        Assert.Equal(3000, (await _trocas.ConsultarValeAsync(vale.Codigo))!.SaldoCentavos);
    }

    // ================= cancelar a troca =================

    [Fact]
    public async Task Cancelar_troca_tira_a_peca_do_estoque_e_cancela_o_vale()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_camisa.Id, 1, "P"));
        var r = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "P"), 1) }, "Troca");
        Assert.Equal(10, await EstoqueAsync(_camisa.Id, "P"));

        await _trocas.CancelarDevolucaoAsync(r.Devolucao.Id, "Lancei errado");

        Assert.Equal(9, await EstoqueAsync(_camisa.Id, "P"));
        var vale = await _trocas.ConsultarValeAsync(r.Vale!.Codigo);
        Assert.True(vale!.Cancelado);
        Assert.Equal(0, vale.SaldoCentavos);
        await _vendas.CancelarAsync(venda.Id, "Agora pode"); // sem troca valendo, a venda volta a poder ser cancelada
    }

    [Fact]
    public async Task Troca_com_vale_ja_usado_nao_cancela()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_camisa.Id, 1, "P"));
        var r = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "P"), 1) }, "Troca");
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_bone.Id, 1) }, 0, FormaPagamento.ValeTroca, ValeTrocaCodigo: r.Vale!.Codigo));

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _trocas.CancelarDevolucaoAsync(r.Devolucao.Id, "Lancei errado"));
        Assert.Contains("já pagou", erro.Message);
    }

    // ================= números: caixa, relatório e financeiro =================

    [Fact]
    public async Task Caixa_e_relatorio_contam_so_o_dinheiro_que_entrou()
    {
        await PrepararAsync();
        var caixa = new CaixaService(_banco.Fabrica, () => Agora);
        var turno = await caixa.AbrirAsync(0);
        var original = await VenderAsync(FormaPagamento.Pix, 0, new ItemCarrinho(_bone.Id, 1));       // Pix 30,00
        var vale = (await _trocas.DevolverAsync(original.Id, new[] { new ItemADevolver(ItemDe(original, _bone.Id, Tamanhos.SemTamanho), 1) }, "Troca")).Vale!;
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_camisa.Id, 1, "M") }, 0, FormaPagamento.Dinheiro,
            ValorRecebidoCentavos: 2000, ValeTrocaCodigo: vale.Codigo));                                   // 50,00 = vale 30,00 + dinheiro 20,00

        var resumo = await caixa.ResumoAsync(turno.Id);
        Assert.Equal(2000, resumo.VendasDinheiroCentavos);
        Assert.Equal(3000, resumo.VendasPixCentavos);

        await using var db = _banco.CriarContexto();
        var formas = RelatorioService.ResumoPorForma(db.Vendas.ToList());
        Assert.Equal(2000, formas.Single(f => f.Forma == FormaPagamento.Dinheiro).TotalCentavos);
        Assert.Equal(3000, formas.Single(f => f.Forma == FormaPagamento.ValeTroca).TotalCentavos);
        Assert.Equal(8000, formas.Sum(f => f.TotalCentavos)); // = soma dos totais das vendas
    }

    [Fact]
    public async Task Financeiro_troca_reduz_receita_e_custo_mas_nao_mexe_no_saldo()
    {
        await PrepararAsync();
        var contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        await contas.GarantirContaPadraoAsync();
        var posicao = new PosicaoFinanceiraService(_banco.Fabrica, () => Agora);
        var antes = await posicao.CalcularAsync(Agora.Date, Agora);

        var original = await VenderAsync(FormaPagamento.Dinheiro, 0, new ItemCarrinho(_camisa.Id, 1, "P")); // +50,00 na gaveta
        var vale = (await _trocas.DevolverAsync(original.Id, new[] { new ItemADevolver(ItemDe(original, _camisa.Id, "P"), 1) }, "Troca")).Vale!;
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_bone.Id, 2) }, 0, FormaPagamento.Dinheiro,
            ValorRecebidoCentavos: 1000, ValeTrocaCodigo: vale.Codigo));                                     // 60,00 = vale 50,00 + 10,00

        var p = await posicao.CalcularAsync(Agora.Date, Agora);

        Assert.Equal(6000, p.DinheiroFisicoCentavos - antes.DinheiroFisicoCentavos); // 50,00 + 10,00: só dinheiro de verdade
        Assert.Equal(11000, p.Periodo.ReceitaBrutaCentavos);                          // 50,00 + 60,00
        Assert.Equal(5000, p.Periodo.DevolucoesCentavos);                             // a camisa voltou
        Assert.Equal(6000, p.Periodo.ReceitaLiquidaCentavos);
        Assert.Equal(2000, p.Periodo.CustoMercadoriasCentavos);                       // 20 + 20 - 20 (custo da camisa voltou)
        Assert.Equal(4000, p.Periodo.LucroBrutoCentavos);
    }

    // ================= cálculo puro =================

    [Fact]
    public void Partes_das_linhas_somam_exatamente_a_base()
    {
        var venda = new Venda
        {
            SubtotalCentavos = 1000, DescontoCentavos = 1,
            Itens = { new ItemVenda { Id = 1, TotalCentavos = 333 }, new ItemVenda { Id = 2, TotalCentavos = 333 }, new ItemVenda { Id = 3, TotalCentavos = 334 } }
        };
        var partes = TrocaService.PartesDasLinhas(venda);
        Assert.Equal(999, partes.Values.Sum());
    }
}
