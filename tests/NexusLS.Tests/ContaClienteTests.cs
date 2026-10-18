using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Venda a prazo como dívida: pagamento parcial (FIFO), limite de crédito e bloqueio de cancelamento.</summary>
public class ContaClienteTests
{
    private static readonly DateTime Inicio = new(2026, 10, 1, 9, 0, 0);

    private sealed class Cenario : IDisposable
    {
        public BancoDeTeste Banco { get; } = new();
        public DateTime Agora { get; set; } = Inicio;
        public VendaService Vendas { get; }
        public ClienteService Clientes { get; }
        public CaixaService Caixa { get; }
        public int ProdutoId { get; private set; }

        public Cenario()
        {
            Vendas = new VendaService(Banco.Fabrica, () => Agora);
            Clientes = new ClienteService(Banco.Fabrica, () => Agora);
            Caixa = new CaixaService(Banco.Fabrica, () => Agora);
        }

        public static async Task<Cenario> CriarAsync()
        {
            var c = new Cenario();
            c.ProdutoId = (await new ProdutoService(c.Banco.Fabrica)
                .CadastrarAsync(new DadosProduto("Boné", null, 1000, 600, 2), 100)).Id;
            return c;
        }

        public Task<Cliente> NovoClienteAsync(long limite = 0) =>
            Clientes.CadastrarAsync(new DadosCliente("Maria", LimiteCreditoCentavos: limite));

        /// <summary>Vende a prazo (cada unidade custa R$ 10,00) um dia depois da anterior.</summary>
        public Task<Venda> VenderAPrazoAsync(int clienteId, int quantidade)
        {
            Agora = Agora.AddDays(1);
            return Vendas.FinalizarAsync(new PedidoVenda(
                new[] { new ItemCarrinho(ProdutoId, quantidade) }, 0, FormaPagamento.APrazo, ClienteId: clienteId));
        }

        public void Dispose() => Banco.Dispose();
    }

    [Fact]
    public async Task Venda_a_prazo_vira_titulo_com_vencimento_em_30_dias()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync();
        var venda = await c.VenderAPrazoAsync(maria.Id, 3);

        var conta = await c.Clientes.ContaAsync(maria.Id);

