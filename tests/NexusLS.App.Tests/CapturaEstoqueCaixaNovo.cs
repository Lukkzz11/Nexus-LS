using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha as telas novas (Estoque com Sobras, Mais vendidos com pizzas, Fechamento com o relatório por período) em PNG
/// e grava o CSV do estoque e o PDF do caixa de demonstração. Só roda com NEXUSLS_PRINTS apontando para uma pasta
/// (e só usa o banco de teste, nunca o banco real). Rode UM teste por vez (--filter).
/// </summary>
public sealed class CapturaEstoqueCaixaNovo : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private static string? Saida()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return null;
        Directory.CreateDirectory(saida);
        return saida;
    }

    private static async Task AteAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 400 && !condicao(); i++) await Task.Delay(10);
    }

    private async Task<List<int>> CadastrarLojaAsync()
    {
        var p = new ProdutoService(_banco.Fabrica);
        var ids = new List<int>();
        async Task<int> Novo(string nome, string cat, long preco, long custo, int min, int est, bool sobra = false, string? tam = null)
        {
            var prod = await p.CadastrarAsync(new DadosProduto(nome, null, preco, custo, min, "R-" + (ids.Count + 1), preco - 1000, cat, Sobra: sobra), est);
            if (tam is not null) foreach (var t in tam.Split(',')) await p.AdicionarTamanhoAsync(prod.Id, t);
            ids.Add(prod.Id);
            return prod.Id;
        }
        await Novo("Camiseta Básica Algodão", "Camisetas", 5990, 2000, 5, 30);
        await Novo("Camiseta Estampada Verão", "Camisetas", 6990, 2400, 5, 18);
        await Novo("Boné Aba Reta", "Acessórios", 2990, 800, 3, 25);
        await Novo("Cinto Couro Legítimo", "Acessórios", 7990, 2500, 2, 30);
        await Novo("Calça Jeans Slim", "Calças", 14990, 5000, 4, 12);
        await Novo("Vestido Midi Floral", "Vestidos", 12990, 4200, 3, 9);
        await Novo("Bermuda Sarja", "Bermudas", 8990, 3000, 3, 14);
        await Novo("Meia Esportiva (par)", "Acessórios", 1500, 400, 6, 40);
        await Novo("Jaqueta Corta-Vento (sobra de coleção)", "Jaquetas", 17990, 6000, 0, 3, sobra: true);
        await Novo("Blusa Tricô Inverno (sobra)", "Blusas", 9990, 3200, 0, 5, sobra: true);
        await Novo("Saia Plissada (sobra)", "Saias", 7990, 2600, 0, 4, sobra: true);
        return ids;
    }

    // ---------------- Estoque da loja ----------------

    [Fact]
    public async Task Gera_o_estoque_com_sobras()
    {
        var saida = Saida();
        if (saida is null) return;
        await CadastrarLojaAsync();

        var vm = new EstoqueLojaViewModel(new ProdutoService(_banco.Fabrica), new EstoqueService(_banco.Fabrica));
        await AteAsync(() => vm.Produtos.Count == 11);

        Capturador.SalvarEstoque(vm, Path.Combine(saida, "estoque-todos.png"), 0);

        Capturador.Rodar(() => vm.VisaoSobras = true);
        await AteAsync(() => vm.Produtos.Count == 3);
        Capturador.Rodar(() => vm.Selecionado = vm.Produtos[0]);
        await AteAsync(() => vm.IdEditando is not null);
        Capturador.SalvarEstoque(vm, Path.Combine(saida, "estoque-sobras.png"), 0);

        Capturador.Rodar(() => vm.AdicionarSobraCommand.Execute(null));
        Capturador.SalvarEstoque(vm, Path.Combine(saida, "estoque-adicionar-sobra.png"), 0);

        // sobras vazio (nenhuma marcada)
        Capturador.Rodar(() => vm.VisaoEstoqueNormal = true);
        await AteAsync(() => vm.Produtos.Count == 8);
        Capturador.SalvarEstoque(vm, Path.Combine(saida, "estoque-so-estoque.png"), 0, largura: 1100, altura: 700);
    }

    [Fact]
    public async Task Gera_o_csv_do_estoque()
    {
        var saida = Saida();
        if (saida is null) return;
        await CadastrarLojaAsync();
        var csv = ExportadorCsv.Estoque(await new ProdutoService(_banco.Fabrica).ListarComTamanhosAsync());
        ExportadorCsv.Salvar(Path.Combine(saida, "estoque.csv"), csv);
    }

    // ---------------- Mais vendidos ----------------

    private async Task VenderAsync(IReadOnlyList<int> ids)
    {
        var vendas = new VendaService(_banco.Fabrica);
        var formas = new[] { FormaPagamento.Pix, FormaPagamento.Dinheiro, FormaPagamento.CartaoCredito, FormaPagamento.CartaoDebito, FormaPagamento.Pix };
        var n = 0;
        for (var i = 0; i < ids.Count - 3; i++)               // não vende as 3 sobras
            for (var k = 0; k <= i % 4; k++)
            {
                var forma = formas[n++ % formas.Length];
                await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(ids[i], 1 + (i + k) % 3) }, 0, forma,
                    forma == FormaPagamento.Dinheiro ? 100000 : null, Parcelas: forma == FormaPagamento.CartaoCredito ? 2 : 1));
            }
    }

    [Fact]
    public async Task Gera_o_mais_vendidos_com_muitos_produtos()
    {
        var saida = Saida();
        if (saida is null) return;
        await VenderAsync(await CadastrarLojaAsync());

        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.TemPizzas);
        Capturador.Desenhar(() => new NexusLS.App.Views.MaisVendidosView { DataContext = vm }, Path.Combine(saida, "mais-vendidos-muitos.png"), 1400, 1000);
    }

    [Fact]
    public async Task Gera_o_mais_vendidos_com_um_so_produto()
    {
        var saida = Saida();
        if (saida is null) return;
        var ids = await CadastrarLojaAsync();
        await new VendaService(_banco.Fabrica).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(ids[0], 3) }, 0, FormaPagamento.Pix));

        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.TemPizzas);
        Capturador.Desenhar(() => new NexusLS.App.Views.MaisVendidosView { DataContext = vm }, Path.Combine(saida, "mais-vendidos-um.png"), 1400, 1000);
    }

    [Fact]
    public async Task Gera_o_mais_vendidos_vazio()
    {
        var saida = Saida();
        if (saida is null) return;
        await CadastrarLojaAsync();

        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.Resumo.Length > 0);
        Capturador.Desenhar(() => new NexusLS.App.Views.MaisVendidosView { DataContext = vm }, Path.Combine(saida, "mais-vendidos-vazio.png"), 1400, 800);
    }

    // ---------------- Fechamento e PDF do caixa ----------------

    private async Task<DateTime> CenarioCaixaAsync(IReadOnlyList<int> ids, int turnos)
    {
        var hoje = DateTime.Today;
        var agora = hoje.AddDays(-turnos).AddHours(8);
        var caixa = new CaixaService(_banco.Fabrica, () => agora);
        var vendas = new VendaService(_banco.Fabrica, () => agora);
        var formas = new[] { FormaPagamento.Dinheiro, FormaPagamento.Pix, FormaPagamento.CartaoCredito, FormaPagamento.CartaoDebito };
        for (var d = 0; d < turnos; d++)
        {
            agora = hoje.AddDays(-turnos + d).AddHours(8);
            var t = await caixa.AbrirAsync(10000 + d * 500);
            for (var v = 0; v < 4; v++)
            {
                agora = agora.AddMinutes(37);
                var forma = formas[(v + d) % formas.Length];
                var venda = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(ids[(d + v) % 7], 1 + v % 2) }, 0, forma,
                    forma == FormaPagamento.Dinheiro ? 100000 : null));
                if (v == 3 && d % 3 == 0) await vendas.CancelarAsync(venda.Id, "Cliente desistiu da compra");
            }
            agora = agora.AddMinutes(10);
            await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Reforco, 5000, "Troco para o caixa");
            await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 8000, "Levou ao banco");
            if (d % 2 == 0) await caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Despesa, 1850, "Café e açúcar para a loja");
            var r = await caixa.ResumoAsync(t.Id);
            agora = agora.AddHours(8);
            var falta = d % 3 == 1 ? 700 : 0;
            await caixa.FecharAsync(t.Id, new ConferenciaCaixa(r.EsperadoDinheiroCentavos - falta, r.EsperadoCartaoCentavos, r.EsperadoPixCentavos,
                falta > 0 ? "Faltou troco, conferir com a funcionária da tarde" : null));
        }
        return hoje;
    }

    [Fact]
    public async Task Gera_o_fechamento_com_o_painel_do_relatorio()
    {
        var saida = Saida();
        if (saida is null) return;
        var ids = await CadastrarLojaAsync();
        await CenarioCaixaAsync(ids, 3);

        var vm = new FechamentoViewModel(new CaixaService(_banco.Fabrica), new ConfiguracaoService(_banco.Fabrica),
            new RelatorioCaixaPeriodoService(_banco.Fabrica));
        await AteAsync(() => vm.Historico.Count > 0);
        Capturador.Desenhar(() => new NexusLS.App.Views.FechamentoView { DataContext = vm }, Path.Combine(saida, "fechamento.png"), 1500, 860);
    }

    [Fact]
    public async Task Gera_o_pdf_do_caixa()
    {
        var saida = Saida();
        if (saida is null) return;
        var ids = await CadastrarLojaAsync();
        var hoje = await CenarioCaixaAsync(ids, 12);
        await new CaixaService(_banco.Fabrica).AbrirAsync(5000);   // um turno aberto para ver o aviso

        var r = await new RelatorioCaixaPeriodoService(_banco.Fabrica).GerarAsync(hoje.AddDays(-12), hoje);
        File.WriteAllBytes(Path.Combine(saida, "caixa-periodo.pdf"), RelatorioCaixaPdf.Gerar(r, "Loja da Maria — Moda & Acessórios"));
        File.WriteAllLines(Path.Combine(saida, "caixa-periodo.txt"), RelatorioCaixaPdf.Linhas(r, "Loja da Maria — Moda & Acessórios"));
    }
}
