using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Lista de vendas da tela Trocas (filtros e páginas), detalhe da venda e o diálogo troca ↔ caixa ↔ estoque.
/// Camisa: R$ 50,00, tamanhos P e M com 10 cada; Boné: R$ 30,00, 10 sem tamanho.
/// </summary>
[Collection("OperadorAtual")]
public class TrocaListagemTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 15, 15, 0, 0);

    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly TrocaService _trocas;
    private readonly ClienteService _clientes;
    private DateTime _relogio = Agora;
    private Produto _camisa = null!;
    private Produto _bone = null!;

    public TrocaListagemTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica, () => _relogio);
        _trocas = new TrocaService(_banco.Fabrica, () => _relogio);
        _clientes = new ClienteService(_banco.Fabrica, () => _relogio);
        OperadorAtual.Nome = null;
    }

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync()
    {
        _camisa = await _produtos.CadastrarAsync(new DadosProduto("Camisa", "CAM", 5000, 2000, 0, Referencia: "REF-77", Categoria: "Camisetas"));
        await _produtos.AdicionarGradeAsync(_camisa.Id, new[] { "P", "M" });
        var estoque = new EstoqueService(_banco.Fabrica);
        await estoque.AjustarAsync(_camisa.Id, TipoMovimentacao.Entrada, 10, "Inicial", "P");
        await estoque.AjustarAsync(_camisa.Id, TipoMovimentacao.Entrada, 10, "Inicial", "M");
        _bone = await _produtos.CadastrarAsync(new DadosProduto("Boné", "BON", 3000, 1000, 0, Categoria: "Acessórios"), 10);
    }

    private Task<Venda> VenderAsync(FormaPagamento forma, params ItemCarrinho[] itens) =>
        _vendas.FinalizarAsync(new PedidoVenda(itens, 0, forma, ValorRecebidoCentavos: forma == FormaPagamento.Dinheiro ? 1_000_000 : null));

    private async Task<int> EstoqueAsync(int produtoId, string tamanho)
    {
        await using var db = _banco.CriarContexto();
        return db.TamanhosProduto.Single(t => t.ProdutoId == produtoId && t.Tamanho == tamanho).Estoque;
    }

    private static int ItemDe(Venda v, int produtoId, string tamanho) => v.Itens.Single(i => i.ProdutoId == produtoId && i.Tamanho == tamanho).Id;

    // ================= lista =================

    [Fact]
    public async Task Lista_traz_as_mais_novas_primeiro_em_paginas_sem_repetir()
    {
        await PrepararAsync();
        var ids = new List<int>();
        for (var i = 0; i < 7; i++) ids.Add((await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_bone.Id, 1))).Id);

        var p1 = await _trocas.ListarVendasAsync(null, null, 3);
        Assert.Equal(ids.AsEnumerable().Reverse().Take(3), p1.Vendas.Select(v => v.VendaId));
        Assert.True(p1.TemMais);

        var p2 = await _trocas.ListarVendasAsync(null, p1.Vendas[^1].VendaId, 3);
        Assert.Equal(ids.AsEnumerable().Reverse().Skip(3).Take(3), p2.Vendas.Select(v => v.VendaId));
        Assert.True(p2.TemMais);

        var p3 = await _trocas.ListarVendasAsync(null, p2.Vendas[^1].VendaId, 3);
        Assert.Equal(new[] { ids[0] }, p3.Vendas.Select(v => v.VendaId));
        Assert.False(p3.TemMais);
    }

    [Fact]
    public async Task Filtros_de_cupom_forma_cliente_periodo_e_produto()
    {
        await PrepararAsync();
        var ana = await _clientes.CadastrarAsync(new DadosCliente("Ana Souza"));
        _relogio = new DateTime(2026, 11, 10, 10, 0, 0);
        var antiga = await VenderAsync(FormaPagamento.Dinheiro, new ItemCarrinho(_bone.Id, 1));
        _relogio = new DateTime(2026, 11, 15, 10, 0, 0);
        var pix = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_camisa.Id, 1, "P") }, 0, FormaPagamento.Pix, ClienteId: ana.Id));
        var debito = await VenderAsync(FormaPagamento.CartaoDebito, new ItemCarrinho(_bone.Id, 2));

        Assert.Equal(3, (await _trocas.ListarVendasAsync()).Vendas.Count);
        Assert.Equal(pix.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(NumeroCupom: pix.Id))).Vendas.Single().VendaId);
        Assert.Equal(antiga.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Forma: FormaPagamento.Dinheiro))).Vendas.Single().VendaId);
        Assert.Equal(pix.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Cliente: "souza"))).Vendas.Single().VendaId);
        Assert.Equal(2, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(De: new DateTime(2026, 11, 15)))).Vendas.Count);
        Assert.Equal(antiga.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Ate: new DateTime(2026, 11, 12)))).Vendas.Single().VendaId);

        // produto: por nome, categoria, referência e código de barras
        Assert.Equal(pix.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Produto: "camis"))).Vendas.Single().VendaId);
        Assert.Equal(pix.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Produto: "Camiset"))).Vendas.Single().VendaId);
        Assert.Equal(pix.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Produto: "REF-77"))).Vendas.Single().VendaId);
        Assert.Equal(pix.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Produto: "CAM"))).Vendas.Single().VendaId);
        Assert.Equal(2, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Produto: "BON"))).Vendas.Count);
        Assert.Empty((await _trocas.ListarVendasAsync(new FiltroVendasTroca(Produto: "inexistente"))).Vendas);
        Assert.Equal(debito.Id, (await _trocas.ListarVendasAsync(new FiltroVendasTroca(Produto: "Bon", Forma: FormaPagamento.CartaoDebito))).Vendas.Single().VendaId);
    }

    [Fact]
    public async Task Lista_marca_cancelada_e_com_troca_e_conta_as_pecas()
    {
        await PrepararAsync();
        var cancelada = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_bone.Id, 1));
        await _vendas.CancelarAsync(cancelada.Id, "Desistiu");
        var trocada = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_camisa.Id, 2, "P"));
        await _trocas.DevolverAsync(trocada.Id, new[] { new ItemADevolver(ItemDe(trocada, _camisa.Id, "P"), 1) }, "Tamanho errado");

        var lista = (await _trocas.ListarVendasAsync()).Vendas;
        var t = lista.Single(v => v.VendaId == trocada.Id);
        Assert.True(t.TemTroca);
        Assert.Equal(2, t.Pecas);
        Assert.True(lista.Single(v => v.VendaId == cancelada.Id).Cancelada);
    }

    // ================= detalhe =================

    [Fact]
    public async Task Detalhe_mostra_itens_pagamento_operador_e_trocas_ja_feitas()
    {
        await PrepararAsync();
        OperadorAtual.Nome = "Ana";
        try
        {
            var venda = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_camisa.Id, 2, "M"), new ItemCarrinho(_bone.Id, 1));
            await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "M"), 1) }, "Cor errada");

            var d = await _trocas.DetalharVendaAsync(venda.Id);
            Assert.Equal(13000, d.TotalCentavos);
            Assert.Equal(FormaPagamento.Pix, d.Forma);
            Assert.Equal("Ana", d.Operador);
            Assert.True(d.PodeTrocar);
            var camisa = d.Itens.Single(i => i.NomeProduto == "Camisa");
            Assert.Equal("M", camisa.Tamanho);
            Assert.Equal("REF-77", camisa.Referencia);
            Assert.Equal(2, camisa.Quantidade);
            Assert.Equal(1, camisa.Devolvidos);
            Assert.Equal(5000, camisa.PrecoUnitarioCentavos);
            var troca = Assert.Single(d.Trocas);
            Assert.Equal(5000, troca.ValorCentavos);
            Assert.NotNull(troca.CodigoVale);
        }
        finally { OperadorAtual.Nome = null; }
    }

    [Fact]
    public async Task Detalhe_de_venda_cancelada_abre_mas_nao_deixa_trocar()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_bone.Id, 1));
        await _vendas.CancelarAsync(venda.Id, "Desistiu");

        var d = await _trocas.DetalharVendaAsync(venda.Id);
        Assert.True(d.Cancelada);
        Assert.False(d.PodeTrocar);
        Assert.Contains("cancelada", d.MotivoNaoPodeTrocar);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(venda.Itens.Single().Id, 1) }, "Troca"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _trocas.DetalharVendaAsync(99999));
    }

    [Fact]
    public async Task Detalhe_com_tudo_devolvido_nao_deixa_trocar_de_novo()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_bone.Id, 1));
        await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(venda.Itens.Single().Id, 1) }, "Defeito");
        var d = await _trocas.DetalharVendaAsync(venda.Id);
        Assert.False(d.PodeTrocar);
        Assert.Contains("já voltaram", d.MotivoNaoPodeTrocar);
    }

    // ================= troca ↔ caixa ↔ estoque =================

    [Fact]
    public async Task Peca_volta_ao_tamanho_certo_e_o_total_do_produto_acompanha()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_camisa.Id, 3, "P"), new ItemCarrinho(_camisa.Id, 2, "M"));
        Assert.Equal(7, await EstoqueAsync(_camisa.Id, "P"));
        Assert.Equal(8, await EstoqueAsync(_camisa.Id, "M"));

        var r = await _trocas.DevolverAsync(venda.Id, new[]
        {
            new ItemADevolver(ItemDe(venda, _camisa.Id, "M"), 2),
            new ItemADevolver(ItemDe(venda, _camisa.Id, "P"), 1)
        }, "Tamanho errado");

        Assert.Equal(8, await EstoqueAsync(_camisa.Id, "P"));
        Assert.Equal(10, await EstoqueAsync(_camisa.Id, "M"));
        Assert.Equal(15000, r.Vale!.ValorCentavos); // 3 peças de R$ 50,00
        await using var db = _banco.CriarContexto();
        Assert.Equal(18, db.Produtos.Single(p => p.Id == _camisa.Id).EstoqueAtual);
        Assert.Equal(db.TamanhosProduto.Where(t => t.ProdutoId == _camisa.Id).Sum(t => t.Estoque), db.Produtos.Single(p => p.Id == _camisa.Id).EstoqueAtual);
    }

    [Fact]
    public async Task Devolucao_maior_que_o_vendido_e_recusada_e_nada_muda()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_camisa.Id, 2, "P"));
        var item = ItemDe(venda, _camisa.Id, "P");
        await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 1) }, "Troca");

        // sobra 1; pedir 2 (total 3 > 2 vendidas) é recusado, inclusive somando a troca anterior
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(item, 2) }, "Troca"));
        Assert.Equal(9, await EstoqueAsync(_camisa.Id, "P"));
        await using var db = _banco.CriarContexto();
        Assert.Single(db.Devolucoes);
        Assert.Single(db.ValesTroca);
    }

    [Fact]
    public async Task Troca_nao_mexe_na_gaveta_e_o_vale_usado_entra_como_vale_no_caixa()
    {
        await PrepararAsync();
        var caixa = new CaixaService(_banco.Fabrica, () => _relogio);
        var turno = await caixa.AbrirAsync(0);
        var venda = await VenderAsync(FormaPagamento.Dinheiro, new ItemCarrinho(_camisa.Id, 1, "P"));
        var antes = await caixa.ResumoAsync(turno.Id);
        Assert.Equal(5000, antes.VendasDinheiroCentavos);

        var vale = (await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "P"), 1) }, "Tamanho errado")).Vale!;
        var depois = await caixa.ResumoAsync(turno.Id);
        Assert.Equal(antes.VendasDinheiroCentavos, depois.VendasDinheiroCentavos); // o dinheiro não sai: o cliente leva um vale

        // o cliente troca o vale por outra camisa: nenhum dinheiro novo entra
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_camisa.Id, 1, "M") }, 0, FormaPagamento.Dinheiro,
            ValorRecebidoCentavos: 5000, ValeTrocaCodigo: vale.Codigo));
        var fim = await caixa.ResumoAsync(turno.Id);
        Assert.Equal(5000, fim.VendasDinheiroCentavos);
        Assert.Equal(0, (await _trocas.ConsultarValeAsync(vale.Codigo))!.SaldoCentavos);
        Assert.Equal(10, await EstoqueAsync(_camisa.Id, "P"));
        Assert.Equal(9, await EstoqueAsync(_camisa.Id, "M"));
    }

    [Fact]
    public async Task Cancelar_a_troca_devolve_a_peca_ao_mesmo_tamanho_e_some_do_financeiro_do_periodo()
    {
        await PrepararAsync();
        var venda = await VenderAsync(FormaPagamento.Pix, new ItemCarrinho(_camisa.Id, 1, "M"));
        var r = await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(ItemDe(venda, _camisa.Id, "M"), 1) }, "Tamanho errado");
        Assert.Equal(10, await EstoqueAsync(_camisa.Id, "M"));

        await _trocas.CancelarDevolucaoAsync(r.Devolucao.Id, "Cliente desistiu");
        Assert.Equal(9, await EstoqueAsync(_camisa.Id, "M"));
        Assert.Equal(10, await EstoqueAsync(_camisa.Id, "P"));
        Assert.Equal(0, (await _trocas.ConsultarValeAsync(r.Vale!.Codigo))!.SaldoCentavos);

        // depois de cancelada a troca, a venda volta a poder trocar tudo
        var d = await _trocas.DetalharVendaAsync(venda.Id);
        Assert.True(d.PodeTrocar);
        Assert.Equal(0, d.Itens.Single().Devolvidos);
        Assert.True(Assert.Single(d.Trocas).Cancelada);
    }
}