        var titulo = Assert.Single(conta.Titulos);
        Assert.Equal(venda.Id, titulo.VendaId);
        Assert.Equal(3000, titulo.SaldoCentavos);
        Assert.Equal(venda.DataHora.AddDays(30), titulo.Vencimento);
        Assert.Equal(3000, conta.SaldoDevedorCentavos);
    }

    [Fact]
    public async Task Pagamento_abate_primeiro_a_venda_mais_antiga()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync();
        await c.VenderAPrazoAsync(maria.Id, 2);   // 20,00
        await c.VenderAPrazoAsync(maria.Id, 3);   // 30,00

        await c.Clientes.RegistrarPagamentoAsync(maria.Id, 2500, FormaPagamento.Pix);   // quita a 1ª e paga 5,00 da 2ª

        var conta = await c.Clientes.ContaAsync(maria.Id);
        Assert.True(conta.Titulos[0].Quitado);
        Assert.Equal(500, conta.Titulos[1].PagoCentavos);
        Assert.Equal(2500, conta.SaldoDevedorCentavos);
        Assert.Equal(2500, conta.TotalPagoCentavos);
    }

    [Fact]
    public async Task Pagamento_maior_que_a_divida_ou_invalido_e_recusado()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync();
        await c.VenderAPrazoAsync(maria.Id, 1);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Clientes.RegistrarPagamentoAsync(maria.Id, 1001, FormaPagamento.Dinheiro));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Clientes.RegistrarPagamentoAsync(maria.Id, 0, FormaPagamento.Dinheiro));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Clientes.RegistrarPagamentoAsync(maria.Id, 100, FormaPagamento.APrazo));
        await c.Clientes.RegistrarPagamentoAsync(maria.Id, 1000, FormaPagamento.Dinheiro, "  quitou ");

        var pagamento = Assert.Single(await c.Clientes.PagamentosAsync(maria.Id));
        Assert.Equal("quitou", pagamento.Observacao);
        Assert.Equal("Caixa", pagamento.Operador);
    }

    [Fact]
    public async Task Titulo_vence_depois_de_30_dias_sem_pagamento()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync();
        var venda = await c.VenderAPrazoAsync(maria.Id, 1);
        var titulo = (await c.Clientes.ContaAsync(maria.Id)).Titulos.Single();

        Assert.False(titulo.VencidoEm(venda.DataHora.AddDays(30)));
        Assert.True(titulo.VencidoEm(venda.DataHora.AddDays(31)));
    }

    [Fact]
    public async Task Limite_de_credito_bloqueia_venda_a_prazo_que_passa_dele()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync(limite: 5000);   // 50,00
        await c.VenderAPrazoAsync(maria.Id, 3);                // deve 30,00

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.VenderAPrazoAsync(maria.Id, 3)); // 60,00 > 50,00
        await c.VenderAPrazoAsync(maria.Id, 2);                                                    // 50,00 exatamente pode

        await c.Clientes.RegistrarPagamentoAsync(maria.Id, 2000, FormaPagamento.Pix);              // abre espaço
        await c.VenderAPrazoAsync(maria.Id, 2);
    }

    [Fact]
    public async Task Limite_zero_significa_sem_limite_e_limite_negativo_e_recusado()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync(limite: 0);
        await c.VenderAPrazoAsync(maria.Id, 50);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Clientes.EditarAsync(maria.Id, new DadosCliente("Maria", LimiteCreditoCentavos: -1)));
    }

    [Fact]
    public async Task Venda_que_recebeu_pagamento_nao_pode_ser_cancelada()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync();
        var primeira = await c.VenderAPrazoAsync(maria.Id, 2);
        var segunda = await c.VenderAPrazoAsync(maria.Id, 2);
        await c.Clientes.RegistrarPagamentoAsync(maria.Id, 500, FormaPagamento.Dinheiro);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.CancelarAsync(primeira.Id, "Engano"));
        await c.Vendas.CancelarAsync(segunda.Id, "Engano");      // a 2ª não recebeu nada: pode

        var conta = await c.Clientes.ContaAsync(maria.Id);
        Assert.Single(conta.Titulos);
        Assert.Equal(1500, conta.SaldoDevedorCentavos);
    }

    [Fact]
    public async Task Venda_a_prazo_cancelada_sem_pagamentos_sai_da_divida()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync();
        var venda = await c.VenderAPrazoAsync(maria.Id, 2);

        await c.Vendas.CancelarAsync(venda.Id, "Engano");

        Assert.Equal(0, (await c.Clientes.ContaAsync(maria.Id)).SaldoDevedorCentavos);
    }

    [Fact]
    public async Task Recebimentos_de_divida_entram_no_esperado_do_caixa_aberto()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await c.NovoClienteAsync();
        await c.VenderAPrazoAsync(maria.Id, 3);
        c.Agora = c.Agora.AddDays(1);
        var turno = await c.Caixa.AbrirAsync(1000);

        await c.Clientes.RegistrarPagamentoAsync(maria.Id, 1000, FormaPagamento.Dinheiro);
        await c.Clientes.RegistrarPagamentoAsync(maria.Id, 500, FormaPagamento.Pix);
        await c.Clientes.RegistrarPagamentoAsync(maria.Id, 300, FormaPagamento.CartaoDebito);

        var resumo = await c.Caixa.ResumoAsync(turno.Id);
        Assert.Equal(2000, resumo.EsperadoDinheiroCentavos);
        Assert.Equal(500, resumo.EsperadoPixCentavos);
        Assert.Equal(300, resumo.EsperadoCartaoCentavos);
    }
}
