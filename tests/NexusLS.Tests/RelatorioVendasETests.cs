using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class RelatorioVendasTests
{
    private sealed class Cenario : IDisposable
    {
        public BancoDeTeste Banco { get; } = new();
        public DateTime Agora { get; set; } = new(2026, 10, 10, 10, 0, 0);
        public VendaService Vendas { get; }
        public RelatorioService Relatorio { get; }
        public int BoneId { get; private set; }

        public Cenario()
        {
            Vendas = new VendaService(Banco.Fabrica, () => Agora);
            Relatorio = new RelatorioService(Banco.Fabrica);
        }

        public static async Task<Cenario> CriarAsync()
        {
            var c = new Cenario();
            c.BoneId = (await new ProdutoService(c.Banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 500)).Id;
            return c;
        }

        public Task<Venda> VenderAsync(int quantidade, FormaPagamento forma = FormaPagamento.Pix, long desconto = 0) =>
            Vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(BoneId, quantidade) }, desconto, forma,
                forma == FormaPagamento.Dinheiro ? 1_000_000 : null));

        public void Dispose() => Banco.Dispose();
    }

    [Fact]
    public async Task Totais_descontos_e_ticket_medio_ignoram_as_canceladas()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(2, desconto: 100);     // 40,00 - 1,00 = 39,00
        await c.VenderAsync(3);                    // 60,00
        var cancelada = await c.VenderAsync(50);
        await c.Vendas.CancelarAsync(cancelada.Id, "Teste");

        var r = await c.Relatorio.VendasPorPeriodoAsync(c.Agora, c.Agora);

        Assert.Equal((2, 1), (r.QuantidadeVendas, r.QuantidadeCanceladas));
        Assert.Equal(9900, r.TotalCentavos);
        Assert.Equal(100, r.DescontosCentavos);
        Assert.Equal(4950, r.TicketMedioCentavos);
        Assert.Equal(3, r.Vendas.Count);                   // a cancelada aparece na lista
        Assert.Contains(r.Vendas, v => v.Cancelada);
    }

    [Fact]
    public async Task Separa_por_dia_e_por_forma_de_pagamento()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(1, FormaPagamento.Dinheiro);                                   // dia 10: 20,00
        await c.VenderAsync(2, FormaPagamento.Pix);                                        // dia 10: 40,00
        c.Agora = c.Agora.AddDays(2);
        await c.VenderAsync(5, FormaPagamento.Dinheiro);                                   // dia 12: 100,00

        var r = await c.Relatorio.VendasPorPeriodoAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        Assert.Equal(
            new[] { (new DateTime(2026, 10, 10), 2, 6000L), (new DateTime(2026, 10, 12), 1, 10000L) },
            r.PorDia.Select(d => (d.Dia, d.Vendas, d.TotalCentavos)));
        Assert.Equal(
            new[] { (FormaPagamento.Dinheiro, 2, 12000L), (FormaPagamento.Pix, 1, 4000L) },
            r.PorForma.Select(f => (f.Forma, f.Vendas, f.TotalCentavos)));
    }

    [Fact]
    public async Task Periodo_inclui_os_dois_dias_inteiros_e_nada_fora()
    {
        using var c = await Cenario.CriarAsync();
        c.Agora = new DateTime(2026, 10, 9, 23, 59, 0);  await c.VenderAsync(1);
        c.Agora = new DateTime(2026, 10, 10, 0, 0, 0);   await c.VenderAsync(1);
        c.Agora = new DateTime(2026, 10, 11, 23, 59, 0); await c.VenderAsync(1);
        c.Agora = new DateTime(2026, 10, 12, 0, 0, 0);   await c.VenderAsync(1);

        var r = await c.Relatorio.VendasPorPeriodoAsync(new DateTime(2026, 10, 10, 18, 0, 0), new DateTime(2026, 10, 11, 6, 0, 0));

        Assert.Equal(2, r.QuantidadeVendas);
        Assert.Equal((new DateTime(2026, 10, 10), new DateTime(2026, 10, 11)), (r.De, r.Ate));
    }

    [Fact]
    public async Task Sem_vendas_tudo_zerado_e_data_invertida_e_recusada()
    {
        using var c = await Cenario.CriarAsync();

        var r = await c.Relatorio.VendasPorPeriodoAsync(c.Agora, c.Agora);

        Assert.Equal((0, 0L, 0L), (r.QuantidadeVendas, r.TotalCentavos, r.TicketMedioCentavos));
        Assert.Empty(r.Vendas);
        Assert.Empty(r.PorDia);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Relatorio.VendasPorPeriodoAsync(c.Agora.AddDays(1), c.Agora));
    }

    [Fact]
    public async Task Ticket_medio_e_arredondado_ao_centavo()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(1); await c.VenderAsync(1); await c.VenderAsync(1, desconto: 1); // 20,00 + 20,00 + 19,99 = 59,99 / 3

        var r = await c.Relatorio.VendasPorPeriodoAsync(c.Agora, c.Agora);

        Assert.Equal(2000, r.TicketMedioCentavos); // 5999 / 3 = 1999,67 centavos: arredonda para 2000
    }

    [Fact]
    public async Task A_lista_traz_itens_e_cliente_para_a_exportacao()
    {
        using var c = await Cenario.CriarAsync();
        var maria = await new ClienteService(c.Banco.Fabrica).CadastrarAsync(new DadosCliente("Maria"));
        await c.Vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(c.BoneId, 1) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));

        var venda = Assert.Single((await c.Relatorio.VendasPorPeriodoAsync(c.Agora, c.Agora)).Vendas);

        Assert.Equal("Maria", venda.Cliente!.Nome);
        Assert.Single(venda.Itens);
    }
}

