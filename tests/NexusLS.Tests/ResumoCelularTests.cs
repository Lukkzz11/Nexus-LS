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
}
