using PdvSimples.Core.Services;

namespace PdvSimples.Tests;

public class DinheiroTests
{
    private static string Normalizar(string s) => s.Replace(' ', ' ');

    [Theory]
    [InlineData(1250, "R$ 12,50")]
    [InlineData(5, "R$ 0,05")]
    [InlineData(123456, "R$ 1.234,56")]
    [InlineData(0, "R$ 0,00")]
    public void Formata_centavos_em_reais(long centavos, string esperado) =>
        Assert.Equal(esperado, Normalizar(Dinheiro.Formatar(centavos)));

    [Theory]
    [InlineData("12,50", 1250)]
    [InlineData("12.50", 1250)]
    [InlineData("12.5", 1250)]
    [InlineData("12", 1200)]
    [InlineData("0,05", 5)]
    [InlineData("1.250", 125000)]
    [InlineData("R$ 1.250,00", 125000)]
    [InlineData("  3,99  ", 399)]
    public void Converte_texto_em_centavos(string texto, long esperado)
    {
        Assert.True(Dinheiro.TentarConverter(texto, out var centavos));
        Assert.Equal(esperado, centavos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1,234")]
    [InlineData("12,5,0")]
    public void Recusa_texto_invalido(string? texto) =>
        Assert.False(Dinheiro.TentarConverter(texto, out _));

    [Fact]
    public void Formato_de_edicao_pode_ser_lido_de_volta()
    {
        var texto = Dinheiro.FormatarParaEdicao(125050); // "1.250,50"
        Assert.True(Dinheiro.TentarConverter(texto, out var centavos));
        Assert.Equal(125050, centavos);
    }
}
