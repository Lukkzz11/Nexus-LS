using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class ResumoCelularTests
{
    private static ResumoInicio Resumo(string nomeProduto = "Camisa") => new(
        new DateTime(2026, 11, 18), 3, 45000, 15000, 1, 30000, true, new DateTime(2026, 11, 18, 8, 5, 0),
        1, new[] { new ProdutoEstoqueBaixo(nomeProduto, 1, 5) }, 2, 80000, 20000, 1);

    [Fact]
    public void Gera_pagina_para_celular_com_os_numeros_do_dia()
    {
        var html = ResumoCelular.Html(Resumo(), "Loja Teste", new DateTime(2026, 11, 18, 14, 30, 0));
        Assert.Contains("viewport", html);
        Assert.Contains("Loja Teste", html);
        Assert.Contains("450,00", html);
        Assert.Contains("3 venda(s)", html);
        Assert.Contains("Aberto desde 08:05", html);
        Assert.Contains("Camisa", html);
    }

    [Fact]
    public void Texto_com_html_nao_vira_codigo()
    {
        var html = ResumoCelular.Html(Resumo("<script>x</script>"), "A & B", DateTime.Now);
        Assert.DoesNotContain("<script>x", html);
        Assert.Contains("A &amp; B", html);
    }

    [Fact]
    public void Nome_do_arquivo_com_ou_sem_data()
    {
        Assert.Equal("resumo-do-dia.html", ResumoCelular.NomeArquivo(new DateTime(2026, 11, 18), false));
        Assert.Equal("resumo-2026-11-18.html", ResumoCelular.NomeArquivo(new DateTime(2026, 11, 18), true));
    }

    [Fact]
    public void Compara_com_ontem()
    {
        Assert.Equal("+50% em relação a ontem", ResumoCelular.ComparacaoComOntem(45000, 30000));
        Assert.Equal("-50% em relação a ontem", ResumoCelular.ComparacaoComOntem(15000, 30000));
        Assert.Equal("igual a ontem", ResumoCelular.ComparacaoComOntem(100, 100));
        Assert.Null(ResumoCelular.ComparacaoComOntem(100, 0));
    }

    [Fact]
    public void Inclui_pecas_comparacao_e_proximas_contas_com_encode()
    {
        var b = Resumo();
        var r = new ResumoInicio(b.Dia, b.VendasHoje, b.TotalHojeCentavos, b.TicketMedioHojeCentavos, b.CanceladasHoje, b.TotalOntemCentavos,
            b.CaixaAberto, b.CaixaAbertoDesde, b.ProdutosAbaixoDoMinimo, b.PioresEstoques, b.ClientesComDivida, b.DividaTotalCentavos,
            b.DividaVencidaCentavos, b.ClientesComDividaVencida)
        {
            PecasHoje = 7,
            ContasAPagar = new[] { new ContaAVencer(1, "Aluguel <b>", null, new DateTime(2026, 11, 20), 120000, false) },
        };
        var html = ResumoCelular.Html(r, "L", new DateTime(2026, 11, 18, 10, 0, 0));
        Assert.Contains("7 peça(s)", html);
        Assert.Contains("+50% em relação a ontem", html);
        Assert.Contains("Aluguel &lt;b&gt;", html);
        Assert.DoesNotContain("Aluguel <b>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
    }
}
