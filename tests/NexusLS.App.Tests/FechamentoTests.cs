using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>A tela de fechamento de caixa: abrir, movimentar, conferir e refazer a conferência.</summary>
public sealed class FechamentoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly CaixaService _caixa;
    private readonly VendaService _vendas;
    private int _produtoId;

    public FechamentoTests()
    {
        _caixa = new CaixaService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync() =>
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 50)).Id;

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private async Task<FechamentoViewModel> NovaTelaAsync()
    {
        var vm = new FechamentoViewModel(_caixa);
        await AteAsync(() => vm.Cabecalho != "", "carregar a tela");
        return vm;
    }

    [Fact]
    public async Task Fluxo_completo_abre_movimenta_e_fecha_sem_diferenca()
    {
        await PrepararAsync();
        var vm = await NovaTelaAsync();
        Assert.True(vm.PodeAbrir);
        Assert.False(vm.PodeConferir);

        vm.TrocoInicialTexto = "50,00";
        await vm.AbrirCaixaCommand.ExecuteAsync(null);
        Assert.True(vm.TurnoAberto);
        Assert.True(vm.PodeConferir);

        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Dinheiro, 5000));
        vm.MovimentoSelecionado = vm.OpcoesMovimento.Single(o => o.Tipo == TipoMovimentoCaixa.Despesa);
        vm.MovimentoValorTexto = "5,00";
        vm.MovimentoDescricao = "Lanche";
        await vm.RegistrarMovimentoCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Single(vm.Lancamentos);
        Assert.Contains("Dinheiro esperado: R$ 65,00", vm.EsperadoDinheiro.Replace(' ', ' '));

        vm.ConferidoDinheiroTexto = "65,00";
        Assert.Contains("confere", vm.TextoDiferenca);
        await vm.FecharCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.False(vm.TurnoAberto);
        Assert.Single(vm.Historico);
        Assert.Equal("Sem diferença", vm.Historico[0].Diferenca);
    }

    [Fact]
    public async Task Diferenca_sem_observacao_mostra_erro_e_mantem_o_caixa_aberto()
    {
        await PrepararAsync();
        var vm = await NovaTelaAsync();
        vm.TrocoInicialTexto = "10,00";
        await vm.AbrirCaixaCommand.ExecuteAsync(null);

        vm.ConferidoDinheiroTexto = "8,00";
        Assert.Contains("falta R$ 2,00", vm.TextoDiferenca.Replace(' ', ' '));
        await vm.FecharCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.True(vm.TurnoAberto);

        vm.Observacao = "Troco errado";
        await vm.FecharCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("Faltou", vm.Historico[0].Diferenca);
    }

    [Fact]
    public async Task Valor_invalido_mostra_erro_em_vez_de_fechar_o_programa()
    {
        await PrepararAsync();
        var vm = await NovaTelaAsync();
        vm.TrocoInicialTexto = "abc";
        await vm.AbrirCaixaCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.False(vm.TurnoAberto);
    }

    [Fact]
    public async Task Conferir_de_novo_um_fechamento_antigo_gera_a_versao_2()
    {
        await PrepararAsync();
        var vm = await NovaTelaAsync();
        await vm.AbrirCaixaCommand.ExecuteAsync(null);
        vm.ConferidoDinheiroTexto = "0";
        await vm.FecharCommand.ExecuteAsync(null);
        Assert.Single(vm.Historico);

        await vm.ConferirDeNovoCommand.ExecuteAsync(vm.Historico[0]);
        Assert.True(vm.Refazendo);
        await vm.FecharCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro); // falta explicar o motivo

        vm.Observacao = "Revisão do dia";
        await vm.FecharCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Equal(2, vm.Historico.Count);
        Assert.Contains(vm.Historico, h => h.Versao == 2);
    }

    [Fact]
    public async Task Cancelar_venda_de_caixa_fechado_avisa_na_pergunta()
    {
        await PrepararAsync();
        var vm = await NovaTelaAsync();
        await vm.AbrirCaixaCommand.ExecuteAsync(null);
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Pix));
        vm.ConferidoPixTexto = "20,00";
        await vm.FecharCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);

        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        var tela = new VendasViewModel(_vendas, cupons, _caixa);
        await AteAsync(() => tela.Vendas.Count == 1, "lista do dia");
        tela.Selecionada = tela.Vendas[0];
        string? pergunta = null;
        tela.PedirCancelamento = p => { pergunta = p; return null; };

        await tela.CancelarVendaCommand.ExecuteAsync(null);

        Assert.Contains("já foi fechado", pergunta);
    }

    [Fact]
    public async Task Gera_a_tela_de_fechamento()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        await PrepararAsync();
        await _caixa.AbrirAsync(5000);
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 2) }, 0, FormaPagamento.Dinheiro, 10000));
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Pix));
        await _caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 3000, "Levar ao banco");

        FechamentoViewModel? vm = null;
        Capturador.Rodar(() => vm = new FechamentoViewModel(_caixa));
        await AteAsync(() => vm!.Cabecalho != "", "carregar");
        Capturador.Rodar(() => vm!.ConferidoDinheiroTexto = "90,00");
        await Task.Delay(100);

        Capturador.Desenhar(() => new FechamentoView { DataContext = vm }, Path.Combine(saida, "16-fechamento.png"), 1400, 900);
    }
}
