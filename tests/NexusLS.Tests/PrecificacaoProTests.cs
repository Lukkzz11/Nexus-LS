using NexusLS.Core.Services;
using static NexusLS.Core.Services.PrecificacaoPro;

namespace NexusLS.Tests;

public class PrecificacaoProTests
{
    private static Parametros Base(long custo = 6000) => new() { CustoCompraCentavos = custo };

    [Fact]
    public void Margem_de_40_sobre_custo_de_60_da_preco_de_100()
    {
        var s = Precificar(Base() with { MargemPct = 40 });
        Assert.True(s.Ok);
        Assert.Equal(10000, s.Analise!.PrecoCentavos);
        Assert.Equal(4000, s.Analise.LucroCentavos);
        Assert.Equal(40m, s.Analise.MargemPct);
        Assert.Equal(66.67m, Math.Round(s.Analise.MarkupPct!.Value, 2));
    }

    [Fact]
    public void Despesas_percentuais_entram_no_divisor_e_o_preco_arredonda_para_cima()
    {
        var s = Precificar(Base() with { ImpostosPct = 8, ComissaoPct = 2, MargemPct = 20 });
        Assert.Equal(8572, s.PrecoSemArredondarCentavos);   // 60 / 0,70 = 85,714...
        Assert.Equal(8572, s.Analise!.PrecoCentavos);
        Assert.True(s.Analise.MargemPct >= 19.9m);
    }

    [Fact]
    public void Custo_adicional_em_reais_e_em_percentual_somam_ao_custo()
    {
        var p = Base(10000) with
        {
            Adicionais = new[] { new CustoAdicional("Frete", false, 500), new CustoAdicional("Embalagem", true, 3) }
        };
        Assert.Equal((10000L, 800L, 10800L), CustoAquisicao(p));
    }

    [Fact]
    public void Rateio_fixo_por_peca_entra_no_preco_e_sem_pecas_vira_aviso()
    {
        var com = Precificar(Base() with { FixasMensaisCentavos = 100000, PecasPorMes = 100, MargemPct = 0 });
        Assert.Equal(1000, com.Analise!.RateioFixoCentavos);
        Assert.Equal(7000, com.Analise.PrecoCentavos);

        var sem = Precificar(Base() with { FixasMensaisCentavos = 100000, MargemPct = 0 });
        Assert.True(sem.Ok);
        Assert.Single(sem.Avisos);
        Assert.Equal(0, sem.Analise!.RateioFixoCentavos);
    }

    [Fact]
    public void Percentuais_que_somam_100_ou_mais_sao_recusados()
    {
        var s = Precificar(Base() with { ImpostosPct = 50, ComissaoPct = 20, MargemPct = 30 });
        Assert.False(s.Ok);
        Assert.Contains("100,0%", Assert.Single(s.Erros));
    }

    [Fact]
    public void Sem_custo_ou_com_valor_negativo_e_recusado()
    {
        Assert.False(Precificar(new Parametros()).Ok);
        Assert.False(Precificar(Base() with { ImpostosPct = -1 }).Ok);
    }

    [Fact]
    public void Terminar_em_90_nunca_fica_abaixo_do_calculado()
    {
        Assert.Equal(10090, Precificar(Base() with { MargemPct = 40 }, terminarEm90: true).Analise!.PrecoCentavos);
        Assert.Equal(5090, Precificar(Base(5000), terminarEm90: true).Analise!.PrecoCentavos);
        Assert.Equal(5090, Precificar(Base(4995), terminarEm90: true).Analise!.PrecoCentavos); // 49,90 ficaria abaixo
        Assert.Equal(4990, Precificar(Base(4980), terminarEm90: true).Analise!.PrecoCentavos);
    }

    [Fact]
    public void Analise_fecha_os_totais_e_o_equilibrio_tem_lucro_zero_ou_pouco_acima()
    {
        var p = Base() with { ImpostosPct = 8, ComissaoPct = 2, OutrasVariaveisCentavos = 150 };
        var a = Analisar(12000, p);
        Assert.Equal(a.ImpostosCentavos + a.ComissaoCentavos + a.TaxaPagamentoCentavos + a.OutrasPctCentavos + a.OutrasReaisCentavos, a.VariaveisCentavos);
        Assert.Equal(12000 - 6000 - a.VariaveisCentavos, a.ContribuicaoCentavos);
        Assert.Equal(a.ContribuicaoCentavos - a.FixasCentavos, a.LucroCentavos);

        var eq = PrecoDeEquilibrio(p)!.Value;
        Assert.InRange(Analisar(eq, p).LucroCentavos, 0, 2);
        Assert.True(Analisar(eq - 5, p).LucroCentavos < 0);
    }

