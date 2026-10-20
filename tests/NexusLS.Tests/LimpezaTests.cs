using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class LimpezaTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 16, 0, 0);

    private readonly BancoDeTeste _banco = new();
    private int _backupsFeitos;
    private Func<Task<string>> _backup;

    public LimpezaTests()
    {
        _backup = () => { _backupsFeitos++; return Task.FromResult(@"C:\Backups\nexus_2026-10-20_160000_pre-limpeza.db"); };
    }

    public void Dispose() => _banco.Dispose();

    private LimpezaService Limpeza() => new(_banco.Fabrica, () => _backup(), () => Agora);

    /// <summary>Uma loja com um pouco de tudo: produto, vendas (uma cancelada, uma a prazo com pagamento), pedido, compra, conta, caixa e funcionário.</summary>
    private async Task PopularAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica, () => Agora);
        var clientes = new ClienteService(_banco.Fabrica);

        var camisa = await produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 2), 20);
        var cliente = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        var fornecedor = await new FornecedorService(_banco.Fabrica).CadastrarAsync(new DadosFornecedor("Têxtil Ltda"));

        var v1 = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camisa.Id, 2) }, 0, FormaPagamento.Pix));
        await vendas.CancelarAsync(v1.Id, "teste de cancelamento");
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camisa.Id, 3) }, 0, FormaPagamento.APrazo, ClienteId: cliente.Id));
        await clientes.RegistrarPagamentoAsync(cliente.Id, 1000, FormaPagamento.Dinheiro);

        await new CompraService(_banco.Fabrica, () => Agora).RegistrarAsync(new DadosCompra(
            new[] { new ItemCompraDados(camisa.Id, 5, 1800) }, fornecedor.Id,
            Pagamento: new PagamentoCompra(2, Agora.AddDays(30))));
        await new FinanceiroService(_banco.Fabrica, () => Agora).CriarAsync(new DadosLancamento(
            TipoLancamento.Despesa, "Aluguel", 100000, Agora.AddDays(5)));
        await new PedidoService(_banco.Fabrica, () => Agora).CriarAsync(new DadosPedido(
            OrigemPedido.WhatsApp, new[] { new ItemPedidoDados(camisa.Id, 1) }, ClienteNome: "João"));
        await new CaixaService(_banco.Fabrica, () => Agora).AbrirAsync(5000);

        await new FuncionarioService(_banco.Fabrica, () => Agora).CriarPrimeiroAdministradorAsync("Admin", "1234");
    }

    private (int Produtos, int Clientes, int Fornecedores, int Vendas, int Itens, int Movs, int Pedidos, int Compras, int Contas, int Turnos, int Pagamentos, int Funcionarios, int Atividades) Contar()
    {
        using var db = _banco.CriarContexto();
        return (db.Produtos.Count(), db.Clientes.Count(), db.Fornecedores.Count(), db.Vendas.Count(), db.ItensVenda.Count(),
            db.Movimentacoes.Count(), db.Pedidos.Count(), db.Compras.Count(), db.Lancamentos.Count(), db.TurnosCaixa.Count(),
            db.PagamentosCliente.Count(), db.Funcionarios.Count(), db.Atividades.Count());
    }

    [Fact]
    public async Task Sem_digitar_APAGAR_nada_acontece_nem_backup()
    {
        await PopularAsync();
        var antes = Contar();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Limpeza().LimparAsync(NivelLimpeza.Tudo, "apagar"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Limpeza().LimparAsync(NivelLimpeza.Tudo, ""));

        Assert.Equal(antes, Contar());
        Assert.Equal(0, _backupsFeitos);
    }

    [Fact]
    public async Task Se_o_backup_falhar_nada_e_apagado()
    {
        await PopularAsync();
        var antes = Contar();
        _backup = () => throw new IOException("disco cheio");

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => Limpeza().LimparAsync(NivelLimpeza.Movimento, "APAGAR"));

        Assert.Contains("disco cheio", erro.Message);
        Assert.Equal(antes, Contar());
    }

    [Fact]
    public async Task Nivel_movimento_apaga_vendas_caixa_e_financeiro_mas_guarda_os_cadastros_e_o_estoque()
    {
        await PopularAsync();
        int estoqueAntes;
        using (var db = _banco.CriarContexto()) estoqueAntes = db.Produtos.Single().EstoqueAtual;

        var r = await Limpeza().LimparAsync(NivelLimpeza.Movimento, "APAGAR");

        var c = Contar();
        Assert.Equal(0, c.Vendas + c.Itens + c.Movs + c.Pedidos + c.Compras + c.Contas + c.Turnos + c.Pagamentos);
        Assert.Equal((1, 1, 1), (c.Produtos, c.Clientes, c.Fornecedores));
        Assert.Equal(1, c.Funcionarios);
        using (var db = _banco.CriarContexto()) Assert.Equal(estoqueAntes, db.Produtos.Single().EstoqueAtual);
        Assert.Equal(1, _backupsFeitos);
        Assert.True(r.Apagados["Vendas"] >= 2);
        Assert.True(r.Total > 0);
    }

    [Fact]
    public async Task Nivel_tudo_apaga_tambem_produtos_clientes_e_fornecedores_mas_guarda_funcionarios()
    {
        await PopularAsync();

        await Limpeza().LimparAsync(NivelLimpeza.Tudo, "APAGAR");

        var c = Contar();
        Assert.Equal(0, c.Produtos + c.Clientes + c.Fornecedores + c.Vendas + c.Pedidos + c.Compras + c.Contas);
        Assert.Equal(1, c.Funcionarios);
    }

    [Fact]
    public async Task A_limpeza_fica_na_trilha_e_a_trilha_anterior_nao_e_apagada()
    {
        await PopularAsync();
        var atividadesAntes = Contar().Atividades; // cancelamento da venda + cadastro etc.
        Assert.True(atividadesAntes > 0);

        await Limpeza().LimparAsync(NivelLimpeza.Tudo, "APAGAR");

        using var db = _banco.CriarContexto();
        Assert.Equal(atividadesAntes + 1, db.Atividades.Count());
        var ultima = db.Atividades.OrderByDescending(a => a.Id).First();
        Assert.Equal(TipoAtividade.DadosApagados, ultima.Tipo);
        Assert.Contains("pre-limpeza", ultima.Descricao);
    }

    [Fact]
    public async Task Depois_de_limpar_o_sistema_continua_usavel()
    {
        await PopularAsync();
        await Limpeza().LimparAsync(NivelLimpeza.Tudo, "APAGAR");

        var produtos = new ProdutoService(_banco.Fabrica);
        var novo = await produtos.CadastrarAsync(new DadosProduto("Bermuda", "999", 7000, 3000, 1), 5);
        var venda = await new VendaService(_banco.Fabrica, () => Agora)
            .FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(novo.Id, 1) }, 0, FormaPagamento.Pix));

        Assert.Equal(7000, venda.TotalCentavos);
    }
}
