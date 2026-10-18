using System.IO;
using System.Text;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>As telas "Caixa" (vendas do dia, resumo e exportação) e "Mais vendidos".</summary>
public sealed class CaixaEMaisVendidosTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-caixa-" + Guid.NewGuid().ToString("N"));
    private readonly VendaService _vendas;
    private readonly ProdutoService _produtos;
    private readonly RelatorioService _relatorio;
    private int _boneId;
    private int _camisetaId;

    public CaixaEMaisVendidosTests()
    {
        Directory.CreateDirectory(_pasta);
        _vendas = new VendaService(_banco.Fabrica);
        _produtos = new ProdutoService(_banco.Fabrica);
        _relatorio = new RelatorioService(_banco.Fabrica);
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private async Task PrepararAsync()
    {
        _boneId = (await _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 50)).Id;
        _camisetaId = (await _produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1), 0)).Id;
        await _produtos.AdicionarTamanhoAsync(_camisetaId, "M");
        await _produtos.AdicionarTamanhoAsync(_camisetaId, "P");
        await new EstoqueService(_banco.Fabrica).AjustarAsync(_camisetaId, TipoMovimentacao.Entrada, 50, null, "M");
        await new EstoqueService(_banco.Fabrica).AjustarAsync(_camisetaId, TipoMovimentacao.Entrada, 50, null, "P");
    }

    private static ItemCarrinho I(int produtoId, int quantidade, string? tamanho = null) => new(produtoId, quantidade, tamanho);

    private Task<Venda> VenderAsync(FormaPagamento forma, params ItemCarrinho[] itens) =>
        _vendas.FinalizarAsync(new PedidoVenda(itens, 0, forma, forma == FormaPagamento.Dinheiro ? 100000 : null));

    private VendasViewModel NovoCaixa()
    {
        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        return new VendasViewModel(_vendas, cupons);
    }

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private string Arquivo(string nome) => Path.Combine(_pasta, nome);

    private static string Ler(string caminho) => File.ReadAllText(caminho, Encoding.UTF8);

    // ---------- Caixa ----------

    [Fact]
    public async Task Resumo_do_dia_mostra_o_total_por_forma_de_pagamento_sem_as_canceladas()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Dinheiro, I(_boneId, 2));      // 40,00
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 1));           // 20,00
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 1));           // 20,00
        var cancelada = await VenderAsync(FormaPagamento.Dinheiro, I(_boneId, 10));
        await _vendas.CancelarAsync(cancelada.Id, "Teste");

        var caixa = NovoCaixa();
        await AteAsync(() => caixa.Vendas.Count == 4, "lista do dia");

        Assert.Contains("3 venda(s)", caixa.Resumo);
        Assert.Contains("1 cancelada(s)", caixa.Resumo);
        var formas = caixa.ResumoFormas.Replace(' ', ' ');
        Assert.Contains("Dinheiro: R$ 40,00 (1)", formas);
        Assert.Contains("Pix: R$ 40,00 (2)", formas);
    }

    [Fact]
    public async Task Cancelar_na_tela_pede_o_motivo_e_mostra_quem_cancelou_e_o_estoque()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Dinheiro, I(_boneId, 2));
        var caixa = NovoCaixa();
        await AteAsync(() => caixa.Vendas.Count == 1, "lista do dia");
        caixa.Selecionada = caixa.Vendas[0];

        caixa.PedirCancelamento = _ => null; // a pessoa desistiu
        await caixa.CancelarVendaCommand.ExecuteAsync(null);
        Assert.False(caixa.Vendas[0].Cancelada);
        Assert.Equal("", caixa.TextoCancelamento);

        caixa.PedirCancelamento = _ => new DadosCancelamento("Cliente desistiu", false);
        await caixa.CancelarVendaCommand.ExecuteAsync(null);
        await AteAsync(() => caixa.Vendas.Count == 1 && caixa.Vendas[0].Cancelada, "venda cancelada");
        caixa.Selecionada = caixa.Vendas[0];

        Assert.Contains("Cliente desistiu", caixa.TextoCancelamento);
        Assert.Contains("NÃO foi devolvido", caixa.TextoCancelamento);
        Assert.Contains("Operador: Caixa", caixa.TextoOperador);
    }

    [Fact]
    public async Task Exportar_vendas_grava_o_csv_com_todas_as_vendas_do_dia_inclusive_a_cancelada_marcada()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Dinheiro, I(_boneId, 2));
        var cancelada = await VenderAsync(FormaPagamento.Pix, I(_boneId, 1));
        await _vendas.CancelarAsync(cancelada.Id, "Teste");
        var caixa = NovoCaixa();
        await AteAsync(() => caixa.Vendas.Count == 2, "lista do dia");
        var destino = Arquivo("vendas.csv");
        string? nomeSugerido = null;
        caixa.EscolherArquivoParaSalvar = nome => { nomeSugerido = nome; return destino; };

        await caixa.ExportarVendasCommand.ExecuteAsync(null);

        Assert.False(caixa.MensagemEhErro, caixa.Mensagem);
        Assert.Equal($"vendas-{DateTime.Today:yyyy-MM-dd}.csv", nomeSugerido);
        Assert.Contains(destino, caixa.Mensagem);
        var csv = Ler(destino);
        Assert.StartsWith("﻿", File.ReadAllText(destino, new UTF8Encoding(false))); // BOM: o Excel lê os acentos
        var linhas = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, linhas.Length);                       // cabeçalho + 2 vendas
        Assert.Contains(linhas, l => l.Contains("Dinheiro") && l.Contains(";Concluída;Caixa"));
        Assert.Contains(linhas, l => l.Contains("Pix") && l.Contains(";Cancelada;") && l.EndsWith(";Teste"));
    }

    [Fact]
    public async Task Exportar_itens_grava_uma_linha_por_item_com_o_tamanho()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 1), I(_camisetaId, 2, "M"));
        var caixa = NovoCaixa();
        await AteAsync(() => caixa.Vendas.Count == 1, "lista do dia");
        var destino = Arquivo("itens.csv");
        caixa.EscolherArquivoParaSalvar = _ => destino;

        await caixa.ExportarItensCommand.ExecuteAsync(null);

        var linhas = Ler(destino).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, linhas.Length);                       // cabeçalho + 2 itens
        Assert.Contains(linhas, l => l.Contains(";Camiseta;M;2;"));
        Assert.Contains(linhas, l => l.Contains(";Boné;;1;"));
    }

    [Fact]
    public async Task Cancelar_a_caixa_salvar_como_nao_cria_arquivo_nem_mostra_erro()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 1));
        var caixa = NovoCaixa();
        await AteAsync(() => caixa.Vendas.Count == 1, "lista do dia");
        caixa.EscolherArquivoParaSalvar = _ => null;

        await caixa.ExportarVendasCommand.ExecuteAsync(null);

        Assert.False(caixa.MensagemEhErro);
        Assert.Empty(Directory.GetFiles(_pasta));
    }

    [Fact]
    public async Task Dia_sem_vendas_nao_exporta()
    {
        var caixa = NovoCaixa();
        await Task.Delay(100);
        caixa.EscolherArquivoParaSalvar = _ => Arquivo("x.csv");

        await caixa.ExportarVendasCommand.ExecuteAsync(null);

        Assert.True(caixa.MensagemEhErro);
        Assert.Contains("Não há vendas", caixa.Mensagem);
        Assert.False(File.Exists(Arquivo("x.csv")));
    }

    [Fact]
    public async Task Falha_ao_gravar_o_arquivo_vira_mensagem_e_nao_derruba_a_tela()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 1));
        var caixa = NovoCaixa();
        await AteAsync(() => caixa.Vendas.Count == 1, "lista do dia");
        caixa.EscolherArquivoParaSalvar = _ => Path.Combine(_pasta, "pasta-que-nao-existe", "x.csv");

        await caixa.ExportarVendasCommand.ExecuteAsync(null);

        Assert.True(caixa.MensagemEhErro);
        Assert.Contains("Erro inesperado", caixa.Mensagem);
    }

    // ---------- Mais vendidos ----------

    [Fact]
    public async Task Comeca_no_mes_atual_e_mostra_o_ranking_com_resumo()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 5), I(_camisetaId, 2, "M"));
        await VenderAsync(FormaPagamento.Pix, I(_camisetaId, 1, "P"));

        var vm = new MaisVendidosViewModel(_relatorio);
        await AteAsync(() => vm.Ranking.Count == 2, "ranking");

        Assert.Equal(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), vm.De);
        Assert.Equal(DateTime.Today, vm.Ate);
        Assert.Equal(new[] { "Boné", "Camiseta" }, vm.Ranking.Select(r => r.Nome));
        Assert.Equal(5, vm.Ranking[0].Quantidade);
        Assert.Equal(3, vm.Ranking[1].Quantidade);          // 2 (M) + 1 (P)
        Assert.Contains("8 unidade(s)", vm.Resumo);
    }

    [Fact]
    public async Task Separar_por_tamanho_divide_o_produto_em_linhas()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, I(_camisetaId, 2, "M"), I(_camisetaId, 1, "P"));
        var vm = new MaisVendidosViewModel(_relatorio);
        await AteAsync(() => vm.Ranking.Count == 1, "ranking");

        vm.PorTamanho = true;
        await AteAsync(() => vm.Ranking.Count == 2, "ranking por tamanho");

        Assert.Equal(new[] { ("M", 2), ("P", 1) }, vm.Ranking.Select(r => (r.Tamanho!, r.Quantidade)));
        Assert.Contains("linha(s)", vm.Resumo);
    }

    [Fact]
    public async Task Atalhos_de_periodo_acertam_as_datas()
    {
        var vm = new MaisVendidosViewModel(_relatorio);
        await Task.Delay(100);

        vm.UltimosSeteDiasCommand.Execute(null);
        Assert.Equal((DateTime.Today.AddDays(-6), DateTime.Today), (vm.De!.Value, vm.Ate!.Value));

        vm.HojeCommand.Execute(null);
        Assert.Equal((DateTime.Today, DateTime.Today), (vm.De!.Value, vm.Ate!.Value));

        vm.MesAtualCommand.Execute(null);
        Assert.Equal(1, vm.De!.Value.Day);
    }

    [Fact]
    public async Task Data_final_antes_da_inicial_avisa_e_limpa_o_ranking()
    {
        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 1));
        var vm = new MaisVendidosViewModel(_relatorio);
        await AteAsync(() => vm.Ranking.Count == 1, "ranking");

        vm.De = DateTime.Today;
        vm.Ate = DateTime.Today.AddDays(-3);
        await AteAsync(() => vm.MensagemEhErro, "aviso de datas");

        Assert.Contains("data final", vm.Mensagem);
        Assert.Empty(vm.Ranking);
    }

    [Fact]
    public async Task Exportar_o_ranking_grava_o_csv_e_sem_dados_avisa()
    {
        var vazio = new MaisVendidosViewModel(_relatorio);
        await Task.Delay(100);
        await vazio.ExportarCommand.ExecuteAsync(null);
        Assert.True(vazio.MensagemEhErro);

        await PrepararAsync();
        await VenderAsync(FormaPagamento.Pix, I(_boneId, 4));
        var vm = new MaisVendidosViewModel(_relatorio);
        await AteAsync(() => vm.Ranking.Count == 1, "ranking");
        var destino = Arquivo("ranking.csv");
        string? sugerido = null;
        vm.EscolherArquivoParaSalvar = nome => { sugerido = nome; return destino; };

        await vm.ExportarCommand.ExecuteAsync(null);

        Assert.StartsWith("mais-vendidos-", sugerido);
        var linhas = Ler(destino).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Posição;Produto;Quantidade;Faturamento;Vendas", linhas[0]);
        Assert.Equal("1;Boné;4;80,00;1", linhas[1]);
    }
}
