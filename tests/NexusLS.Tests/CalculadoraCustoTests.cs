using NexusLS.Core.Services;
using Xunit;

namespace NexusLS.Tests;

public class CalculadoraCustoTests
{
    private static void Digitar(CalculadoraCusto c, string texto)
    {
        foreach (var ch in texto)
        {
            if (char.IsDigit(ch)) c.Digito(ch - '0');
            else if (ch is ',' or '.') c.Virgula();
            else if (ch == '=') c.Igual();
            else c.Operador(ch);
        }
    }

    [Fact]
    public void Soma_e_multiplicacao_encadeadas()
    {
        var c = new CalculadoraCusto();
        Digitar(c, "12,5*4=");
        Assert.Equal("50", c.Visor);
        Assert.Equal(5000, c.PrecoCentavos);
    }

    [Fact]
    public void Operador_encadeado_mostra_resultado_parcial()
    {
        var c = new CalculadoraCusto();
        Digitar(c, "1+2+");
        Assert.Equal("3", c.Visor);
        Digitar(c, "4=");
        Assert.Equal("7", c.Visor);
    }

    [Fact]
    public void Divisao_por_zero_mostra_erro_e_limpa_ao_digitar()
    {
        var c = new CalculadoraCusto();
        Digitar(c, "5/0=");
        Assert.Equal("Erro", c.Visor);
        Assert.Equal(0, c.PrecoCentavos);
        c.Digito(3);
        Assert.Equal("3", c.Visor);
    }

    [Fact]
    public void Apagar_remove_ultimo_digito()
    {
        var c = new CalculadoraCusto();
        Digitar(c, "123");
        c.Apagar();
        Assert.Equal("12", c.Visor);
        c.Apagar(); c.Apagar();
        Assert.Equal("0", c.Visor);
    }

    [Fact]
    public void Virgula_nao_duplica()
    {
        var c = new CalculadoraCusto();
        Digitar(c, "1,2,3");
        Assert.Equal("1,23", c.Visor);
    }

    [Fact]
    public void Juros_incidem_sobre_o_valor_atual_incluindo_acrescimos()
    {
        var c = new CalculadoraCusto();
        Digitar(c, "100");
        c.AdicionarJuros(10);          // +10,00
        c.AdicionarValor("Frete", 500); // +5,00
        c.AdicionarJuros(10);          // 115,00 * 10% = 11,50
        Assert.Equal(10000 + 1000 + 500 + 1150, c.TotalCentavos);
        Assert.Equal(3, c.Acrescimos.Count);
    }

    [Fact]
    public void Remover_acrescimo_atualiza_total()
    {
        var c = new CalculadoraCusto();
        Digitar(c, "50");
        c.AdicionarValor("Frete", 1000);
        c.RemoverAcrescimo(0);
        Assert.Equal(5000, c.TotalCentavos);
    }

    [Fact]
    public void Juros_e_valor_invalidos_sao_recusados()
    {
        var c = new CalculadoraCusto();
        Assert.Throws<RegraDeNegocioException>(() => c.AdicionarJuros(0));
        Assert.Throws<RegraDeNegocioException>(() => c.AdicionarValor("x", 0));
    }
}
