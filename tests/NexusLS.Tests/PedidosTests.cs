using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class PedidosTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private DateTime _agora = new(2026, 10, 20, 10, 0, 0);
    private readonly PedidoService _pedidos;
    private readonly ProdutoService _produtos;

    public PedidosTests()
    {
        _pedidos = new PedidoService(_banco.Fabrica, () => _agora);
        _produtos = new ProdutoService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private async Task<int> ProdutoAsync(string nome = "Boné", long preco = 2000, int estoque = 5) =>
        (await _produtos.CadastrarAsync(new DadosProduto(nome, null, preco, 800, 1), estoque)).Id;

    private Task<Pedido> CriarAsync(params ItemPedidoDados[] itens) =>
        _pedidos.CriarAsync(new DadosPedido(OrigemPedido.WhatsApp, itens, ClienteNome: "Maria", ClienteTelefone: "8199999-0000"));

    // ---------- criar ----------

    [Fact]
    public async Task Cria_pedido_com_numero_total_itens_do_produto_e_historico()
    {
        var bone = await ProdutoAsync();

        var p = await CriarAsync(new ItemPedidoDados(bone, 3), new ItemPedidoDados(null, 1, "Conserto de bainha", 1500));

        Assert.Equal("PED-2610-0001", p.Numero);
        Assert.Equal(3 * 2000 + 1500, p.TotalCentavos);
        Assert.Equal(StatusPedido.Novo, p.Status);
        Assert.Equal(("Boné", 2000L), (p.Itens[0].Nome, p.Itens[0].PrecoUnitarioCentavos));
        Assert.Null(p.Itens[1].ProdutoId);
        Assert.Single(p.Historico);
        // pedido não mexe no estoque
        await using var db = _banco.CriarContexto();
        Assert.Equal(5, (await db.Produtos.SingleAsync(x => x.Id == bone)).EstoqueAtual);
    }

    [Fact]
    public async Task Preco_e_nome_informados_valem_mais_que_os_do_produto()
    {
        var bone = await ProdutoAsync();

        var p = await CriarAsync(new ItemPedidoDados(bone, 2, "Boné personalizado", 1800, "m"));

        Assert.Equal(("Boné personalizado", 1800L, "M"), (p.Itens[0].Nome, p.Itens[0].PrecoUnitarioCentavos, p.Itens[0].Tamanho));
    }

    [Fact]
    public async Task Cliente_cadastrado_traz_nome_e_telefone()
    {
        var c = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Maria Souza", Telefone: "8188888-0000"));
        var bone = await ProdutoAsync();

        var p = await _pedidos.CriarAsync(new DadosPedido(OrigemPedido.Encomenda, new[] { new ItemPedidoDados(bone, 1) }, ClienteId: c.Id));

        Assert.Equal(("Maria Souza", "8188888-0000"), (p.ClienteNome, p.ClienteTelefone));
    }

    [Fact]
    public async Task Dados_invalidos_sao_recusados()
    {
        var bone = await ProdutoAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => CriarAsync());
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => CriarAsync(new ItemPedidoDados(bone, 0)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => CriarAsync(new ItemPedidoDados(bone, 1, PrecoUnitarioCentavos: -1)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => CriarAsync(new ItemPedidoDados(null, 1, " ", 100)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => CriarAsync(new ItemPedidoDados(999, 1)));
        Assert.Empty(await _pedidos.ListarAsync());
    }

    // ---------- etapas ----------

    [Fact]
    public async Task Anda_por_todas_as_etapas_e_registra_cada_uma_no_historico()
    {
        var p = await CriarAsync(new ItemPedidoDados(null, 1, "Camiseta", 5000));

        foreach (var esperada in new[] { StatusPedido.EmSeparacao, StatusPedido.Separado, StatusPedido.ProntoRetirada, StatusPedido.Entregue })
        {
            _agora = _agora.AddMinutes(10);
            Assert.Equal(esperada, (await _pedidos.AvancarAsync(p.Id, "Ana")).Status);
        }

        var final = await _pedidos.ObterAsync(p.Id);
        Assert.Equal(5, final.Historico.Count);
        Assert.Equal("Ana", final.Historico.Last().Operador);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _pedidos.AvancarAsync(p.Id));
    }

    [Fact]
    public async Task Volta_uma_etapa_mas_nao_antes_de_novo_e_o_entregue_pode_ser_reaberto()
    {
        var p = await CriarAsync(new ItemPedidoDados(null, 1, "X", 100));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _pedidos.VoltarAsync(p.Id));

        await _pedidos.AvancarAsync(p.Id);
        Assert.Equal(StatusPedido.Novo, (await _pedidos.VoltarAsync(p.Id)).Status);

        for (var i = 0; i < 4; i++) await _pedidos.AvancarAsync(p.Id); // até Entregue
        Assert.Equal(StatusPedido.ProntoRetirada, (await _pedidos.VoltarAsync(p.Id)).Status);
    }

    [Fact]
    public async Task Cancelar_exige_motivo_vale_antes_de_entregar_e_cancelado_nao_anda()
    {
        var a = await CriarAsync(new ItemPedidoDados(null, 1, "X", 100));
        var b = await CriarAsync(new ItemPedidoDados(null, 1, "Y", 100));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _pedidos.CancelarAsync(a.Id, "x"));
        var cancelado = await _pedidos.CancelarAsync(a.Id, "cliente desistiu");
        Assert.Equal(("cliente desistiu", StatusPedido.Cancelado), (cancelado.MotivoCancelamento, cancelado.Status));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _pedidos.AvancarAsync(a.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _pedidos.VoltarAsync(a.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _pedidos.CancelarAsync(a.Id, "de novo"));

        for (var i = 0; i < 4; i++) await _pedidos.AvancarAsync(b.Id);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _pedidos.CancelarAsync(b.Id, "tarde demais"));
    }

    // ---------- listar ----------

    [Fact]
    public async Task Lista_filtra_por_etapa_texto_e_esconde_finalizados_se_pedido()
    {
        var a = await CriarAsync(new ItemPedidoDados(null, 1, "Camiseta polo", 100));
        _agora = _agora.AddMinutes(1);
        var b = await _pedidos.CriarAsync(new DadosPedido(OrigemPedido.Catalogo, new[] { new ItemPedidoDados(null, 1, "Boné", 100) }, ClienteNome: "João"));
        await _pedidos.AvancarAsync(b.Id);
        await _pedidos.CancelarAsync(a.Id, "teste");

        Assert.Equal(new[] { b.Id, a.Id }, (await _pedidos.ListarAsync()).Select(p => p.Id));
        Assert.Equal(b.Id, (await _pedidos.ListarAsync(new FiltroPedidos(Status: StatusPedido.EmSeparacao))).Single().Id);
        Assert.Equal(a.Id, (await _pedidos.ListarAsync(new FiltroPedidos(Texto: "polo"))).Single().Id);
        Assert.Equal(b.Id, (await _pedidos.ListarAsync(new FiltroPedidos(Texto: "joão"))).Single().Id);
        Assert.Equal(b.Id, (await _pedidos.ListarAsync(new FiltroPedidos(IncluirFinalizados: false))).Single().Id);
    }

    // ---------- venda a prazo vira pedido ----------

    [Fact]
    public async Task Venda_a_prazo_cria_o_pedido_ligado_a_venda_e_cancelar_a_venda_cancela_o_pedido()
    {
        var bone = await ProdutoAsync(estoque: 10);
        var maria = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Maria Souza", Telefone: "8188888-0000"));
        var vendas = new VendaService(_banco.Fabrica);

        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 2) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));

        var pedido = (await _pedidos.ListarAsync()).Single();
        Assert.Equal((venda.Id, "Maria Souza", 4000L, OrigemPedido.Balcao, StatusPedido.Novo), (pedido.VendaId, pedido.ClienteNome, pedido.TotalCentavos, pedido.Origem, pedido.Status));
        Assert.Matches(@"^PED-\d{4}-\d{4}$", pedido.Numero);
        Assert.Equal("Boné", Assert.Single(pedido.Itens).Nome);

        await vendas.CancelarAsync(venda.Id, "cliente desistiu");

        var depois = await _pedidos.ObterAsync(pedido.Id);
        Assert.Equal(StatusPedido.Cancelado, depois.Status);
        Assert.Contains("cancelada", depois.MotivoCancelamento);
        Assert.Equal(2, depois.Historico.Count);
    }

    [Fact]
    public async Task Venda_a_vista_nao_cria_pedido()
    {
        var bone = await ProdutoAsync(estoque: 10);

        await new VendaService(_banco.Fabrica).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1) }, 0, FormaPagamento.Pix));

        Assert.Empty(await _pedidos.ListarAsync());
    }
}