    [Fact]
    public void Promocao_avisa_prejuizo_e_calcula_quanto_a_venda_precisa_crescer()
    {
        var p = Base() with { ImpostosPct = 10 };
        var ok = SimularPromocao(10000, false, 10, p);
        Assert.True(ok.Ok);
        Assert.Equal(9000, ok.PrecoPromocionalCentavos);
        Assert.Equal(1000, ok.DescontoCentavos);
        Assert.Empty(ok.Alertas);
        // lucro 10000-6000-1000 = 3000 → 9000-6000-900 = 2100: precisa vender 42,86% mais
        Assert.Equal(42.86m, Math.Round(ok.VendaExtraNecessariaPct!.Value, 2));

        var ruim = SimularPromocao(10000, false, 50, p);
        Assert.Contains(ruim.Alertas, a => a.Erro && a.Texto.Contains("prejuízo"));
        Assert.Null(ruim.VendaExtraNecessariaPct);

        Assert.False(SimularPromocao(10000, false, 100, p).Ok);
        Assert.False(SimularPromocao(10000, true, 10000, p).Ok);
        Assert.False(SimularPromocao(0, false, 10, p).Ok);
    }

    [Fact]
    public void Promocao_respeita_preco_minimo_e_margem_minima()
    {
        var r = SimularPromocao(10000, false, 20, Base(), precoMinimoCentavos: 9000, margemMinimaPct: 30);
        Assert.Contains(r.Alertas, a => a.Texto.Contains("mínimo que você definiu"));
        Assert.Contains(r.Alertas, a => a.Texto.Contains("Margem abaixo"));
    }

    [Fact]
    public void Comparador_desconta_a_taxa_de_cada_forma_e_marca_as_nao_configuradas()
    {
        var linhas = CompararPagamentos(10000, Base(), new[]
        {
            new CenarioPagamento("Pix", 1m), new CenarioPagamento("Débito", null), new CenarioPagamento("Crédito em 3x", 9m)
        });
        Assert.Equal(9900, linhas[0].RecebidoCentavos);
        Assert.Equal(3900, linhas[0].LucroCentavos);
        Assert.False(linhas[1].Configurada);
        Assert.Equal(10000, linhas[1].RecebidoCentavos);
        Assert.Equal(9100, linhas[2].RecebidoCentavos);
        Assert.Equal(3100, linhas[2].LucroCentavos);
    }

    [Fact]
    public void Quantidades_multiplicam_o_lucro_e_calculam_meta_e_equilibrio_mensal()
    {
        var p = Base() with { FixasMensaisCentavos = 200000, PecasPorMes = 100 };
        var q = SimularQuantidades(10000, p, new[] { 1, 10, 0 }, metaLucroCentavos: 100000);
        Assert.Equal(2, q.Linhas.Count);            // quantidade 0 é descartada
        Assert.Equal(2000, q.LucroPorPecaCentavos); // 100 − 60 − 20 de rateio
        Assert.Equal(20000, q.Linhas[1].LucroCentavos);
        Assert.Equal(50, q.QuantidadeParaMeta);
        Assert.False(q.MetaImpossivel);
        Assert.Equal(50, q.EquilibrioMensalPecas);  // 2.000,00 ÷ 40,00 de contribuição por peça

        var prejuizo = SimularQuantidades(5000, Base(), new[] { 1 }, metaLucroCentavos: 1000);
        Assert.True(prejuizo.MetaImpossivel);
        Assert.Null(prejuizo.QuantidadeParaMeta);
    }

    [Fact]
    public void Lote_rateia_o_frete_pelo_valor_ou_por_peca_e_a_soma_fecha_no_centavo()
    {
        var itens = new[] { new ItemLote("A", 10, 1000), new ItemLote("B", 5, 2000), new ItemLote("C", 3, 333) };
        var r = RatearLote(itens, new[] { new DespesaLote("Frete", 1001, false), new DespesaLote("Caixas", 301, true) });

        Assert.Equal(1302, r.TotalDespesasCentavos);
        Assert.Equal(r.Itens.Sum(i => i.TotalItemCentavos), r.TotalLoteCentavos);
        Assert.Equal(10000 + 10000 + 999 + 1302, r.TotalLoteCentavos);
        Assert.True(r.Itens[0].RateioUnitarioCentavos > 0);
    }

    [Fact]
    public void Lote_recusa_valores_negativos_e_lote_vazio()
    {
        Assert.Throws<RegraDeNegocioException>(() => RatearLote(Array.Empty<ItemLote>(), Array.Empty<DespesaLote>()));
        Assert.Throws<RegraDeNegocioException>(() => RatearLote(new[] { new ItemLote("A", 1, -5) }, Array.Empty<DespesaLote>()));
        Assert.Throws<RegraDeNegocioException>(() => RatearLote(new[] { new ItemLote("A", 1, 5) }, new[] { new DespesaLote("x", -1, false) }));
    }

    [Fact]
    public void Avaliar_produto_aponta_o_que_falta_no_cadastro()
    {
        Assert.Equal(2, AvaliarProduto(0, 0, 0).Count);
        Assert.Contains("abaixo do custo", Assert.Single(AvaliarProduto(5000, 4000, 0)));
        Assert.Contains("atacado está maior", Assert.Single(AvaliarProduto(1000, 5000, 6000)));
        Assert.Empty(AvaliarProduto(1000, 5000, 4000));
    }
}
