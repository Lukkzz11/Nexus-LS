using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Vitrine;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Filtro Sobras da loja no Estoque e as pizzas do Mais vendidos.</summary>
public sealed class SobrasEPizzasTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-sobras-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;

    public SobrasEPizzasTests()
    {
        Directory.CreateDirectory(_pasta);
        _produtos = new ProdutoService(_banco.Fabrica);
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private EstoqueLojaViewModel NovoEstoque(AjustesProdutoStore? store = null) =>
        new(_produtos, new EstoqueService(_banco.Fabrica), ajustesCatalogo: store);

    // ---------- Sobras ----------

    [Fact]
    public async Task Filtro_separa_estoque_normal_e_sobras_e_conta_as_sobras()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1), 5);
        await _produtos.CadastrarAsync(new DadosProduto("Retalho", null, 1000, 300, 0, Sobra: true), 2);
        await _produtos.CadastrarAsync(new DadosProduto("Cinto", null, 3000, 1000, 0, Sobra: true), 1);
        var vm = NovoEstoque();
        await AteAsync(() => vm.Produtos.Count == 3, "lista");

        vm.VisaoSobras = true;
        await AteAsync(() => vm.Produtos.Count == 2, "sobras");
        Assert.All(vm.Produtos, p => Assert.True(p.Sobra));
        Assert.Equal("Sobras (2)", vm.TextoSobras);

        vm.VisaoEstoqueNormal = true;
        await AteAsync(() => vm.Produtos.Count == 1, "estoque normal");
        Assert.Equal("Camiseta", vm.Produtos[0].Nome);

        vm.VisaoTodos = true;
        await AteAsync(() => vm.Produtos.Count == 3, "todos");
    }

    [Fact]
    public async Task Sobra_continua_sendo_estoque_vende_no_pdv_e_entra_na_exportacao()
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto("Retalho", null, 1000, 300, 0, Sobra: true), 3);
        var venda = await new VendaService(_banco.Fabrica).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2, null) }, 0, FormaPagamento.Pix, null));

        Assert.Equal(2000, venda.TotalCentavos);
        Assert.Equal(1, (await _produtos.ObterAsync(p.Id))!.EstoqueAtual);
        Assert.Contains("Retalho", ExportadorCsv.Estoque(await _produtos.ListarComTamanhosAsync()));
    }

    [Fact]
    public async Task Adicionar_sobra_abre_o_cadastro_marcado_e_a_sobra_nova_nasce_fora_do_catalogo()
    {
        var store = new AjustesProdutoStore(Path.Combine(_pasta, "catalogo-produtos.json"));
        var vm = NovoEstoque(store);
        await AteAsync(() => !vm.EstaEditando, "formulário");

        vm.AdicionarSobraCommand.Execute(null);
        Assert.True(vm.Sobra);
        vm.Nome = "Vestido sobra";
        vm.PrecoVenda = "80,00";
        await vm.SalvarCommand.ExecuteAsync(null);

        var salvo = (await _produtos.BuscarAsync("Vestido sobra")).Single();
        Assert.True(salvo.Sobra);
        var ajuste = store.Carregar()[salvo.Guid.ToString("D").ToLowerInvariant()];
        Assert.False(ajuste.Visivel);

        // um produto comum não mexe no arquivo do catálogo
        vm.NovoCommand.Execute(null);
        vm.Nome = "Comum";
        vm.PrecoVenda = "10,00";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Single(store.Carregar());
    }

    [Fact]
    public async Task Editar_pode_marcar_e_desmarcar_a_sobra()
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto("Meia", null, 1000, 300, 0), 1);
        var vm = NovoEstoque();
        await AteAsync(() => vm.Produtos.Count == 1, "lista");
        vm.Selecionado = vm.Produtos[0];
        await AteAsync(() => vm.IdEditando == p.Id, "abrir");

        vm.Sobra = true;
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True((await _produtos.ObterAsync(p.Id))!.Sobra);

        vm.Sobra = false;
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.False((await _produtos.ObterAsync(p.Id))!.Sobra);
    }

    // ---------- Pizzas ----------

    [Fact]
    public void Desenho_da_pizza_cobre_o_circulo_e_usa_cinza_para_Outros()
    {
        var fatias = Participacao.Fatias(Enumerable.Range(1, 8).Select(i => ($"P{i}", (long)i * 100)));
        var desenho = PizzaGrafico.Desenhar(fatias);

        Assert.Equal(7, desenho.Count);
        Assert.Equal(PizzaGrafico.CorOutros, desenho[^1].ChaveCor);
        Assert.Equal("CorSerie1", desenho[0].ChaveCor);
        Assert.All(desenho, d => Assert.StartsWith("M 100,100 L", d.Caminho));
        Assert.Contains("P8 · R$", desenho[0].Legenda);

        var inteira = PizzaGrafico.Desenhar(Participacao.Fatias(new[] { ("Único", 500L) }));
        Assert.Contains("0 1 1", inteira.Single().Caminho); // círculo inteiro, dois arcos
    }

    [Fact]
    public async Task Mais_vendidos_monta_as_tres_pizzas_sem_contar_vendas_canceladas()
    {
        var bone = (await _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0, Categoria: "Acessórios"), 50)).Id;
        var camiseta = (await _produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 0, Categoria: "Roupas"), 50)).Id;
        var vendas = new VendaService(_banco.Fabrica);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1, null) }, 0, FormaPagamento.Pix, null));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 1, null) }, 0, FormaPagamento.Dinheiro, 10000));
        var cancelada = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 9, null) }, 0, FormaPagamento.Pix, null));
        await vendas.CancelarAsync(cancelada.Id, "teste do cancelamento", devolverEstoque: true);

        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.PizzaProdutos.Count == 2, "pizzas");

        Assert.True(vm.TemPizzas);
        Assert.Equal(new[] { "Camiseta", "Boné" }, vm.PizzaProdutos.Select(f => f.Rotulo));   // 5.000 e 2.000: a cancelada ficou fora
        Assert.Equal(new[] { "Roupas", "Acessórios" }, vm.PizzaCategorias.Select(f => f.Rotulo));
        Assert.Equal(2, vm.PizzaFormas.Count);
        Assert.Contains("71,4%", vm.PizzaProdutos[0].Legenda);
    }

    [Fact]
    public async Task As_tres_pizzas_fecham_com_o_cabecalho_mesmo_com_desconto_e_credito_parcelado()
    {
        var bone = (await _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0, Categoria: "Acessórios"), 50)).Id;
        var camiseta = (await _produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 0, Categoria: "Roupas"), 50)).Id;
        var vendas = new VendaService(_banco.Fabrica);
        // R$ 70,00 em itens com R$ 10,00 de desconto, no Pix
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 1, null), new ItemCarrinho(bone, 1, null) }, 1000, FormaPagamento.Pix, null));
        // R$ 100,00 em itens no crédito em 3x (a venda ganha acréscimo, que não é faturamento de produto)
        var credito = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 2, null) }, 0, FormaPagamento.CartaoCredito, null, Parcelas: 3));
        Assert.True(credito.TotalCentavos > 10000, "o crédito parcelado deveria ter acréscimo");

        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.PizzaFormas.Count == 2, "pizzas");

        long Soma(IEnumerable<FatiaDesenhada> f) => f.Sum(x => long.Parse(x.Legenda.Split('·')[1].Replace("R$", "").Replace(".", "").Replace(",", "").Replace(" ", "").Trim()));
        Assert.Equal(16000, Soma(vm.PizzaProdutos));              // 70,00 - 10,00 de desconto + 100,00
        Assert.Equal(16000, Soma(vm.PizzaCategorias));
        Assert.Equal(16000, Soma(vm.PizzaFormas));
        Assert.Contains("Faturamento R$ 160,00", vm.Resumo);
        Assert.Contains("10,00", vm.Resumo);                      // aviso do desconto já abatido
    }

    [Fact]
    public async Task Sem_vendas_nao_ha_pizzas()
    {
        var vm = new MaisVendidosViewModel(new RelatorioService(_banco.Fabrica));
        await AteAsync(() => vm.Resumo == "Nenhuma venda neste período.", "resumo");
        Assert.False(vm.TemPizzas);
        Assert.Empty(vm.PizzaFormas);
    }
}

