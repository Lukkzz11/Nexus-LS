using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>O Caixa com o fechamento dentro: relatório do dia, botão Fechar de um clique e situação do caixa.</summary>
public sealed class CaixaComFechamentoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly CaixaService _caixa;
    private readonly VendaService _vendas;
    private int _produtoId;

    public CaixaComFechamentoTests()
    {
        _caixa = new CaixaService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private async Task<VendasViewModel> NovoCaixaAsync(Func<string, bool>? confirmar = null)
    {
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 50)).Id;
        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        var fechamento = new FechamentoViewModel(_caixa);
        if (confirmar is not null) fechamento.Confirmar = confirmar;
        var vm = new VendasViewModel(_vendas, cupons, _caixa, fechamento);
        await AteAsync(() => fechamento.Cabecalho != "", "carregar o caixa");
        return vm;
    }

    [Fact]
    public async Task Relatorio_do_dia_mostra_total_ticket_medio_e_formas_sem_somar_canceladas()
    {
        var vm = await NovoCaixaAsync();
        await _caixa.AbrirAsync(5000);
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Dinheiro, 2000));   // 20,00
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 2) }, 0, FormaPagamento.Pix));              // 40,00
        var cancelada = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 5) }, 0, FormaPagamento.Pix));
        await _vendas.CancelarAsync(cancelada.Id, "Cliente desistiu");

        await vm.CarregarCommand.ExecuteAsync(null);

        Assert.Equal("2", vm.QuantidadeVendasTexto);
        Assert.Equal("R$ 60,00", vm.TotalDiaTexto.Replace(' ', ' '));
        Assert.Equal("R$ 30,00", vm.TicketMedioTexto.Replace(' ', ' '));
        Assert.Contains("1 cancelada", vm.CanceladasTexto);
        Assert.Equal(2, vm.FormasDoDia.Count);
        Assert.Contains(vm.FormasDoDia, f => f.Total.Replace(' ', ' ') == "R$ 40,00" && f.Vendas == 1);
    }

    [Fact]
    public async Task Situacao_do_caixa_acompanha_abrir_e_fechar()
    {
        var vm = await NovoCaixaAsync(_ => true);
        Assert.Equal("Caixa fechado", vm.Fechamento!.SituacaoTexto);

        vm.Fechamento.TrocoInicialTexto = "50,00";
        await vm.Fechamento.AbrirCaixaCommand.ExecuteAsync(null);
        Assert.StartsWith("Caixa nº", vm.Fechamento.SituacaoTexto);

        await vm.Fechamento.FecharRapidoCommand.ExecuteAsync(null);
        Assert.Equal("Caixa fechado", vm.Fechamento.SituacaoTexto);
    }

    [Fact]
    public async Task Fechar_com_um_clique_mostra_o_resumo_e_fecha_sem_diferenca()
    {
        string? perguntado = null;
        var vm = await NovoCaixaAsync(texto => { perguntado = texto; return true; });
        vm.Fechamento!.TrocoInicialTexto = "50,00";
        await vm.Fechamento.AbrirCaixaCommand.ExecuteAsync(null);
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Dinheiro, 2000));
        await vm.Fechamento.AtualizarCommand.ExecuteAsync(null);

        await vm.Fechamento.FecharRapidoCommand.ExecuteAsync(null);

        Assert.NotNull(perguntado);
        Assert.Contains("R$ 70,00", perguntado!.Replace(' ', ' '));   // 50 de troco + 20 de venda
        Assert.Contains("Sem diferença", perguntado);
        Assert.False(vm.Fechamento.TurnoAberto);
        var historico = Assert.Single(vm.Fechamento.Historico);
        Assert.Equal("Sem diferença", historico.Diferenca);
    }

    [Fact]
    public async Task Fechar_e_recusar_na_confirmacao_mantem_o_caixa_aberto()
    {
        var vm = await NovoCaixaAsync(_ => false);
        await _caixa.AbrirAsync(5000);
        await vm.Fechamento!.AtualizarCommand.ExecuteAsync(null);

        await vm.Fechamento.FecharRapidoCommand.ExecuteAsync(null);

        Assert.True(vm.Fechamento.TurnoAberto);
        Assert.Empty(vm.Fechamento.Historico);
    }

    [Fact]
    public async Task Fechar_com_diferenca_exige_observacao_antes_de_confirmar()
    {
        var perguntou = false;
        var vm = await NovoCaixaAsync(_ => { perguntou = true; return true; });
        await _caixa.AbrirAsync(5000);
        await vm.Fechamento!.AtualizarCommand.ExecuteAsync(null);
        vm.Fechamento.ConferidoDinheiroTexto = "40,00";   // faltam 10,00

        await vm.Fechamento.FecharRapidoCommand.ExecuteAsync(null);

        Assert.False(perguntou);
        Assert.True(vm.Fechamento.MensagemEhErro);
        Assert.True(vm.Fechamento.TurnoAberto);

        vm.Fechamento.Observacao = "Faltou troco";
        await vm.Fechamento.FecharRapidoCommand.ExecuteAsync(null);

        Assert.True(perguntou);
        Assert.False(vm.Fechamento.TurnoAberto);
        Assert.Contains("Faltou", vm.Fechamento.Historico[0].Diferenca);
    }

    [Fact]
    public async Task Sem_caixa_aberto_fechar_avisa_e_nao_faz_nada()
    {
        var vm = await NovoCaixaAsync();

        await vm.Fechamento!.FecharRapidoCommand.ExecuteAsync(null);

        Assert.True(vm.Fechamento.MensagemEhErro);
        Assert.Empty(vm.Fechamento.Historico);
    }

    [Fact]
    public async Task Cancelar_uma_venda_atualiza_o_esperado_do_caixa()
    {
        var vm = await NovoCaixaAsync();
        await _caixa.AbrirAsync(5000);
        var venda = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, 1) }, 0, FormaPagamento.Dinheiro, 2000));
        await vm.Fechamento!.AtualizarCommand.ExecuteAsync(null);
        await vm.CarregarCommand.ExecuteAsync(null);
        Assert.Contains("R$ 70,00", vm.Fechamento.EsperadoDinheiro.Replace(' ', ' '));

        vm.Selecionada = vm.Vendas.Single(v => v.Id == venda.Id);
        await vm.CancelarVendaCommand.ExecuteAsync(null);

        Assert.Contains("Dinheiro esperado: R$ 50,00", vm.Fechamento.EsperadoDinheiro.Replace(' ', ' '));
    }
}
