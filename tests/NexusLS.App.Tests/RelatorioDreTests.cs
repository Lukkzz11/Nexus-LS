using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>A aba "Demonstrativo (DRE)" dos Relatórios mostra os números do motor, em linhas prontas.</summary>
public sealed class RelatorioDreTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private async Task<RelatoriosViewModel> AbrirAsync(bool comDre = true)
    {
        var vm = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica), null,
            comDre ? new DemonstrativoService(_banco.Fabrica) : null);
        await AteAsync(() => vm.TotalTexto != "", "carregar o relatório");
        return vm;
    }

    [Fact]
    public async Task Sem_o_servico_a_aba_do_DRE_fica_escondida()
    {
        var vm = await AbrirAsync(comDre: false);

        Assert.False(vm.TemDre);
        Assert.Empty(vm.Dre);
    }

    [Fact]
    public async Task Mostra_receita_custo_lucro_e_resultado_nas_linhas_do_DRE()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0), 10);
        await new VendaService(_banco.Fabrica).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 2) }, 0, FormaPagamento.Pix));

        var vm = await AbrirAsync();
        await AteAsync(() => vm.Dre.Count > 0, "montar o DRE");

        string Valor(string rotulo) => vm.Dre.First(l => l.Rotulo.StartsWith(rotulo)).Valor;
        Assert.Equal(Dinheiro.Formatar(4000), Valor("Receita bruta"));
        Assert.Equal(Dinheiro.Formatar(1600), Valor("(−) Custo das mercadorias"));
        Assert.Equal(Dinheiro.Formatar(2400), Valor("= Lucro bruto"));
        Assert.Equal(Dinheiro.Formatar(2400), Valor("= Resultado do período"));
        Assert.Contains(vm.Dre, l => l.Titulo && l.Rotulo.Contains("NÃO é receita nem despesa"));
        Assert.Contains("Impostos", vm.DreAvisos);
    }

    [Fact]
    public async Task Cartao_de_lucro_bruto_e_o_do_DRE_inclusive_com_abatimento_em_titulo()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0), 10);
        var maria = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Maria"));
        var venda = await new VendaService(_banco.Fabrica).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 2) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));   // 40,00
        await new TituloPrazoService(_banco.Fabrica).EditarAsync(venda.Id, 3000, null, "desconto combinado");           // -10,00

        var vm = await AbrirAsync();
        await AteAsync(() => vm.Dre.Count > 0, "montar o DRE");

        var lucroDre = vm.Dre.First(l => l.Rotulo.StartsWith("= Lucro bruto")).Valor;
        Assert.Equal(Dinheiro.Formatar(3000 - 1600), lucroDre);       // 30,00 - 16,00 de custo
        Assert.Equal(lucroDre, vm.LucroTexto);                        // o cartão do topo mostra o MESMO número
        Assert.Contains("abatimentos", vm.AvisoLucro);
    }

    [Fact]
    public async Task Resultado_negativo_e_marcado_em_vermelho_e_a_despesa_aparece_por_categoria()
    {
        await new FinanceiroService(_banco.Fabrica).CriarAsync(new DadosLancamento(
            TipoLancamento.Despesa, "Aluguel", 5000, DateTime.Today, "Aluguel", JaPago: true, Forma: FormaPagamento.Pix));

        var vm = await AbrirAsync();
        await AteAsync(() => vm.Dre.Count > 0, "montar o DRE");

        var resultado = vm.Dre.First(l => l.Rotulo.StartsWith("= Resultado"));
        Assert.True(resultado.Negativo);
        Assert.Contains(vm.Dre, l => l.Rotulo == "Aluguel" && l.Recuo);
    }
}
