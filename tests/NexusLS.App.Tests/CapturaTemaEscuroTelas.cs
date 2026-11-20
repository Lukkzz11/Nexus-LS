using System.IO;
using NexusLS.App.Aparencia;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Desenha Financeiro, Clientes e Relatório financeiro nos dois temas, para conferir a legibilidade. Só roda com NEXUSLS_PRINTS.</summary>
public sealed class CapturaTemaEscuroTelas : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose()
    {
        Capturador.Rodar(() => GerenciadorDeTema.Aplicar(false));
        _banco.Dispose();
    }

    [Fact]
    public async Task Gera_financeiro_clientes_e_relatorio_nos_dois_temas()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var agora = DateTime.Now;
        var produtos = new ProdutoService(_banco.Fabrica);
        var clientes = new ClienteService(_banco.Fabrica);
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 3), 20)).Id;
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria Souza"));
        await clientes.CadastrarAsync(new DadosCliente("Marcos Lima"));
        var vendas = new VendaService(_banco.Fabrica);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 5) }, 0, FormaPagamento.Pix, ClienteId: maria.Id));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 2) }, 500, FormaPagamento.APrazo, ClienteId: maria.Id));

        var fin = new FinanceiroService(_banco.Fabrica, () => agora);
        var hoje = agora.Date;
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel da loja", 150000, new DateTime(hoje.Year, hoje.Month, 5), "Aluguel", "Imobiliária Central", JaPago: true, Forma: FormaPagamento.Pix));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Conta de luz", 38000, hoje.AddDays(-2), "Energia", "Neoenergia"));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Conserto de máquina", 25000, hoje.AddDays(3), "Serviços"));

        FinanceiroViewModel? fvm = null;
        ClientesViewModel? cvm = null;
        RelatorioFinanceiroViewModel? rvm = null;
        Capturador.Rodar(() =>
        {
            fvm = new FinanceiroViewModel(fin, () => agora);
            cvm = new ClientesViewModel(clientes);
            rvm = new RelatorioFinanceiroViewModel(new RelatorioFinanceiroService(_banco.Fabrica));
        });
        await fvm!.Carregamento;
        await rvm!.Carregamento;
        for (var i = 0; i < 300 && cvm!.Clientes.Count < 2; i++) await Task.Delay(10);
        Capturador.Rodar(() => cvm!.Selecionado = cvm.Clientes.First(c => c.Nome.StartsWith("Maria")));
        for (var i = 0; i < 300 && cvm!.Compras.Count < 2; i++) await Task.Delay(10);
        Capturador.Rodar(() => fvm!.Selecionada = fvm.Linhas.First(l => l.Atrasado));

        foreach (var (nome, escuro) in new[] { ("claro", false), ("escuro", true) })
        {
            Capturador.Rodar(() => GerenciadorDeTema.Aplicar(escuro));
            Capturador.SalvarFinanceiro(fvm!, Path.Combine(saida, $"tema-{nome}-financeiro.png"), 1400, 820);
            Capturador.Desenhar(() => new ClientesView { DataContext = cvm }, Path.Combine(saida, $"tema-{nome}-clientes.png"), 1400, 820);
            Capturador.Desenhar(() => new RelatorioFinanceiroView { DataContext = rvm }, Path.Combine(saida, $"tema-{nome}-relatorio-financeiro.png"), 1400, 820);
        }
    }
}
