using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("OperadorAtual")]
public sealed class RelatoriosGraficosTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 10, 12, 15, 0, 0);
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 400 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    private async Task<(int camiseta, int bone)> ProdutosAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta", "1", 10000, 4000, 1, Categoria: "Camisetas"), 100)).Id;
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné", "2", 3000, 1000, 1, Categoria: "Acessórios"), 100)).Id;
        return (camiseta, bone);
    }

    private Task VenderAsync(DateTime dia, FormaPagamento forma, long desconto, params ItemCarrinho[] itens) =>
        new VendaService(_banco.Fabrica, () => dia.AddHours(10)).FinalizarAsync(new PedidoVenda(itens, desconto, forma, forma == FormaPagamento.Dinheiro ? 100000 : null));

    [Fact]
    public async Task Os_graficos_da_tela_batem_com_os_cartoes_e_com_as_tabelas()
    {
        var (camiseta, bone) = await ProdutosAsync();
        await VenderAsync(new DateTime(2026, 10, 10), FormaPagamento.Pix, 500, new ItemCarrinho(camiseta, 2), new ItemCarrinho(bone, 1));
        await VenderAsync(new DateTime(2026, 10, 12), FormaPagamento.Dinheiro, 0, new ItemCarrinho(bone, 4));
        await VenderAsync(new DateTime(2026, 10, 8), FormaPagamento.Pix, 0, new ItemCarrinho(camiseta, 1)); // período anterior (7 a 9/10, mesmo tamanho de 10 a 12/10)

        var vm = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica), Hoje.Date);
        vm.De = new DateTime(2026, 10, 10);
        await Esperar(() => vm.TemGraficos && vm.Vendas.Count == 2 && vm.ColunasPorDia.Count == 3);

        // três dias (10, 11 e 12/10), com o dia sem venda zerado
        Assert.Equal(3, vm.ColunasPorDia.Count);
        Assert.Equal(0, vm.ColunasPorDia[1].Fracao);
        Assert.Equal(1, vm.ColunasPorDia.Max(c => Math.Max(c.Fracao, c.FracaoAnterior)), 6);
        // o primeiro cartão de comparação é o mesmo faturamento do cartão "Total vendido"
        Assert.Equal(vm.TotalTexto, vm.Comparacao[0].Valor);
        Assert.Equal(vm.QuantidadeTexto, vm.Comparacao[1].Valor);
        Assert.Equal(vm.TicketMedioTexto, vm.Comparacao[3].Valor);
        // formas e categorias somam o mesmo total
        Assert.Equal(vm.TotalTexto, vm.TotalFormasTexto);
        Assert.Equal(vm.TotalTexto, vm.TotalCategoriasTexto);
        Assert.Equal(2, vm.PizzaFormas.Count);
        Assert.Equal(2, vm.PizzaCategorias.Count);
        // o que mais saiu (peças): Boné (5) antes da Camiseta (2); a maior barra vale 1
        Assert.Equal("Boné", vm.RankingPecas[0].Rotulo);
        Assert.Equal(1, vm.RankingPecas[0].Fracao);
        Assert.True(vm.RankingPecas[1].Fracao is > 0 and < 1);
        Assert.True(vm.TemComparacao);
        Assert.Contains("07/10/2026 a 09/10/2026", vm.PeriodoAnteriorTexto);
        Assert.StartsWith("▲", vm.Comparacao[0].Variacao);
    }

    [Fact]
    public async Task Sem_venda_no_periodo_anterior_a_comparacao_some_e_sem_venda_nenhuma_nao_ha_graficos()
    {
        var (camiseta, _) = await ProdutosAsync();
        await VenderAsync(new DateTime(2026, 10, 12), FormaPagamento.Pix, 0, new ItemCarrinho(camiseta, 1));

        var vm = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica), Hoje.Date);
        await Esperar(() => vm.TemGraficos);
        Assert.False(vm.TemComparacao);
        Assert.All(vm.Comparacao, c => Assert.Contains("sem período anterior", c.Variacao));
        Assert.All(vm.ColunasPorDia, c => Assert.Equal(0, c.FracaoAnterior));

        vm.Ate = new DateTime(2026, 10, 12);
        vm.De = new DateTime(2026, 9, 1);   // setembro não teve venda
        vm.Ate = new DateTime(2026, 9, 30);
        await Esperar(() => !vm.TemGraficos && vm.QuantidadeTexto == "0");
        Assert.Empty(vm.RankingPecas);
        Assert.Empty(vm.PizzaFormas);
    }
}
