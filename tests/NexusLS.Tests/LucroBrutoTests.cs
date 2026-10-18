using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Lucro bruto (venda menos custo) e resumo por categoria no relatório de vendas.</summary>
public class LucroBrutoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly RelatorioService _relatorio;

    public LucroBrutoTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica);
        _relatorio = new RelatorioService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private Task<Produto> ProdutoAsync(string nome, long venda, long custo, string? categoria) =>
        _produtos.CadastrarAsync(new DadosProduto(nome, null, venda, custo, 0, Categoria: categoria), 100);

    private static PedidoVenda Pix(long desconto, params ItemCarrinho[] itens) => new(itens, desconto, FormaPagamento.Pix);

    private Task<RelatorioVendas> HojeAsync() => _relatorio.VendasPorPeriodoAsync(DateTime.Today, DateTime.Today);

    [Fact]
    public async Task Lucro_bruto_e_total_vendido_menos_o_custo()
    {
        var bone = await ProdutoAsync("Boné", 2000, 800, "Acessórios");
        await _vendas.FinalizarAsync(Pix(0, new ItemCarrinho(bone.Id, 3)));   // vende 60,00; custo 24,00

        var r = await HojeAsync();

        Assert.Equal(2400, r.CustoTotalCentavos);
        Assert.Equal(3600, r.LucroBrutoCentavos);
        Assert.False(r.CustoEstimado);
    }

    [Fact]
    public async Task Desconto_da_venda_reduz_o_lucro()
    {
        var bone = await ProdutoAsync("Boné", 2000, 800, null);
        await _vendas.FinalizarAsync(Pix(1000, new ItemCarrinho(bone.Id, 3)));   // 60,00 - 10,00 = 50,00

        Assert.Equal(2600, (await HojeAsync()).LucroBrutoCentavos);              // 50,00 - 24,00
    }

    [Fact]
    public async Task Custo_fica_guardado_na_venda_mesmo_se_o_produto_mudar_depois()
    {
        var bone = await ProdutoAsync("Boné", 2000, 800, null);
        await _vendas.FinalizarAsync(Pix(0, new ItemCarrinho(bone.Id, 1)));
        await _produtos.EditarAsync(bone.Id, new DadosProduto("Boné", null, 2000, 1500, 0));   // custo subiu

        var r = await HojeAsync();

        Assert.Equal(800, r.CustoTotalCentavos);
        Assert.False(r.CustoEstimado);
    }

    [Fact]
    public async Task Venda_antiga_sem_custo_guardado_usa_o_custo_atual_e_marca_como_estimativa()
    {
        var bone = await ProdutoAsync("Boné", 2000, 800, null);
        await _vendas.FinalizarAsync(Pix(0, new ItemCarrinho(bone.Id, 2)));
        using (var db = _banco.CriarContexto())
        {
            foreach (var item in db.ItensVenda) item.CustoUnitarioCentavos = null;   // como era antes do registro do custo
            db.SaveChanges();
        }

        var r = await HojeAsync();

        Assert.Equal(1600, r.CustoTotalCentavos);
        Assert.True(r.CustoEstimado);
    }

    [Fact]
    public async Task Canceladas_nao_entram_no_lucro_nem_nas_categorias()
    {
        var bone = await ProdutoAsync("Boné", 2000, 800, "Acessórios");
        var cancelada = await _vendas.FinalizarAsync(Pix(0, new ItemCarrinho(bone.Id, 5)));
        await _vendas.CancelarAsync(cancelada.Id, "Engano");

        var r = await HojeAsync();

        Assert.Equal(0, r.LucroBrutoCentavos);
        Assert.Empty(r.PorCategoria!);
    }

    [Fact]
    public async Task Resumo_por_categoria_soma_faturamento_custo_e_lucro_e_agrupa_os_sem_categoria()
    {
        var bone = await ProdutoAsync("Boné", 2000, 800, "Acessórios");
        var cinto = await ProdutoAsync("Cinto", 5000, 2000, "Acessórios");
        var camiseta = await ProdutoAsync("Camiseta", 4000, 1000, "Roupas");
        var avulso = await ProdutoAsync("Avulso", 1000, 400, null);

        await _vendas.FinalizarAsync(Pix(0, new ItemCarrinho(bone.Id, 2), new ItemCarrinho(cinto.Id, 1)));   // acessórios: 90,00
        await _vendas.FinalizarAsync(Pix(0, new ItemCarrinho(camiseta.Id, 3), new ItemCarrinho(avulso.Id, 1)));

        var categorias = (await HojeAsync()).PorCategoria!;

        Assert.Equal(new[] { "Roupas", "Acessórios", "Sem categoria" }, categorias.Select(c => c.Categoria));
        var acessorios = categorias.Single(c => c.Categoria == "Acessórios");
        Assert.Equal((3, 9000L, 3600L, 5400L),
            (acessorios.Quantidade, acessorios.FaturamentoCentavos, acessorios.CustoCentavos, acessorios.LucroCentavos));
    }
}