/// <summary>O painel "Relatório do caixa por período (PDF)" da tela Fechamento.</summary>
public sealed class RelatorioCaixaTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-relcx-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _hoje = new(2026, 10, 20);

    public RelatorioCaixaTelaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private FechamentoViewModel Nova(bool comRelatorio = true) => new(new CaixaService(_banco.Fabrica), new ConfiguracaoService(_banco.Fabrica),
        comRelatorio ? new RelatorioCaixaPeriodoService(_banco.Fabrica, () => _hoje.AddHours(10)) : null, _hoje);

    [Fact]
    public async Task Gera_o_pdf_no_arquivo_escolhido_com_periodo_padrao_do_mes()
    {
        var vm = Nova();
        Assert.True(vm.TemRelatorioPeriodo);
        Assert.Equal(new DateTime(2026, 10, 1), vm.RelatorioDe);
        Assert.Equal(_hoje, vm.RelatorioAte);
        var caminho = Path.Combine(_pasta, "saida.pdf");
        string? sugerido = null;
        vm.EscolherArquivoParaSalvar = nome => { sugerido = nome; return caminho; };

        await vm.GerarRelatorioPdfCommand.ExecuteAsync(null);

        Assert.Equal("caixa_2026-10-01_a_2026-10-20.pdf", sugerido);
        Assert.False(vm.MensagemEhErro);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(caminho), 0, 8));
    }

    [Fact]
    public async Task Periodo_invalido_mostra_o_erro_e_cancelar_a_caixa_nao_grava()
    {
        var vm = Nova();
        vm.EscolherArquivoParaSalvar = _ => null;
        await vm.GerarRelatorioPdfCommand.ExecuteAsync(null);
        Assert.Empty(Directory.GetFiles(_pasta));

        vm.RelatorioAte = _hoje.AddDays(5);
        await vm.GerarRelatorioPdfCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("futuro", vm.Mensagem);

        Assert.False(Nova(comRelatorio: false).TemRelatorioPeriodo);
    }
}
