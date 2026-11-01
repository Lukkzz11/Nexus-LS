using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class DesempenhoVendasViewModelTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 10, 31);
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private async Task PrepararVendasAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 10000, 4000, 1), 50);
        await produtos.CadastrarAsync(new DadosProduto("Encalhado", null, 5000, 2000, 1), 3);
        await using var db = _banco.CriarContexto();
        Venda Nova(int diasAtras, long total, FormaPagamento forma = FormaPagamento.Pix) => new()
        {
            DataHora = Hoje.AddDays(-diasAtras).AddHours(9), TotalCentavos = total, SubtotalCentavos = total, FormaPagamento = forma, Operador = "Ana",
            Itens = { new ItemVenda { ProdutoId = p.Id, NomeProduto = "Boné", Quantidade = 1, PrecoUnitarioCentavos = total, TotalCentavos = total, CustoUnitarioCentavos = 4000 } }
        };
        db.Vendas.AddRange(Nova(0, 10000), Nova(1, 20000, FormaPagamento.CartaoCredito), Nova(3, 8000), Nova(40, 5000));
        await db.SaveChangesAsync();
    }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    [Fact]
    public async Task Abre_nos_ultimos_30_dias_com_indicadores_comparacao_e_rankings()
    {
        await PrepararVendasAsync();

        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje);
        await Esperar(() => vm.Indicadores.Count == 5 && vm.MaisVendidosLista.Count > 0);

        Assert.StartsWith("02/10/2026 a 31/10/2026 (30 dia(s))", vm.TextoPeriodo);
        var fat = vm.Indicadores[0];
        Assert.Equal("Faturamento", fat.Titulo);
        Assert.Equal("R$ 380,00", fat.Valor);               // 100 + 200 + 80 (a venda de 40 dias atrás fica fora do período)
        Assert.Equal("Vendas", vm.Indicadores[1].Titulo);
        Assert.Equal("3", vm.Indicadores[1].Valor);
        Assert.StartsWith("R$ 260,00", vm.Indicadores[4].Valor); // lucro: 380,00 − 3 peças × custo de 40,00
        Assert.Equal("Encalhado", Assert.Single(vm.SemVendasLista).Nome);
        Assert.Equal("Boné", vm.MaisVendidosLista[0].Nome);
        Assert.True(vm.TemVendas);
        Assert.Equal(30, vm.VendasNoTempo.Count);
    }

    [Fact]
    public async Task Atalho_de_periodo_troca_as_datas_e_recarrega_so_uma_vez()
    {
        await PrepararVendasAsync();
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje);
        await Esperar(() => vm.Indicadores.Count == 5);

        vm.HojeCommand.Execute(null);
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 100,00");

        Assert.Equal(Hoje, vm.De);
        Assert.Equal(Hoje, vm.Ate);
        Assert.Single(vm.VendasNoTempo);
        Assert.Contains("sobre o período anterior", vm.Indicadores[0].Variacao); // ontem teve R$ 200: caiu
        Assert.True(vm.Indicadores[0].Caiu);
    }

    [Fact]
    public async Task Sem_vendas_mostra_aviso_e_nao_inventa_lucro()
    {
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje);
        await Esperar(() => vm.Indicadores.Count == 5);

        Assert.False(vm.TemVendas);
        Assert.Equal("—", vm.Indicadores[4].Valor);
        Assert.Equal("sem base de comparação", vm.Indicadores[0].Variacao);
    }

    [Fact]
    public async Task A_tela_abre_com_os_estilos_do_programa_e_desenha_os_graficos()
    {
        await PrepararVendasAsync();
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje);
        await Esperar(() => vm.Indicadores.Count == 5 && vm.PorOperadorBarras.Count > 0);
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") ?? Path.GetTempPath();
        Directory.CreateDirectory(pasta);
        var png = Path.Combine(pasta, "desempenho-vendas.png");

        Capturador.SalvarDesempenho(vm, png);

        Assert.True(new FileInfo(png).Length > 5000);
        if (Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") is null) File.Delete(png);
    }
}
