using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class DesempenhoFiltrosTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 10, 31);
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 10000, 4000, 1, Categoria: "Acessórios"), 50);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1, Categoria: "Roupas"), 50);
        await using var db = _banco.CriarContexto();
        Venda Nova(Produto p, long total, FormaPagamento forma, string operador) => new()
        {
            DataHora = Hoje.AddHours(9), TotalCentavos = total, SubtotalCentavos = total, FormaPagamento = forma, Operador = operador,
            Itens = { new ItemVenda { ProdutoId = p.Id, NomeProduto = p.Nome, Quantidade = 1, PrecoUnitarioCentavos = total, TotalCentavos = total, CustoUnitarioCentavos = p.PrecoCustoCentavos } }
        };
        db.Vendas.AddRange(Nova(bone, 10000, FormaPagamento.Pix, "Ana"), Nova(camiseta, 5000, FormaPagamento.Dinheiro, "Bia"), Nova(camiseta, 5000, FormaPagamento.Pix, "Ana"));
        await db.SaveChangesAsync();
    }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    [Fact]
    public async Task Filtros_por_categoria_pagamento_e_vendedor_recalculam_o_relatorio()
    {
        await PrepararAsync();
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje);
        await Esperar(() => vm.Indicadores.Count == 5 && vm.OpcoesCategoria.Count == 3);
        Assert.Equal("R$ 200,00", vm.Indicadores[0].Valor);
        Assert.Contains("Roupas", vm.OpcoesCategoria);
        Assert.Contains("Ana", vm.OpcoesOperador);

        vm.FiltroCategoria = "Roupas";
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 100,00");
        Assert.True(vm.TemFiltro);
        Assert.Equal(3, vm.OpcoesCategoria.Count);          // as opções não somem ao filtrar

        vm.FiltroForma = "Pix";
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 50,00");

        vm.LimparFiltrosCommand.Execute(null);
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 200,00");
        Assert.False(vm.TemFiltro);

        vm.FiltroOperador = "Bia";
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 50,00");
        vm.LimparFiltrosCommand.Execute(null);
        vm.FiltroProduto = "bon";
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 100,00");
    }

    [Fact]
    public async Task Exportar_grava_o_csv_so_com_as_linhas_filtradas()
    {
        await PrepararAsync();
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje);
        await Esperar(() => vm.Indicadores.Count == 5);
        vm.FiltroCategoria = "Roupas";
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 100,00");

        var arquivo = Path.Combine(Path.GetTempPath(), $"desempenho-{Guid.NewGuid():N}.csv");
        string? sugerido = null;
        vm.EscolherArquivoParaSalvar = nome => { sugerido = nome; return arquivo; };
        try
        {
            await vm.ExportarCommand.ExecuteAsync(null);
            Assert.False(vm.MensagemEhErro, vm.Mensagem);
            Assert.Equal("desempenho-2026-10-02_a_2026-10-31.csv", sugerido);
            var linhas = File.ReadAllLines(arquivo);
            Assert.Equal(3, linhas.Length);                // cabeçalho + 2 camisetas
            Assert.StartsWith("Venda;Data;Produto;Categoria", linhas[0].TrimStart('﻿'));
            Assert.All(linhas.Skip(1), l => Assert.Contains("Camiseta;Roupas", l));
        }
        finally { File.Delete(arquivo); }
    }

    [Fact]
    public async Task Exportar_sem_vendas_avisa_e_cancelar_a_caixa_nao_grava()
    {
        await PrepararAsync();
        var vm = new DesempenhoVendasViewModel(new DesempenhoService(_banco.Fabrica), () => Hoje);
        await Esperar(() => vm.Indicadores.Count == 5);

        await vm.ExportarCommand.ExecuteAsync(null);       // a caixa devolve nulo = cancelou
        Assert.False(vm.MensagemEhErro);

        vm.FiltroProduto = "nada disso";
        await Esperar(() => vm.Indicadores[0].Valor == "R$ 0,00");
        await vm.ExportarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Não há vendas", vm.Mensagem);
    }
}
