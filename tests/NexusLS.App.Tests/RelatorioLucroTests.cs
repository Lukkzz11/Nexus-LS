using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>A tela de relatórios mostra lucro bruto, aviso de estimativa e o resumo por categoria.</summary>
public sealed class RelatorioLucroTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    [Fact]
    public async Task Mostra_lucro_bruto_e_categorias_sem_aviso_quando_o_custo_esta_guardado()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0, Categoria: "Acessórios"), 10);
        await new VendaService(_banco.Fabrica).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 2) }, 0, FormaPagamento.Pix));

        var vm = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.LucroTexto != "" && vm.PorCategoria.Count == 1, "carregar o relatório");

        Assert.Equal("R$ 24,00", vm.LucroTexto.Replace(' ', ' '));
        Assert.Equal("R$ 16,00", vm.CustoTexto.Replace(' ', ' '));
        Assert.Equal("", vm.AvisoLucro);
        Assert.Equal("Acessórios", vm.PorCategoria[0].Categoria);
    }

    [Fact]
    public async Task Avisa_que_o_lucro_e_estimado_quando_a_venda_e_antiga()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0), 10);
        await new VendaService(_banco.Fabrica).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix));
        using (var db = _banco.CriarContexto())
        {
            foreach (var item in db.ItensVenda) item.CustoUnitarioCentavos = null;
            db.SaveChanges();
        }

        var vm = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.LucroTexto != "", "carregar o relatório");

        Assert.Contains("estimado", vm.AvisoLucro);
    }
}