public class HistoricoClienteTests
{
    [Fact]
    public async Task Soma_as_compras_validas_separa_o_a_prazo_e_guarda_a_ultima_data()
    {
        using var banco = new BancoDeTeste();
        var agora = new DateTime(2026, 10, 1, 9, 0, 0);
        var vendas = new VendaService(banco.Fabrica, () => agora);
        var clientes = new ClienteService(banco.Fabrica);
        var bone = await new ProdutoService(banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 100);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        var outra = await clientes.CadastrarAsync(new DadosCliente("Outra"));

        Task<Venda> Vender(int qtd, FormaPagamento forma, int cliente) =>
            vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, qtd) }, 0, forma, ClienteId: cliente));

        await Vender(1, FormaPagamento.Pix, maria.Id);                            // 20,00
        agora = agora.AddDays(5);
        await Vender(3, FormaPagamento.APrazo, maria.Id);                         // 60,00 a prazo
        var cancelada = await Vender(10, FormaPagamento.APrazo, maria.Id);        // não conta
        await vendas.CancelarAsync(cancelada.Id, "Teste");
        await Vender(7, FormaPagamento.Pix, outra.Id);                            // de outra pessoa

        var h = await clientes.HistoricoAsync(maria.Id);

        Assert.Equal((2, 8000L, 6000L), (h.Compras, h.TotalCompradoCentavos, h.TotalAPrazoCentavos));
        Assert.Equal(new DateTime(2026, 10, 6, 9, 0, 0), h.UltimaCompra);
        Assert.Equal(3, h.Vendas.Count);                                          // a cancelada aparece
        Assert.True(h.Vendas[0].DataHora >= h.Vendas[^1].DataHora);               // mais recente primeiro
    }

    [Fact]
    public async Task Cliente_sem_compras_tem_historico_vazio()
    {
        using var banco = new BancoDeTeste();
        var clientes = new ClienteService(banco.Fabrica);
        var novo = await clientes.CadastrarAsync(new DadosCliente("Novo"));

        var h = await clientes.HistoricoAsync(novo.Id);

        Assert.Equal((0, 0L, 0L), (h.Compras, h.TotalCompradoCentavos, h.TotalAPrazoCentavos));
        Assert.Null(h.UltimaCompra);
        Assert.Empty(h.Vendas);
    }
}
