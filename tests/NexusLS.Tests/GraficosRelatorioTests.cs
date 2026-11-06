using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;
using static NexusLS.Core.Services.DesempenhoVendas;

namespace NexusLS.Tests;

/// <summary>Os gráficos de Relatórios: mesma base do cabeçalho (itens líquidos, desconto e acréscimo rateados, canceladas fora).</summary>
public sealed class GraficosRelatorioTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static Linha L(int venda, DateTime dia, int? produto, string nome, string categoria, int qtd, long valor,
        FormaPagamento forma = FormaPagamento.Pix) =>
        new(venda, dia, forma, "Ana", true, "Cliente", produto, nome, categoria, qtd, valor, null);

    private static readonly Intervalo Periodo = new(new DateTime(2026, 10, 10), new DateTime(2026, 10, 12));

    [Fact]
    public void Granularidade_acompanha_o_tamanho_do_periodo()
    {
        Assert.Equal(Granularidade.Dia, GraficosRelatorio.GranularidadeSugerida(1));
        Assert.Equal(Granularidade.Dia, GraficosRelatorio.GranularidadeSugerida(62));
        Assert.Equal(Granularidade.Semana, GraficosRelatorio.GranularidadeSugerida(63));
        Assert.Equal(Granularidade.Semana, GraficosRelatorio.GranularidadeSugerida(200));
        Assert.Equal(Granularidade.Mes, GraficosRelatorio.GranularidadeSugerida(201));
    }

    [Fact]
    public void Ranking_por_pecas_e_por_valor_saem_da_mesma_base_e_somam_o_mesmo_total()
    {
        var d = new DateTime(2026, 10, 10);
        var linhas = new[]
        {
            L(1, d, 1, "Camiseta", "Camisetas", 10, 20000),
            L(2, d, 2, "Vestido", "Vestidos", 2, 30000),
            L(3, d.AddDays(1), 1, "Camiseta", "Camisetas", 1, 2000),
            L(4, d.AddDays(2), 3, "Boné", "Acessórios", 5, 10000)
        };

        var g = GraficosRelatorio.Montar(linhas, Array.Empty<Linha>(), Periodo);

        Assert.Equal(new[] { "Camiseta", "Boné", "Vestido" }, g.MaisSairam.Select(p => p.Nome));
        Assert.Equal(new[] { "Vestido", "Camiseta", "Boné" }, g.MaisFaturaram.Select(p => p.Nome));
        Assert.Equal(11, g.MaisSairam[0].Pecas);
        Assert.Equal(22000, g.MaisSairam[0].ValorCentavos);
        // todas as visões fecham no faturamento do período
        var total = linhas.Sum(l => l.ValorCentavos);
        Assert.Equal(total, g.Atual.FaturamentoCentavos);
        Assert.Equal(total, g.Serie.Sum(p => p.FaturamentoCentavos));
        Assert.Equal(total, g.PorCategoria.Sum(c => c.ValorCentavos));
        Assert.Equal(total, g.PorForma.Sum(f => f.ValorCentavos));
        Assert.Equal(total, g.MaisFaturaram.Sum(p => p.ValorCentavos));
    }

    [Fact]
    public void Ranking_limita_a_dez_e_desempata_pelo_nome()
    {
        var d = new DateTime(2026, 10, 10);
        var linhas = Enumerable.Range(1, 12).Select(i => L(i, d, i, $"Produto {i:00}", "Geral", 1, 1000)).ToList();

        var g = GraficosRelatorio.Montar(linhas, Array.Empty<Linha>(), Periodo);

        Assert.Equal(10, g.MaisSairam.Count);
        Assert.Equal(10, g.MaisFaturaram.Count);
        Assert.Equal("Produto 01", g.MaisSairam[0].Nome); // empate em peças e valor: ordem do nome
    }

    [Fact]
    public void Serie_por_dia_traz_os_dias_sem_venda_zerados_e_o_periodo_anterior_na_mesma_posicao()
    {
        var atual = new[] { L(1, new DateTime(2026, 10, 10), 1, "A", "X", 1, 5000), L(2, new DateTime(2026, 10, 12), 1, "A", "X", 1, 7000) };
        var antes = new[] { L(3, new DateTime(2026, 10, 7), 1, "A", "X", 1, 4000), L(4, new DateTime(2026, 10, 8), 1, "A", "X", 1, 1000) };

        var g = GraficosRelatorio.Montar(atual, antes, Periodo);

        Assert.Equal(new DateTime(2026, 10, 7), g.Anterior.Inicio);
        Assert.Equal(new DateTime(2026, 10, 9), g.Anterior.Fim);
        Assert.Equal(new long[] { 5000, 0, 7000 }, g.Serie.Select(p => p.FaturamentoCentavos));
        Assert.Equal(new long[] { 4000, 1000, 0 }, g.SerieAnterior.Select(p => p.FaturamentoCentavos));
        Assert.True(g.TemAnterior);
        Assert.Equal(12000, g.Atual.FaturamentoCentavos);
        Assert.Equal(5000, g.Antes.FaturamentoCentavos);
        Assert.Equal(140m, Variacao(g.Atual.FaturamentoCentavos, g.Antes.FaturamentoCentavos));
    }

    [Fact]
    public void Sem_venda_no_periodo_anterior_nao_ha_comparacao()
    {
        var g = GraficosRelatorio.Montar(new[] { L(1, new DateTime(2026, 10, 10), 1, "A", "X", 1, 5000) }, Array.Empty<Linha>(), Periodo);

        Assert.False(g.TemAnterior);
        Assert.Null(Variacao(g.Atual.FaturamentoCentavos, g.Antes.FaturamentoCentavos));
    }

    [Fact]
    public void Sem_nenhuma_venda_tudo_vem_vazio_sem_quebrar()
    {
        var g = GraficosRelatorio.Montar(Array.Empty<Linha>(), Array.Empty<Linha>(), Periodo);

        Assert.Equal(0, g.Atual.Vendas);
        Assert.Empty(g.MaisSairam);
        Assert.Empty(g.PorCategoria);
        Assert.Empty(g.PorForma);
        Assert.Equal(3, g.Serie.Count); // os três dias aparecem, zerados
    }

    [Fact]
    public async Task Os_graficos_fecham_com_o_total_vendido_mesmo_com_desconto_acrescimo_do_cartao_e_venda_cancelada()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta", "1", 10000, 4000, 1, Categoria: "Camisetas"), 100)).Id;
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné", "2", 3000, 1000, 1, Categoria: "Acessórios"), 100)).Id;

        async Task<Venda> Vender(DateTime dia, FormaPagamento forma, long desconto, int parcelas, params ItemCarrinho[] itens) =>
            await new VendaService(_banco.Fabrica, () => dia.AddHours(10)).FinalizarAsync(
                new PedidoVenda(itens, desconto, forma, forma == FormaPagamento.Dinheiro ? 100000 : null, Parcelas: parcelas));

        await Vender(new DateTime(2026, 10, 10), FormaPagamento.Dinheiro, 1000, 1, new ItemCarrinho(camiseta, 2), new ItemCarrinho(bone, 1));
        await Vender(new DateTime(2026, 10, 11), FormaPagamento.CartaoCredito, 0, 3, new ItemCarrinho(camiseta, 1), new ItemCarrinho(bone, 3));
        await Vender(new DateTime(2026, 10, 12), FormaPagamento.Pix, 500, 1, new ItemCarrinho(bone, 2));
        var cancelada = await Vender(new DateTime(2026, 10, 12), FormaPagamento.Pix, 0, 1, new ItemCarrinho(camiseta, 5));
        await new VendaService(_banco.Fabrica).CancelarAsync(cancelada.Id, "cliente desistiu");
        await Vender(new DateTime(2026, 10, 8), FormaPagamento.Pix, 0, 1, new ItemCarrinho(camiseta, 1)); // período anterior

        var servico = new RelatorioService(_banco.Fabrica);
        var relatorio = await servico.VendasPorPeriodoAsync(new DateTime(2026, 10, 10), new DateTime(2026, 10, 12));
        var g = await servico.GraficosAsync(new DateTime(2026, 10, 10), new DateTime(2026, 10, 12));

        Assert.True(relatorio.TotalCentavos > 0);
        Assert.Equal(relatorio.TotalCentavos, g.Atual.FaturamentoCentavos);        // cartão "Total vendido"
        Assert.Equal(relatorio.QuantidadeVendas, g.Atual.Vendas);                   // cartão "Vendas"
        Assert.Equal(relatorio.TicketMedioCentavos, g.Atual.TicketMedioCentavos);   // cartão "Ticket médio"
        Assert.Equal(relatorio.TotalCentavos, g.Serie.Sum(p => p.FaturamentoCentavos));
        Assert.Equal(relatorio.TotalCentavos, g.PorCategoria.Sum(c => c.ValorCentavos));
        Assert.Equal(relatorio.TotalCentavos, g.PorForma.Sum(f => f.ValorCentavos));
        Assert.Equal(relatorio.TotalCentavos, g.MaisFaturaram.Sum(p => p.ValorCentavos)); // só 2 produtos: o ranking inteiro
        // por dia e por forma batem linha a linha com as tabelas da aba Vendas
        Assert.Equal(relatorio.PorDia.Select(d => d.TotalCentavos), g.Serie.Select(p => p.FaturamentoCentavos));
        foreach (var f in relatorio.PorForma)
            Assert.Equal(f.TotalCentavos, g.PorForma.Single(x => x.Forma == f.Forma).ValorCentavos);
        // a cancelada ficou de fora e o período anterior (3 a 9/10) é só a venda do dia 8
        Assert.Equal(1, g.Antes.Vendas);
        Assert.Equal(10000, g.Antes.FaturamentoCentavos);
    }

    [Fact]
    public async Task Periodo_invalido_continua_avisando()
    {
        var servico = new RelatorioService(_banco.Fabrica);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.GraficosAsync(new DateTime(2026, 10, 12), new DateTime(2026, 10, 10)));
    }
}
