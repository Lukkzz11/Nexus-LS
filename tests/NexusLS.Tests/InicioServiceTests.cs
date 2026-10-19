using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class InicioServiceTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private DateTime _agora = new(2026, 10, 19, 14, 0, 0);
    private readonly VendaService _vendas;
    private readonly ProdutoService _produtos;
    private readonly ClienteService _clientes;
    private readonly CaixaService _caixa;
    private readonly InicioService _inicio;
    private int _produtoId;

    public InicioServiceTests()
    {
        _vendas = new VendaService(_banco.Fabrica, () => _agora);
        _produtos = new ProdutoService(_banco.Fabrica);
        _clientes = new ClienteService(_banco.Fabrica, () => _agora);
        _caixa = new CaixaService(_banco.Fabrica, () => _agora);
        _inicio = new InicioService(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private async Task<int> ProdutoAsync()
    {
        if (_produtoId == 0)
            _produtoId = (await _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 500)).Id; // R$ 20,00 cada
        return _produtoId;
    }

    private async Task<Venda> VenderAsync(int quantidade, FormaPagamento forma = FormaPagamento.Pix, int? clienteId = null) =>
        await _vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(await ProdutoAsync(), quantidade) }, 0, forma,
            forma == FormaPagamento.Dinheiro ? 1_000_000 : null, ClienteId: clienteId));

    [Fact]
    public async Task Banco_vazio_mostra_tudo_zerado_sem_quebrar()
    {
        var r = await _inicio.ObterAsync();

        Assert.Equal(new DateTime(2026, 10, 19), r.Dia);
        Assert.Equal((0, 0L, 0L, 0), (r.VendasHoje, r.TotalHojeCentavos, r.TicketMedioHojeCentavos, r.CanceladasHoje));
        Assert.False(r.CaixaAberto);
        Assert.Equal(0, r.ProdutosAbaixoDoMinimo);
        Assert.Empty(r.PioresEstoques);
        Assert.Equal((0, 0L, 0L), (r.ClientesComDivida, r.DividaTotalCentavos, r.DividaVencidaCentavos));
    }

    [Fact]
    public async Task Vendas_de_hoje_somam_e_as_canceladas_ficam_de_fora()
    {
        await VenderAsync(2);                               // 40,00
        await VenderAsync(3);                               // 60,00
        var cancelada = await VenderAsync(50);
        await _vendas.CancelarAsync(cancelada.Id, "Teste");

        var r = await _inicio.ObterAsync();

        Assert.Equal(2, r.VendasHoje);
        Assert.Equal(10000, r.TotalHojeCentavos);
        Assert.Equal(5000, r.TicketMedioHojeCentavos);
        Assert.Equal(1, r.CanceladasHoje);
    }

    [Fact]
    public async Task Hoje_vai_de_zero_hora_a_23h59_do_dia_local_e_ontem_aparece_a_parte()
    {
        _agora = new DateTime(2026, 10, 18, 23, 59, 0);
        await VenderAsync(1);                               // ontem 23:59: 20,00
        _agora = new DateTime(2026, 10, 19, 0, 0, 0);
        await VenderAsync(2);                               // hoje 00:00: 40,00
        _agora = new DateTime(2026, 10, 19, 23, 59, 0);
        await VenderAsync(3);                               // hoje 23:59: 60,00
        _agora = new DateTime(2026, 10, 20, 0, 0, 0);
        await VenderAsync(4);                               // amanhã: não entra

        _agora = new DateTime(2026, 10, 19, 23, 59, 30);
        var r = await _inicio.ObterAsync();

        Assert.Equal(2, r.VendasHoje);
        Assert.Equal(10000, r.TotalHojeCentavos);
        Assert.Equal(2000, r.TotalOntemCentavos);
    }

    [Fact]
    public async Task Caixa_aberto_mostra_desde_quando()
    {
        await _caixa.AbrirAsync(5000);

        var r = await _inicio.ObterAsync();

        Assert.True(r.CaixaAberto);
        Assert.Equal(_agora, r.CaixaAbertoDesde);
    }

    [Fact]
    public async Task Estoque_baixo_conta_so_ativos_com_controle_e_lista_os_piores_primeiro()
    {
        // Cadastra com estoque abaixo do mínimo (estoque, mínimo): 1/5, 0/3, 2/2 (no limite: não é baixo)
        await _produtos.CadastrarAsync(new DadosProduto("Camisa", null, 3000, 1000, 5), 1);
        await _produtos.CadastrarAsync(new DadosProduto("Short", null, 3000, 1000, 3), 0);
        await _produtos.CadastrarAsync(new DadosProduto("Meia", null, 3000, 1000, 2), 2);
        var inativo = await _produtos.CadastrarAsync(new DadosProduto("Antigo", null, 3000, 1000, 9), 0);
        await _produtos.DesativarAsync(inativo.Id);

        var r = await _inicio.ObterAsync();

        Assert.Equal(2, r.ProdutosAbaixoDoMinimo);
        Assert.Equal(new[] { "Camisa", "Short" }, r.PioresEstoques.Select(p => p.Nome)); // -4 vem antes de -3
    }

    [Fact]
    public async Task Divida_e_calculada_por_cliente_com_pagamentos_e_separa_a_vencida()
    {
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));
        var joao = await _clientes.CadastrarAsync(new DadosCliente("João"));

        _agora = new DateTime(2026, 8, 1, 10, 0, 0);
        await VenderAsync(5, FormaPagamento.APrazo, maria.Id);    // 100,00, venceu em 31/08
        _agora = new DateTime(2026, 10, 15, 10, 0, 0);
        await VenderAsync(2, FormaPagamento.APrazo, joao.Id);     // 40,00, vence só em 14/11
        _agora = new DateTime(2026, 10, 19, 14, 0, 0);
        await _clientes.RegistrarPagamentoAsync(maria.Id, 3000, FormaPagamento.Pix);   // Maria ainda deve 70,00

        var r = await _inicio.ObterAsync();

        Assert.Equal(2, r.ClientesComDivida);
        Assert.Equal(11000, r.DividaTotalCentavos);       // 70 + 40
        Assert.Equal(7000, r.DividaVencidaCentavos);      // só a da Maria
        Assert.Equal(1, r.ClientesComDividaVencida);
    }

    [Fact]
    public async Task Cliente_que_quitou_e_venda_a_prazo_cancelada_nao_contam_como_divida()
    {
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));
        await VenderAsync(2, FormaPagamento.APrazo, maria.Id);                       // 40,00
        await _clientes.RegistrarPagamentoAsync(maria.Id, 4000, FormaPagamento.Pix); // quitou
        var outra = await VenderAsync(1, FormaPagamento.APrazo, maria.Id);
        await _vendas.CancelarAsync(outra.Id, "Teste");

        var r = await _inicio.ObterAsync();

        Assert.Equal(0, r.ClientesComDivida);
        Assert.Equal(0, r.DividaTotalCentavos);
    }
}
