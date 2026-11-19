using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha as telas novas da Gestão em PNG para conferir o visual. Só roda com NEXUSLS_PRINTS apontando para uma pasta
/// (e só usa o banco de teste, nunca o real). NEXUSLS_PRINTS_TAMANHO=1900x1000 força o tamanho.
/// </summary>
[Collection("Sessao")]
public sealed class CapturaGestaoTelas : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-print-gestao-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private static string? Saida()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return null;
        Directory.CreateDirectory(saida);
        return saida;
    }

    private async Task SemearAsync(LancamentoEmpresaService lancamentos, AnexoService anexos)
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "7891234567895", 5990, 2000, 2, "CAM-100", 3990), 0);
        foreach (var t in new[] { "P", "M", "G" })
        {
            await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
            await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 20, null, t);
        }
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "7890000000555", 2990, 800, 1, "BON-020"), 30);
        var jeans = await produtos.CadastrarAsync(new DadosProduto("Calça Jeans Skinny", null, 12990, 5000, 1, "JEA-010"), 12);
        await produtos.CadastrarAsync(new DadosProduto("Camisa Polo Piquet", null, 8990, 3000, 1, "POL-300"), 5);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 2, "M"), new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 3, "G"), new ItemCarrinho(jeans.Id, 1) }, 0, FormaPagamento.CartaoCredito));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 4) }, 0, FormaPagamento.Dinheiro, 20000));

        var hoje = DateTime.Today;
        var luz = await lancamentos.CriarAsync(new DadosLancamentoEmpresa(hoje.AddDays(-1), TipoMovimentoEmpresa.Saida, "Energia", "Conta de luz de outubro", 38090, FormaPagamento.Pix));
        await lancamentos.CriarAsync(new DadosLancamentoEmpresa(hoje.AddDays(-2), TipoMovimentoEmpresa.Saida, "Aluguel", "Aluguel da loja", 250000, FormaPagamento.Pix));
        await lancamentos.CriarAsync(new DadosLancamentoEmpresa(hoje, TipoMovimentoEmpresa.Entrada, "Aporte do dono", "Reforço de caixa", 100000));
        Directory.CreateDirectory(_pasta);
        var arquivo = Path.Combine(_pasta, "nota-energia.png");
        File.WriteAllBytes(arquivo, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 });
        await anexos.AdicionarAsync(luz.Id, arquivo);
    }

    private (AnexoService anexos, LancamentoEmpresaService lancamentos) Servicos()
    {
        var anexos = new AnexoService(_banco.Fabrica, Path.Combine(_pasta, "anexos"));
        return (anexos, new LancamentoEmpresaService(_banco.Fabrica, anexos));
    }

    [Fact]
    public async Task Gera_os_relatorios_financeiro_e_de_produtos()
    {
        var saida = Saida();
        if (saida is null) return;
        var (anexos, lancamentos) = Servicos();
        await SemearAsync(lancamentos, anexos);

        RelatorioFinanceiroViewModel? fin = null;
        RelatorioProdutosViewModel? prod = null;
        Capturador.Rodar(() =>
        {
            fin = new RelatorioFinanceiroViewModel(new RelatorioFinanceiroService(_banco.Fabrica));
            prod = new RelatorioProdutosViewModel(new RelatorioProdutosService(_banco.Fabrica));
        });
        await fin!.Carregamento;
        await prod!.Carregamento;

        Capturador.Desenhar(() => new RelatorioFinanceiroView { DataContext = fin }, Path.Combine(saida, "gestao-relatorio-financeiro.png"), 1400, 820);
        Capturador.Rodar(() => fin!.UltimosSeteDiasCommand.Execute(null));
        await fin!.Carregamento;
        Capturador.Desenhar(() => new RelatorioProdutosView { DataContext = prod }, Path.Combine(saida, "gestao-relatorio-produtos.png"), 1400, 820);
    }

    [Fact]
    public async Task Gera_entradas_e_saidas_com_anexo_e_a_gestao_da_empresa()
    {
        var saida = Saida();
        if (saida is null) return;
        var (anexos, lancamentos) = Servicos();
        await SemearAsync(lancamentos, anexos);

        EntradasESaidasViewModel? es = null;
        GestaoEmpresaViewModel? empresa = null;
        Capturador.Rodar(() =>
        {
            es = new EntradasESaidasViewModel(lancamentos, anexos);
            var lojas = new LojasViewModel(new RelatorioFinanceiroService(_banco.Fabrica),
                () => new ConfiguracaoSeguranca { LojaNome = "Loja Centro", LojaId = "3f9c1a7e2b4d4c8e9a10b5d6e7f80123" });
            empresa = new GestaoEmpresaViewModel(lojas, new EntradasESaidasViewModel(lancamentos, anexos));
        });
        await es!.Carregamento;
        await empresa!.Lojas.Carregamento;
        await empresa.EntradasESaidas!.Carregamento;

        // lista com a conta de luz escolhida: formulário preenchido e o anexo dela
        var luz = es.Linhas.First(l => l.Descricao.StartsWith("Conta de luz"));
        await Capturador.RodarAsync(() => es.AbrirLancamentoAsync(luz.Id));
        Capturador.Desenhar(() => new EntradasESaidasView { DataContext = es }, Path.Combine(saida, "gestao-entradas-e-saidas.png"), 1400, 940);

        // Gestão da empresa: aba Lojas com o passo a passo aberto e depois a aba de lançamentos
        Capturador.Rodar(() => empresa!.Lojas.AdicionarLojaCommand.Execute(null));
        Capturador.Desenhar(() => new GestaoEmpresaView { DataContext = empresa }, Path.Combine(saida, "gestao-empresa-lojas.png"), 1400, 1000);
        Capturador.Rodar(() => empresa!.AbaSelecionada = 1);
        Capturador.Desenhar(() => new GestaoEmpresaView { DataContext = empresa }, Path.Combine(saida, "gestao-empresa-lancamentos.png"), 1400, 820);
    }
}
