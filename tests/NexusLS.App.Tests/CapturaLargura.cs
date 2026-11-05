using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha em PNG as telas que ainda não tinham captura (abas de Contas e saldos, Atividades, Fornecedores), para conferir
/// como elas ocupam um monitor largo. Só roda com NEXUSLS_PRINTS apontando para uma pasta (e só usa o banco de teste).
/// Use NEXUSLS_PRINTS_TAMANHO=1900x1000 para escolher o tamanho. Rode UM teste por vez (--filter).
/// </summary>
[Collection("OperadorAtual")]
public sealed class CapturaLargura : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);
    private readonly BancoDeTeste _banco = new();

    public CapturaLargura() => OperadorAtual.Nome = "Ana";

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static string? Pasta()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return null;
        Directory.CreateDirectory(saida);
        return saida;
    }

    private static async Task AteAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Gera_as_abas_de_contas_e_saldos()
    {
        var saida = Pasta();
        if (saida is null) return;

        var produto = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "789", 5990, 2000, 2), 80)).Id;
        var contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        var banco = await contas.CriarAsync(new DadosConta("Banco Itaú", TipoContaFin.Banco, 800000, new DateTime(2026, 10, 1), Padrao: true));
        var cofre = await contas.CriarAsync(new DadosConta("Cofre da loja", TipoContaFin.Caixa, 50000, new DateTime(2026, 10, 1)));
        for (var d = 3; d <= 15; d += 2)
        {
            var vendas = new VendaService(_banco.Fabrica, () => new DateTime(2026, 10, d, 10, 0, 0));
            await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produto, 2) }, 0, FormaPagamento.CartaoCredito));
            await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produto, 1) }, 0, FormaPagamento.CartaoDebito));
            await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produto, 3) }, 0, FormaPagamento.Cheque));
        }

        var vm = new ContasESaldosViewModel(new PosicaoFinanceiraService(_banco.Fabrica, () => Agora), contas,
            new LiquidacaoCartaoService(_banco.Fabrica, () => Agora), new ChequeService(_banco.Fabrica, () => Agora), () => Agora,
            new ChequeEmitidoService(_banco.Fabrica, () => Agora));
        await vm.Saldos.Carregamento;
        await vm.Contas.Carregamento;
        await vm.Cartoes.Carregamento;
        await vm.Cheques.Carregamento;
        await AteAsync(() => vm.Cartoes.Aliquidar.Count > 0 && vm.Cheques.Cheques.Count > 0);

        Capturador.Desenhar(() => new ContasESaldosView { DataContext = vm }, Path.Combine(saida, "contas-aba0-visao-geral.png"), 1500, 1100);
        vm.AbaAtual = 1;
        Capturador.Desenhar(() => new ContasESaldosView { DataContext = vm }, Path.Combine(saida, "contas-aba1-contas.png"), 1500, 1100);
        vm.AbaAtual = 2;
        Capturador.Desenhar(() => new ContasESaldosView { DataContext = vm }, Path.Combine(saida, "contas-aba2-cartoes.png"), 1500, 1100);
        vm.AbaAtual = 3;
        Capturador.Desenhar(() => new ContasESaldosView { DataContext = vm }, Path.Combine(saida, "contas-aba3-cheques.png"), 1500, 1100);
        vm.AbaAtual = 4;
        Capturador.Desenhar(() => new ContasESaldosView { DataContext = vm }, Path.Combine(saida, "contas-aba4-cheques-emitidos.png"), 1500, 1100);
        _ = cofre; _ = banco;
    }

    [Fact]
    public async Task Gera_atividades_e_fornecedores()
    {
        var saida = Pasta();
        if (saida is null) return;

        var produto = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 1), 30)).Id;
        var vendas = new VendaService(_banco.Fabrica, () => Agora);
        for (var i = 0; i < 6; i++)
            await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produto, 1) }, 0, FormaPagamento.Pix));
        var fornecedores = new FornecedorService(_banco.Fabrica, () => Agora);
        await fornecedores.CadastrarAsync(new DadosFornecedor("Malharia Sol", "12.345.678/0001-90", "(81) 99999-1234", "contato@malhariasol.com.br"));
        await fornecedores.CadastrarAsync(new DadosFornecedor("Armarinho Central", null, "(81) 3333-4444"));
        await fornecedores.CadastrarAsync(new DadosFornecedor("Bordados Ltda", "98.765.432/0001-10", null, "vendas@bordados.com"));

        var atv = new AtividadesViewModel(new AtividadeService(_banco.Fabrica), Agora.Date);
        await atv.Carregamento;
        Capturador.Desenhar(() => new AtividadesView { DataContext = atv }, Path.Combine(saida, "atividades.png"), 1400, 820);

        var forn = new FornecedoresViewModel(fornecedores);
        await forn.Carregamento;
        Capturador.Desenhar(() => new FornecedoresView { DataContext = forn }, Path.Combine(saida, "fornecedores.png"), 1400, 820);
    }
}
