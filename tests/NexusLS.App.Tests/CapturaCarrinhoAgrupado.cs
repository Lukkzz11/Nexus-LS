using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Desenha o carrinho do PDV com os tamanhos agrupados. Só roda com NEXUSLS_PRINTS apontando para uma pasta.</summary>
public sealed class CapturaCarrinhoAgrupado : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-print-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private static async Task AteAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 400 && !condicao(); i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Gera_o_carrinho_agrupado()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", null, 5990, 2000, 2, "1435", 3990), 0);
        await produtos.AdicionarGradeAsync(camiseta.Id, new[] { "P", "M", "G" });
        foreach (var t in new[] { "P", "M", "G" }) await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 6, null, t);
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", null, 2990, 800, 1, "2000"), 10);

        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        var cupons = new CupomService(_banco.Fabrica, configuracao, new ImpressoraDeMentira());
        var vm = new PdvViewModel(produtos, new VendaService(_banco.Fabrica),
            new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "r.json")), _banco.Fabrica),
            new ClienteService(_banco.Fabrica), cupons, TimeSpan.Zero, configuracao);

        async Task Bipar(string codigo)
        {
            Capturador.Rodar(() => { vm.Busca = codigo; vm.ProcessarTecla(ComandoTecla.Enter); });
            await AteAsync(() => vm.Etapa is EtapaPdv.Quantidade or EtapaPdv.Tamanho);
            if (vm.Etapa == EtapaPdv.Tamanho)
            {
                Capturador.Rodar(() => vm.ProcessarTecla(ComandoTecla.Enter));
                await AteAsync(() => vm.Etapa == EtapaPdv.Quantidade);
            }
            Capturador.Rodar(() => vm.ProcessarTecla(ComandoTecla.Enter));
            await AteAsync(() => vm.Etapa == EtapaPdv.Busca);
        }

        await Bipar("14352"); // M
        await Bipar("14351"); // P
        await Bipar("14351"); // P de novo
        await Bipar("2000");  // boné
        await Bipar("14353"); // G, junto da camiseta
        Capturador.Salvar(vm, Path.Combine(saida, "pdv-carrinho-agrupado-1400x820.png"));
    }
}
