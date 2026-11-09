using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class MascarasEModelosTests
{
    [Theory]
    [InlineData("8", "(8")]
    [InlineData("81", "(81")]
    [InlineData("819", "(81) 9")]
    [InlineData("8192142", "(81) 9.2142")]
    [InlineData("81921423368", "(81) 9.2142-3368")]
    [InlineData("(81) 9.2142-3368999", "(81) 9.2142-3368")]
    [InlineData("8133334444", "(81) 3333-4444")]
    [InlineData("813333", "(81) 3333")]
    [InlineData("", "")]
    public void Telefone_ganha_a_mascara_enquanto_digita(string digitado, string esperado) =>
        Assert.Equal(esperado, ValidadorDocumento.FormatarTelefoneDigitando(digitado));

    [Theory]
    [InlineData("55038", "55038")]
    [InlineData("55038275", "55038-275")]
    [InlineData("55038-275999", "55038-275")]
    public void Cep_ganha_a_mascara(string digitado, string esperado) =>
        Assert.Equal(esperado, ValidadorDocumento.FormatarCepDigitando(digitado));

    private static string Cupom(int modelo)
    {
        var venda = MontadorCupom.VendaDeExemplo();
        return MontadorCupom.Montar(venda, new ConfiguracaoLoja { NomeLoja = "Life", CupomModelo = modelo });
    }

    [Fact]
    public void Os_tres_modelos_saem_diferentes_e_sempre_dizem_que_nao_e_fiscal()
    {
        var life = Cupom(0); var compacto = Cupom(1); var comprovante = Cupom(2);
        Assert.Contains("CUPOM NÃO FISCAL", life);
        Assert.Contains("ITENS DA VENDA", life);
        Assert.DoesNotContain("ITENS DA VENDA", compacto);
        Assert.Contains("2x Camiseta Básica (M)", compacto);          // um item por linha
        Assert.Contains("(NÃO FISCAL)", comprovante);
        Assert.Contains("Total da Nota:", comprovante);
        Assert.Contains("Valor Recebido:", comprovante);
        Assert.Equal(3, new[] { life, compacto, comprovante }.Distinct().Count());
    }

    [Fact]
    public void Linhas_do_cupom_nao_passam_da_largura_em_nenhum_modelo()
    {
        foreach (var modelo in new[] { 0, 1, 2 })
            foreach (var largura in new[] { 32, 42, 48 })
            {
                var cupom = MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(),
                    new ConfiguracaoLoja { NomeLoja = "Life Surf Confecções", LarguraCupom = largura, CupomModelo = modelo, CupomTituloEmCaixa = true });
                Assert.All(cupom.Split('\n'), l => Assert.True(l.Length <= largura, $"modelo {modelo}/{largura}: \"{l}\""));
            }
    }
}
