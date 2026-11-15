using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>A tela Trocas (marcar peças, gerar e imprimir o vale) e o vale no pagamento do PDV (F6), tecla por tecla.</summary>
public sealed class TrocaTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-troca-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly TrocaService _trocas;
    private readonly ImpressoraDeMentira _impressora = new();
    private readonly CupomService _cupons;
    private readonly PdvViewModel _pdv;
    private int _boneId;

    public TrocaTelaTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica);
        _trocas = new TrocaService(_banco.Fabrica);
        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        _cupons = new CupomService(_banco.Fabrica, configuracao, _impressora);
        var rascunho = new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "rascunho.json")), _banco.Fabrica);
        _pdv = new PdvViewModel(_produtos, _vendas, rascunho, new ClienteService(_banco.Fabrica), _cupons, TimeSpan.Zero) { Trocas = _trocas };
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private async Task<Venda> VendaDeDoisBonesAsync()
    {
        _boneId = (await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 10)).Id;
        return await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_boneId, 2) }, 0, FormaPagamento.Pix));
    }

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    [Fact]
    public async Task Tela_de_troca_marca_a_peca_gera_e_imprime_o_vale()
    {
        var venda = await VendaDeDoisBonesAsync();
        var vm = new TrocasViewModel(_trocas, _cupons);
        await vm.Carregamento;

        vm.NumeroVendaTexto = "#" + venda.Id;
        await vm.BuscarVendaCommand.ExecuteAsync(null);
        var linha = Assert.Single(vm.Linhas);
        linha.QuantidadeTexto = "1";
        Assert.Contains("R$ 20,00", vm.TotalTrocaTexto.Replace(' ', ' '));

        linha.QuantidadeTexto = "3"; // mais do que foi vendido
        await vm.GerarValeCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);

        linha.QuantidadeTexto = "1";
        vm.Motivo = "Cor errada";
        await vm.GerarValeCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.NotNull(vm.UltimoValeCodigo);
        Assert.Contains(vm.UltimoValeCodigo!, _impressora.Impressos.Last());
        Assert.Contains("VALE-TROCA", _impressora.Impressos.Last());
        Assert.Single(vm.Devolucoes);
        Assert.Equal(2000, Assert.Single(vm.Vales).SaldoCentavos);
    }

    /// <summary>Desenha a tela Trocas (só com NEXUSLS_PRINTS): pega erro de XAML que só aparece ao abrir a tela.</summary>
    [Fact]
    public async Task Gera_a_tela_de_trocas()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var venda = await VendaDeDoisBonesAsync();
        await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(venda.Itens.Single().Id, 1) }, "Cor errada");
        var vm = new TrocasViewModel(_trocas, _cupons);
        await vm.Carregamento;
        vm.NumeroVendaTexto = venda.Id.ToString();
        await vm.BuscarVendaCommand.ExecuteAsync(null);
        vm.Linhas.Single().QuantidadeTexto = "1";
        vm.Motivo = "Tamanho errado";
        Capturador.Desenhar(() => new NexusLS.App.Views.TrocasView { DataContext = vm }, Path.Combine(saida, "trocas.png"), 1400, 820);
        Assert.True(File.Exists(Path.Combine(saida, "trocas.png")));
    }

    [Fact]
    public async Task F6_no_pagamento_aplica_o_vale_e_a_venda_sai_paga_com_ele()
    {
        var venda = await VendaDeDoisBonesAsync();
        var vale = (await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(venda.Itens.Single().Id, 1) }, "Troca")).Vale!;
        _pdv.PedirCodigoVale = () => vale.Codigo;

        // um boné (20,00) pago com o vale de 20,00
        _pdv.Busca = "Boné";
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _pdv.Etapa == EtapaPdv.Tamanho, "tamanho");
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Busca, _pdv.Etapa);
        _pdv.ProcessarTecla(ComandoTecla.Enter); // desconto
        _pdv.ProcessarTecla(ComandoTecla.Enter); // pagamento
        Assert.Equal(EtapaPdv.Pagamento, _pdv.Etapa);
        Assert.True(_pdv.AceitaVale);

        _pdv.ProcessarTecla(ComandoTecla.F6);
        await AteAsync(() => _pdv.ValeTexto.Length > 0, "vale aplicado");
        Assert.Equal("R$ 0,00", _pdv.TotalFinalTexto.Replace(' ', ' '));

        _pdv.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _pdv.Etapa == EtapaPdv.Sucesso, "sucesso");

        await using var db = _banco.CriarContexto();
        var nova = db.Vendas.OrderBy(v => v.Id).Last();
        Assert.Equal(FormaPagamento.ValeTroca, nova.FormaPagamento);
        Assert.Equal(2000, nova.ValeTrocaCentavos);
        Assert.Equal(0, (await _trocas.ConsultarValeAsync(vale.Codigo))!.SaldoCentavos);
    }

    [Fact]
    public async Task F6_de_novo_tira_o_vale_e_vale_que_nao_existe_avisa()
    {
        var venda = await VendaDeDoisBonesAsync();
        var vale = (await _trocas.DevolverAsync(venda.Id, new[] { new ItemADevolver(venda.Itens.Single().Id, 1) }, "Troca")).Vale!;

        _pdv.Busca = "Boné";
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _pdv.Etapa == EtapaPdv.Tamanho, "tamanho");
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        _pdv.ProcessarTecla(ComandoTecla.Cima);  // 2 bonés = 40,00
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        _pdv.ProcessarTecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Pagamento, _pdv.Etapa);

        _pdv.PedirCodigoVale = () => "VTNAOEXISTE";
        _pdv.ProcessarTecla(ComandoTecla.F6);
        await AteAsync(() => _pdv.MensagemEhErro, "erro do vale");
        Assert.Equal("", _pdv.ValeTexto);

        _pdv.PedirCodigoVale = () => vale.Codigo;
        _pdv.ProcessarTecla(ComandoTecla.F6);
        await AteAsync(() => _pdv.ValeTexto.Length > 0, "vale aplicado");
        Assert.Equal("R$ 20,00", _pdv.TotalFinalTexto.Replace(' ', ' ')); // 40 - 20
        Assert.Equal("20,00", _pdv.ValorRecebidoTexto);

        _pdv.ProcessarTecla(ComandoTecla.F6);
        await AteAsync(() => _pdv.ValeTexto.Length == 0, "vale retirado");
        Assert.Equal("R$ 40,00", _pdv.TotalFinalTexto.Replace(' ', ' '));
    }
}
