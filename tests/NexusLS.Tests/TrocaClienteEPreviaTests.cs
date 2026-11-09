using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class TrocaClienteEPreviaTests
{
    private static async Task<(BancoDeTeste banco, VendaService vendas, ClienteService clientes, Produto produto)> Cenario()
    {
        var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 10);
        return (banco, new VendaService(banco.Fabrica), new ClienteService(banco.Fabrica), p);
    }

    [Fact]
    public async Task Venda_a_vista_troca_e_tira_o_cliente_e_registra_na_trilha()
    {
        var (banco, vendas, clientes, p) = await Cenario();
        using var _ = banco;
        var ana = await clientes.CadastrarAsync(new DadosCliente("Ana"));
        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));

        var trocada = await vendas.AlterarClienteAsync(venda.Id, ana.Id);
        Assert.Equal("Ana", trocada.Cliente!.Nome);
        Assert.Equal("Ana", (await vendas.ListarDoDiaAsync()).Single().Cliente!.Nome);

        await vendas.AlterarClienteAsync(venda.Id, null);
        Assert.Null((await vendas.ListarDoDiaAsync()).Single().ClienteId);
    }

    [Fact]
    public async Task Venda_a_prazo_nao_fica_sem_cliente_e_a_divida_acompanha_a_troca()
    {
        var (banco, vendas, clientes, p) = await Cenario();
        using var _ = banco;
        var ana = await clientes.CadastrarAsync(new DadosCliente("Ana"));
        var beto = await clientes.CadastrarAsync(new DadosCliente("Beto"));
        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.APrazo, ClienteId: ana.Id));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => vendas.AlterarClienteAsync(venda.Id, null));

        await vendas.AlterarClienteAsync(venda.Id, beto.Id);
        Assert.Equal(0, (await clientes.ContaAsync(ana.Id)).SaldoDevedorCentavos);
        Assert.Equal(5000, (await clientes.ContaAsync(beto.Id)).SaldoDevedorCentavos);
    }

    [Fact]
    public async Task Venda_cancelada_nao_troca_de_cliente()
    {
        var (banco, vendas, clientes, p) = await Cenario();
        using var _ = banco;
        var ana = await clientes.CadastrarAsync(new DadosCliente("Ana"));
        var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));
        await vendas.CancelarAsync(venda.Id, "teste");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => vendas.AlterarClienteAsync(venda.Id, ana.Id));
    }

    [Fact]
    public void Previa_estilizada_tem_o_mesmo_texto_do_cupom_e_marca_o_negrito()
    {
        var texto = MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), new ConfiguracaoLoja { NomeLoja = "Life" });
        var linhas = MontadorCupom.Estilizar(texto);

        Assert.Equal(texto, string.Join("\n", linhas.Select(l => string.Concat(l.Select(s => s.Texto)))));
        Assert.True(linhas[0].All(s => s.Negrito));                                                  // nome da loja
        Assert.Contains(linhas, l => l.Any(s => s.Negrito && s.Texto.Contains("CUPOM NÃO FISCAL")));
        Assert.Contains(linhas, l => l.Any(s => s.Negrito && s.Texto == "Operador:") && l.Any(s => !s.Negrito));
        Assert.Contains(linhas, l => l.All(s => s.Negrito) && l.Any(s => s.Texto.StartsWith("TOTAL A PAGAR:")));
    }
}
