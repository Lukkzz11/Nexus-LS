using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>PDV: "perguntar o tamanho" desligado (modo rápido) e produto sem baixa de estoque.</summary>
public sealed class ModoRapidoPdvTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-rapido-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly ConfiguracaoService _configuracao;
    private readonly PdvViewModel _vm;

    public ModoRapidoPdvTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _configuracao = new ConfiguracaoService(_banco.Fabrica);
        var cupons = new CupomService(_banco.Fabrica, _configuracao, new ImpressoraDeMentira());
        var rascunho = new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "rascunho.json")), _banco.Fabrica);
        _vm = new PdvViewModel(_produtos, new VendaService(_banco.Fabrica), rascunho, new ClienteService(_banco.Fabrica),
            cupons, TimeSpan.Zero, _configuracao);
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

    private async Task BuscarAsync(string texto, EtapaPdv esperada)
    {
        _vm.Busca = texto;
        _vm.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == esperada, $"etapa {esperada}");
    }

    [Fact]
    public async Task Por_padrao_pergunta_o_tamanho_mesmo_com_uma_unica_opcao()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 10);

        await BuscarAsync("Boné", EtapaPdv.Tamanho);

        Assert.Equal(EtapaPdv.Tamanho, _vm.Etapa);
    }

    [Fact]
    public async Task Modo_rapido_vai_direto_para_a_quantidade_quando_so_ha_uma_opcao()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 10);
        await _configuracao.SalvarAsync(new DadosLoja("Loja", PerguntarTamanho: false));

        await BuscarAsync("Boné", EtapaPdv.Quantidade);

        Assert.Equal("Sem tamanho", _vm.TamanhoModalTexto);
        _vm.ProcessarTecla(ComandoTecla.Enter);
        Assert.Single(_vm.Itens);
    }

    [Fact]
    public async Task Modo_rapido_continua_perguntando_quando_ha_varios_tamanhos()
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto("Camiseta", "789", 5000, 2000, 1), 0);
        foreach (var t in new[] { "P", "M" })
        {
            await _produtos.AdicionarTamanhoAsync(p.Id, t);
            await new EstoqueService(_banco.Fabrica).AjustarAsync(p.Id, TipoMovimentacao.Entrada, 5, null, t);
        }
        await _configuracao.SalvarAsync(new DadosLoja("Loja", PerguntarTamanho: false));

        await BuscarAsync("Camiseta", EtapaPdv.Tamanho);

        Assert.Equal(2, _vm.OpcoesTamanho.Count);
    }

    [Fact]
    public async Task Modo_rapido_nao_pula_produto_esgotado()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 0);
        await _configuracao.SalvarAsync(new DadosLoja("Loja", PerguntarTamanho: false));

        await BuscarAsync("Boné", EtapaPdv.Tamanho);

        Assert.Equal(EtapaPdv.Tamanho, _vm.Etapa);   // fica na janela, que mostra "esgotado"
    }

    [Fact]
    public async Task Produto_sem_baixa_aparece_sem_controle_e_vende_mesmo_com_estoque_zero()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Conserto", null, 5000, 0, 0, VenderSemBaixa: true), 0);

        await BuscarAsync("Conserto", EtapaPdv.Tamanho);
        Assert.Equal("sem controle de estoque", _vm.OpcoesTamanho[0].Detalhe);

        _vm.ProcessarTecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Quantidade, _vm.Etapa);
        Assert.Equal("Sem controle de estoque", _vm.QuantidadeMaximoTexto);
        for (var i = 0; i < 4; i++) _vm.ProcessarTecla(ComandoTecla.Cima);   // quantidade 5, sem estoque nenhum
        _vm.ProcessarTecla(ComandoTecla.Enter);

        var linha = Assert.Single(_vm.Itens);
        Assert.Equal(5, linha.Quantidade);
    }
}
