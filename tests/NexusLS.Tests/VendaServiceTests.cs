using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class VendaServiceTests
{
    private static readonly DateTime Hoje = new(2026, 10, 17, 14, 0, 0);

    /// <summary>Banco com dois produtos: Café (R$ 10,00, estoque 5) e Pão (R$ 2,50, estoque 20).</summary>
    private sealed class Cenario : IDisposable
    {
        public BancoDeTeste Banco { get; } = new();
        public DateTime Agora { get; set; } = Hoje;
        public VendaService Vendas { get; }
        public int CafeId { get; private set; }
        public int PaoId { get; private set; }

        public Cenario()
        {
            Vendas = new VendaService(Banco.Fabrica, () => Agora);
        }

        public static async Task<Cenario> CriarAsync()
        {
            var c = new Cenario();
            var produtos = new ProdutoService(c.Banco.Fabrica);
            c.CafeId = (await produtos.CadastrarAsync(new DadosProduto("Café", "111", 1000, 600, 2), 5)).Id;
            c.PaoId = (await produtos.CadastrarAsync(new DadosProduto("Pão", "222", 250, 100, 5), 20)).Id;
            return c;
        }

        public int Estoque(int produtoId)
        {
            using var db = Banco.CriarContexto();
            return db.Produtos.Single(p => p.Id == produtoId).EstoqueAtual;
        }

        public void Dispose() => Banco.Dispose();
    }

    private static ItemCarrinho I(int produtoId, int quantidade) => new(produtoId, quantidade);

    private static PedidoVenda Pix(params ItemCarrinho[] itens) =>
        new(itens, 0, FormaPagamento.Pix);

    // ---------- Finalizar ----------

    [Fact]
    public async Task Finalizar_grava_venda_calcula_total_e_baixa_estoque()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 2), I(c.PaoId, 4)));

        Assert.Equal(3000, venda.SubtotalCentavos); // 2x10,00 + 4x2,50
        Assert.Equal(3000, venda.TotalCentavos);
        Assert.Equal(3, c.Estoque(c.CafeId));
        Assert.Equal(16, c.Estoque(c.PaoId));
        Assert.Equal(2, venda.Itens.Count);
    }

    [Fact]
    public async Task Finalizar_registra_movimentacao_de_venda_ligada_a_venda()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 2)));

        using var db = c.Banco.CriarContexto();
        var mov = db.Movimentacoes.Single(m => m.Tipo == TipoMovimentacao.Venda);
        Assert.Equal(-2, mov.Quantidade);
        Assert.Equal(venda.Id, mov.VendaId);
    }

    [Fact]
    public async Task Mesmo_produto_em_duas_linhas_soma_as_quantidades()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 1), I(c.CafeId, 2)));

        var item = Assert.Single(venda.Itens);
        Assert.Equal(3, item.Quantidade);
        Assert.Equal(2, c.Estoque(c.CafeId));
    }

    [Fact]
    public async Task Estoque_insuficiente_bloqueia_e_nao_grava_nada()
    {
        using var c = await Cenario.CriarAsync();

        // O pão é válido, mas o café (pedido 6, estoque 5) não é: nada pode ser gravado.
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => c.Vendas.FinalizarAsync(Pix(I(c.PaoId, 1), I(c.CafeId, 6))));

        Assert.Contains("Café", erro.Message);
        Assert.Equal(20, c.Estoque(c.PaoId));
        Assert.Equal(5, c.Estoque(c.CafeId));
        using var db = c.Banco.CriarContexto();
        Assert.Empty(db.Vendas);
        Assert.Equal(2, db.Movimentacoes.Count()); // só os dois estoques iniciais
    }

    [Fact]
    public async Task Pode_vender_todo_o_estoque_mas_nao_deixa_negativo()
    {
        using var c = await Cenario.CriarAsync();

        await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 5)));
        Assert.Equal(0, c.Estoque(c.CafeId));

        await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 1))));
        Assert.Equal(0, c.Estoque(c.CafeId));
    }

    [Fact]
    public async Task Carrinho_vazio_ou_quantidade_invalida_falha()
    {
        using var c = await Cenario.CriarAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(Pix()));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 0))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(Pix(I(c.CafeId, -1))));
    }

    [Fact]
    public async Task Produto_desativado_ou_inexistente_nao_pode_ser_vendido()
    {
        using var c = await Cenario.CriarAsync();
        await new ProdutoService(c.Banco.Fabrica).DesativarAsync(c.CafeId);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 1))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(Pix(I(999, 1))));
    }

    [Fact]
    public async Task Item_guarda_nome_e_preco_da_hora_da_venda()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 1)));

        await new ProdutoService(c.Banco.Fabrica)
            .EditarAsync(c.CafeId, new DadosProduto("Café Premium", "111", 9999, 600, 2));

        using var db = c.Banco.CriarContexto();
        var item = db.ItensVenda.Single(i => i.VendaId == venda.Id);
        Assert.Equal("Café", item.NomeProduto);
        Assert.Equal(1000, item.PrecoUnitarioCentavos);
    }

    // ---------- Desconto e pagamento ----------

    [Fact]
    public async Task Desconto_reduz_o_total()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(c.CafeId, 2) }, 500, FormaPagamento.CartaoDebito));

        Assert.Equal(2000, venda.SubtotalCentavos);
        Assert.Equal(500, venda.DescontoCentavos);
        Assert.Equal(1500, venda.TotalCentavos);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2001)]
    public async Task Desconto_negativo_ou_maior_que_o_subtotal_falha(long desconto)
    {
        using var c = await Cenario.CriarAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(c.CafeId, 2) }, desconto, FormaPagamento.Pix)));
        Assert.Equal(5, c.Estoque(c.CafeId));
    }

    [Fact]
    public async Task Dinheiro_calcula_o_troco()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.CafeId, 1), new ItemCarrinho(c.PaoId, 2) }, 100, FormaPagamento.Dinheiro, 5000));

        Assert.Equal(1400, venda.TotalCentavos); // 10,00 + 5,00 - 1,00
        Assert.Equal(5000, venda.ValorRecebidoCentavos);
        Assert.Equal(3600, venda.TrocoCentavos);
    }

    [Fact]
    public async Task Dinheiro_exige_valor_recebido_suficiente()
    {
        using var c = await Cenario.CriarAsync();
        var itens = new[] { new ItemCarrinho(c.CafeId, 1) };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            c.Vendas.FinalizarAsync(new PedidoVenda(itens, 0, FormaPagamento.Dinheiro, null)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            c.Vendas.FinalizarAsync(new PedidoVenda(itens, 0, FormaPagamento.Dinheiro, 999)));
        Assert.Equal(5, c.Estoque(c.CafeId));
    }

    [Fact]
    public async Task Pix_e_cartao_nao_tem_troco()
    {
        using var c = await Cenario.CriarAsync();

        var venda = await c.Vendas.FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(c.CafeId, 1) }, 0, FormaPagamento.Pix, 99999));

        Assert.Equal(0, venda.TrocoCentavos);
        Assert.Null(venda.ValorRecebidoCentavos);
    }

    // ---------- Cancelar ----------

    [Fact]
    public async Task Cancelar_devolve_o_estoque_e_marca_a_venda_sem_apagar()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 2), I(c.PaoId, 4)));

        var cancelada = await c.Vendas.CancelarAsync(venda.Id);

        Assert.True(cancelada.Cancelada);
        Assert.Equal(5, c.Estoque(c.CafeId));
        Assert.Equal(20, c.Estoque(c.PaoId));
        using var db = c.Banco.CriarContexto();
        var salva = db.Vendas.Include(v => v.Itens).Single(); // continua existindo
        Assert.True(salva.Cancelada);
        Assert.Equal(Hoje, salva.DataCancelamento);
        Assert.Equal(2, salva.Itens.Count);
    }

    [Fact]
    public async Task Cancelar_registra_movimentacoes_de_cancelamento()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 2)));

        await c.Vendas.CancelarAsync(venda.Id);

        using var db = c.Banco.CriarContexto();
        var mov = db.Movimentacoes.Single(m => m.Tipo == TipoMovimentacao.CancelamentoVenda);
        Assert.Equal(2, mov.Quantidade);
        Assert.Equal(venda.Id, mov.VendaId);
    }

    [Fact]
    public async Task Cancelar_duas_vezes_falha_e_nao_devolve_estoque_em_dobro()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 2)));
        await c.Vendas.CancelarAsync(venda.Id);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.CancelarAsync(venda.Id));

        Assert.Equal(5, c.Estoque(c.CafeId));
    }

    [Fact]
    public async Task Cancelar_venda_inexistente_falha()
    {
        using var c = await Cenario.CriarAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Vendas.CancelarAsync(123));
    }

    [Fact]
    public async Task Cancelar_funciona_mesmo_com_produto_desativado()
    {
        using var c = await Cenario.CriarAsync();
        var venda = await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 2)));
        await new ProdutoService(c.Banco.Fabrica).DesativarAsync(c.CafeId);

        await c.Vendas.CancelarAsync(venda.Id);

        Assert.Equal(5, c.Estoque(c.CafeId));
    }

    // ---------- Vendas do dia ----------

    [Fact]
    public async Task Lista_so_as_vendas_do_dia_e_o_total_ignora_canceladas()
    {
        using var c = await Cenario.CriarAsync();
        c.Agora = Hoje.AddDays(-1);
        await c.Vendas.FinalizarAsync(Pix(I(c.PaoId, 1)));            // ontem
        c.Agora = Hoje;
        await c.Vendas.FinalizarAsync(Pix(I(c.CafeId, 1)));           // hoje, 10,00
        var cancelar = await c.Vendas.FinalizarAsync(Pix(I(c.PaoId, 2))); // hoje, 5,00
        await c.Vendas.CancelarAsync(cancelar.Id);

        var doDia = await c.Vendas.ListarDoDiaAsync();

        Assert.Equal(2, doDia.Count);                      // a cancelada aparece na lista...
        Assert.Equal(1000, VendaService.TotalValido(doDia)); // ...mas não entra no total
    }
}
