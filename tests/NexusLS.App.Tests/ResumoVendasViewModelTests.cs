using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("Sessao")]
public sealed class ResumoVendasViewModelTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 10, 28);
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var a = await produtos.CadastrarAsync(new DadosProduto("Camiseta Azul", "7890000000017", 5000, 2000, 1, "CM-100"), 20);
        var b = await produtos.CadastrarAsync(new DadosProduto("Boné Preto", null, 3000, 1000, 1, "BN-200"), 20);
        await using var db = _banco.CriarContexto();
        Venda Nova(int dias, int hora, long total, FormaPagamento f, string op, ItemVenda item, bool cancelada = false) => new()
        {
            DataHora = Hoje.AddDays(-dias).AddHours(hora), TotalCentavos = total, SubtotalCentavos = total, FormaPagamento = f, Operador = op,
            Cancelada = cancelada, Itens = { item }
        };
        ItemVenda It(Produto p, string tam, int q, long total) => new()
        {
            ProdutoId = p.Id, NomeProduto = p.Nome, Tamanho = tam, Quantidade = q, PrecoUnitarioCentavos = total / q, TotalCentavos = total, CustoUnitarioCentavos = 1500
        };
        db.Vendas.AddRange(
            Nova(0, 10, 10000, FormaPagamento.Pix, "Ana", It(a, "M", 2, 10000)),
            Nova(0, 15, 5000, FormaPagamento.CartaoCredito, "Bia", It(a, "G", 1, 5000)),
            Nova(1, 11, 6000, FormaPagamento.Dinheiro, "Ana", It(b, "U", 2, 6000)),
            Nova(2, 16, 3000, FormaPagamento.Pix, "Ana", It(b, "U", 1, 3000), cancelada: true),
            Nova(3, 10, 4000, FormaPagamento.Pix, "Ana", It(a, "M", 1, 4000)),
            Nova(35, 10, 5000, FormaPagamento.Pix, "Ana", It(a, "M", 1, 5000)));
        await db.SaveChangesAsync();
    }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 500 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    [Fact]
    public async Task Resumo_carrega_e_pesquisa_mostra_unidades_variacoes_e_vendas()
    {
        await PrepararAsync();
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje, new ResumoVendasService(_banco.Fabrica));
        await Esperar(() => vm.ResumoIndicadores.Count == 6);

        Assert.Equal("Total vendido", vm.ResumoIndicadores[0].Titulo);
        Assert.Equal("R$ 250,00", vm.ResumoIndicadores[0].Valor);
        Assert.Contains("1 venda(s) cancelada(s)", vm.ResumoCancelamentos);
        Assert.NotEmpty(vm.ResumoFormas);
        Assert.NotEmpty(vm.ResumoVendedores);

        vm.PesquisaReferencia = "cm-100";
        await Esperar(() => vm.TemBusca && vm.BuscaVendas.Count == 3);

        Assert.StartsWith("4 unidade(s) vendida(s)", vm.BuscaTotais);
        Assert.Equal(2, vm.BuscaVariacoes.Count);
        Assert.Equal("Tamanho M", vm.BuscaVariacoes[0].Nome);

        vm.PesquisaReferencia = "zzz";
        await Esperar(() => !vm.TemBusca && vm.BuscaAviso.Length > 0);
        Assert.Empty(vm.BuscaVendas);
    }

    [Fact]
    public async Task Exportar_resumo_grava_o_arquivo()
    {
        await PrepararAsync();
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje, new ResumoVendasService(_banco.Fabrica));
        await Esperar(() => vm.ResumoIndicadores.Count == 6);
        var arquivo = Path.Combine(Path.GetTempPath(), "resumo_vm_" + Guid.NewGuid().ToString("N") + ".csv");
        vm.EscolherArquivoParaSalvar = _ => arquivo;
        try
        {
            await vm.ExportarResumoCommand.ExecuteAsync(null);

            Assert.False(vm.MensagemEhErro, vm.Mensagem);
            var bytes = File.ReadAllBytes(arquivo);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }
        finally { File.Delete(arquivo); }
    }

    [Fact]
    public async Task Captura_do_desempenho_com_resumo_e_pesquisa()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);
        await PrepararAsync();
        DesempenhoVendasViewModel? vm = null;
        Capturador.Rodar(() => vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje, new ResumoVendasService(_banco.Fabrica)));
        await Esperar(() => vm!.ResumoIndicadores.Count == 6);
        Capturador.Rodar(() => vm!.PesquisaReferencia = "camiseta");
        await Esperar(() => vm!.BuscaVendas.Count == 3);
        // a janela de captura é limitada à altura da tela: esconde o começo da página para ver o resumo e a pesquisa
        foreach (var (nome, esconder) in new[] { ("topo", 0), ("fim", 5), ("busca", 6) })
            Capturador.Desenhar(() => new NexusLS.App.Views.DesempenhoVendasView { DataContext = vm }, Path.Combine(saida, $"desempenho-resumo-{nome}.png"), 1400, 1000,
                t =>
                {
                    var rolagem = (System.Windows.Controls.ScrollViewer)((System.Windows.Controls.Grid)((System.Windows.Controls.UserControl)t).Content).Children[2];
                    var pilha = (System.Windows.Controls.StackPanel)rolagem.Content;
                    for (var i = 0; i < esconder; i++) pilha.Children[i].Visibility = System.Windows.Visibility.Collapsed;
                });
    }
}
