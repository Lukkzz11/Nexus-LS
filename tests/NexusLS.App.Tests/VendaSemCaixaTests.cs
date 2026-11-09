using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Venda com o caixa fechado: quando a tela tem o serviço de caixa (programa de verdade), a venda é barrada
/// com mensagem clara. Sem isso, a venda ficaria fora de qualquer turno e não entraria no fechamento.
/// </summary>
public sealed class VendaSemCaixaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-semcaixa-" + Guid.NewGuid().ToString("N"));
    private readonly PdvViewModel _vm;
    private readonly CaixaService _caixa;

    public VendaSemCaixaTests()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        var clientes = new ClienteService(_banco.Fabrica);
        var cupons = new CupomService(_banco.Fabrica, configuracao, new ImpressoraDeMentira());
        var rascunho = new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "rascunho.json")), _banco.Fabrica);
        _caixa = new CaixaService(_banco.Fabrica);
        _vm = new PdvViewModel(produtos, new VendaService(_banco.Fabrica), rascunho, clientes, cupons,
            TimeSpan.Zero, configuracao, caixa: _caixa);
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), $"Tempo esgotado esperando: {quando}. Etapa={_vm.Etapa}, mensagem=\"{_vm.Mensagem}\"");
    }

    private async Task ColocarBoneNoCarrinhoAsync()
    {
        await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 10);

        _vm.Busca = "Boné";
        _vm.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Tamanho, "tamanho");

        // Tamanho único -> quantidade -> volta para a busca.
        _vm.ProcessarTecla(ComandoTecla.Enter);
        _vm.ProcessarTecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.Single(_vm.Itens);
    }

    [Fact]
    public async Task Com_caixa_fechado_a_venda_nao_e_gravada_e_a_tela_avisa()
    {
        await ColocarBoneNoCarrinhoAsync();

        // Busca vazia + Enter = desconto; Enter = pagamento; Enter = tentativa de finalizar.
        _vm.ProcessarTecla(ComandoTecla.Enter);
        _vm.ProcessarTecla(ComandoTecla.Enter);
        _vm.ProcessarTecla(ComandoTecla.Enter);

        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
        Assert.Contains("Abra o caixa", _vm.Mensagem);

        using var db = _banco.CriarContexto();
        Assert.Empty(db.Vendas);
        Assert.False(_vm.CaixaAberto);
    }

    [Fact]
    public async Task Depois_de_abrir_o_caixa_a_mesma_venda_finaliza()
    {
        await ColocarBoneNoCarrinhoAsync();
        await _caixa.AbrirAsync(10000);
        await _vm.AtualizarCaixaAsync();

        _vm.ProcessarTecla(ComandoTecla.Enter); // desconto
        _vm.ProcessarTecla(ComandoTecla.Enter); // pagamento
        _vm.ProcessarTecla(ComandoTecla.Enter); // finalizar
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");

        using var db = _banco.CriarContexto();
        Assert.Single(db.Vendas);
    }
}
