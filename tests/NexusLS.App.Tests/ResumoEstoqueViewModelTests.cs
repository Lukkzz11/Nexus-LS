using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class ResumoEstoqueViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    [Fact]
    public async Task Carrega_cartoes_graficos_e_linha_de_movimentacoes()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        await produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 5, Categoria: "Camisetas"), 10);
        await produtos.CadastrarAsync(new DadosProduto("Retalho", null, 1000, 300, 0, Categoria: "Bonés", Sobra: true), 4);
        await produtos.CadastrarAsync(new DadosProduto("Zerado", null, 800, 100, 2), 0);

        var vm = new ResumoEstoqueViewModel(produtos, new RelatorioService(_banco.Fabrica), estoque: estoque);
        await AteAsync(() => vm.PecasEmEstoque == "14" && !vm.Movimentacoes.Vazia, "carga");

        Assert.Equal("3", vm.ProdutosAtivos);
        Assert.Equal(Dinheiro.Formatar(10 * 2000 + 4 * 300), vm.ValorCusto);
        Assert.Equal(Dinheiro.Formatar(10 * 5000 + 4 * 1000), vm.ValorVenda);
        Assert.Equal(Dinheiro.Formatar(10 * 3000 + 4 * 700), vm.LucroPotencial);
        Assert.Equal("1", vm.QtdAbaixoMinimo);
        Assert.Equal("1", vm.QtdEsgotado);
        Assert.Equal(3, vm.PorCategoria.Count);   // Camisetas, Bonés e "Sem categoria" (o produto zerado)
        Assert.Equal(2, vm.PizzaCategorias.Count);
        Assert.Equal(2, vm.PizzaSobras.Count);
        Assert.NotEmpty(vm.PorTamanho);
        Assert.Equal("Camiseta", vm.MaioresValores[0].Rotulo);
        Assert.Single(vm.EstoqueBaixo);
        Assert.Equal(14, vm.Movimentacoes.TotalEntradas);
    }

    [Fact]
    public void Linha_vazia_sem_movimento_e_caminhos_dentro_da_area()
    {
        Assert.True(ResumoEstoqueViewModel.DesenharLinha(Array.Empty<PontoMovimentacao>()).Vazia);

        var d = new DateTime(2026, 5, 1);
        var linha = ResumoEstoqueViewModel.DesenharLinha(new[]
        {
            new PontoMovimentacao(d, "01/05", 10, 0), new PontoMovimentacao(d.AddDays(1), "02/05", 0, 5), new PontoMovimentacao(d.AddDays(2), "03/05", 5, 10),
        });
        Assert.False(linha.Vazia);
        Assert.StartsWith("M 0,0", linha.CaminhoEntradas);   // maior valor fica no topo (y = 0)
        Assert.Contains("L 300,0", linha.CaminhoSaidas);      // saída máxima no último ponto
        Assert.Equal("01/05", linha.PrimeiroRotulo);
        Assert.Equal("03/05", linha.UltimoRotulo);
        Assert.Equal(15, linha.TotalEntradas);
        Assert.Equal(15, linha.TotalSaidas);
    }
}
