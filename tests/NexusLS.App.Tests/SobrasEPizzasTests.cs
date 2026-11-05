using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Vitrine;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Filtro Sobras da loja no Estoque e as pizzas do Mais vendidos.</summary>
public sealed class SobrasEPizzasTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-sobras-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;

    public SobrasEPizzasTests()
    {
        Directory.CreateDirectory(_pasta);
        _produtos = new ProdutoService(_banco.Fabrica);
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private EstoqueLojaViewModel NovoEstoque(AjustesProdutoStore? store = null) =>
        new(_produtos, new EstoqueService(_banco.Fabrica), ajustesCatalogo: store);

    // ---------- Sobras ----------

    [Fact]
    public async Task Filtro_separa_estoque_normal_e_sobras_e_conta_as_sobras()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1), 5);
        await _produtos.CadastrarAsync(new DadosProduto("Retalho", null, 1000, 300, 0, Sobra: true), 2);
        await _produtos.CadastrarAsync(new DadosProduto("Cinto", null, 3000, 1000, 0, Sobra: true), 1);
        var vm = NovoEstoque();
        await AteAsync(() => vm.Produtos.Count == 3, "lista");

        vm.VisaoSobras = true;
        await AteAsync(() => vm.Produtos.Count == 2, "sobras");
        Assert.All(vm.Produtos, p => Assert.True(p.Sobra));
        Assert.Equal("Sobras (2)", vm.TextoSobras);

        vm.VisaoEstoqueNormal = true;
        await AteAsync(() => vm.Produtos.Count == 1, "estoque normal");
        Assert.Equal("Camiseta", vm.Produtos[0].Nome);

        vm.VisaoTodos = true;
        await AteAsync(() => vm.Produtos.Count == 3, "todos");
    }

    [Fact]
    public async Task Sobra_continua_sendo_estoque_vende_no_pdv_e_entra_na_exportacao()
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto("Retalho", null, 1000, 300, 0, Sobra: true), 3);
        var venda = await new VendaService(_banco.Fabrica).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2, null) }, 0, FormaPagamento.Pix, null));

        Assert.Equal(2000, venda.TotalCentavos);
        Assert.Equal(1, (await _produtos.ObterAsync(p.Id))!.EstoqueAtual);
        Assert.Contains("Retalho", ExportadorCsv.Estoque(await _produtos.ListarComTamanhosAsync()));
    }

    [Fact]
    public async Task Adicionar_sobra_abre_o_cadastro_marcado_e_a_sobra_nova_nasce_fora_do_catalogo()
    {
        var store = new AjustesProdutoStore(Path.Combine(_pasta, "catalogo-produtos.json"));
        var vm = NovoEstoque(store);
        await AteAsync(() => !vm.EstaEditando, "formulário");

        vm.AdicionarSobraCommand.Execute(null);
        Assert.True(vm.Sobra);
        vm.Nome = "Vestido sobra";
        vm.PrecoVenda = "80,00";
        await vm.SalvarCommand.ExecuteAsync(null);

        var salvo = (await _produtos.BuscarAsync("Vestido sobra")).Single();
        Assert.True(salvo.Sobra);
        var ajuste = store.Carregar()[salvo.Guid.ToString("D").ToLowerInvariant()];
        Assert.False(ajuste.Visivel);

        // um produto comum não mexe no arquivo do catálogo
        vm.NovoCommand.Execute(null);
        vm.Nome = "Comum";
        vm.PrecoVenda = "10,00";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Single(store.Carregar());
    }

    [Fact]
    public async Task Editar_pode_marcar_e_desmarcar_a_sobra()
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto("Meia", null, 1000, 300, 0), 1);
        var vm = NovoEstoque();
        await AteAsync(() => vm.Produtos.Count == 1, "lista");
        vm.Selecionado = vm.Produtos[0];
        await AteAsync(() => vm.IdEditando == p.Id, "abrir");

        vm.Sobra = true;
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True((await _produtos.ObterAsync(p.Id))!.Sobra);

        vm.Sobra = false;
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.False((await _produtos.ObterAsync(p.Id))!.Sobra);
    }

    // ---------- Pizzas ----------

    [Fact]
    public void Desenho_da_pizza_cobre_o_circulo_e_usa_cinza_para_Outros()
    {
        var fatias = Participacao.Fatias(Enumerable.Range(1, 8).Select(i => ($"P{i}", (long)i * 100)));
        var desenho = PizzaGrafico.Desenhar(fatias);

        Assert.Equal(7, desenho.Count);
        Assert.Equal(PizzaGrafico.CorOutros, desenho[^1].Cor);
        Assert.All(desenho, d => Assert.StartsWith("M 100,100 L", d.Caminho));
        Assert.Contains("P8 · R$", desenho[0].Legenda);

        var inteira = PizzaGrafico.Desenhar(Participacao.Fatias(new[] { ("Único", 500L) }));
        Assert.Contains("0 1 1", inteira.Single().Caminho); // círculo inteiro, dois arcos
    }

    [Fact]
    public async Task Mais_vendidos_monta_as_tres_pizzas_sem_contar_vendas_canceladas()
    {
        var bone = (await _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0, Categoria: "Acessórios"), 50)).Id;
        var camiseta = (await _produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 0, Categoria: "Roupas"), 50)).Id;
        var vendas = new VendaService(_banco.Fabrica);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1, null) }, 0, FormaPagamento.Pix, null));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 1, null) }, 0, FormaPagamento.Dinheiro, 10000));
        var cancelada = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 9, null) }, 0, FormaPagamento.Pix, null));
        await vendas.CancelarAsync(cancelada.Id, "teste do cancelamento", devolverEstoque: true);

        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.PizzaProdutos.Count == 2, "pizzas");

        Assert.True(vm.TemPizzas);
        Assert.Equal(new[] { "Camiseta", "Boné" }, vm.PizzaProdutos.Select(f => f.Rotulo));   // 5.000 e 2.000: a cancelada ficou fora
        Assert.Equal(new[] { "Roupas", "Acessórios" }, vm.PizzaCategorias.Select(f => f.Rotulo));
        Assert.Equal(2, vm.PizzaFormas.Count);
        Assert.Contains("71,4%", vm.PizzaProdutos[0].Legenda);
    }

    [Fact]
    public async Task Sem_vendas_nao_ha_pizzas()
    {
        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.Resumo == "Nenhuma venda neste período.", "resumo");
        Assert.False(vm.TemPizzas);
        Assert.Empty(vm.PizzaFormas);
    }
}
