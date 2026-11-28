using NexusLS.Core.Etiquetas;

namespace NexusLS.Tests;

public class BarrasNaImpressoraTests
{
    private const double DipPorMm = 96.0 / 25.4;
    private static double Ponto => BarrasNaImpressora.PontoDip(203);

    /// <summary>O espaço do código na tag 50 × 75: 37,5 mm no meio, 6,25 mm de folga de cada lado.</summary>
    private static PosicaoBarras NaTag(int modulos, double colunaMm = 0, int silencio = 10) =>
        BarrasNaImpressora.Calcular(modulos, (colunaMm + 6.25) * DipPorMm, 37.5 * DipPorMm,
            6.25 * DipPorMm, 6.25 * DipPorMm, 203, silencio);

    [Theory]
    [InlineData(79, 0)]
    [InlineData(90, 50)]
    [InlineData(112, 50)]
    [InlineData(123, 0)]
    public void Modulo_e_inteiro_e_o_comeco_cai_num_ponto_da_pagina(int modulos, double colunaMm)
    {
        var p = NaTag(modulos, colunaMm);

        Assert.Equal(p.PontosPorModulo * Ponto, p.ModuloDip, 9);
        Assert.Equal(Math.Round(p.InicioDip / Ponto), p.InicioDip / Ponto, 6);
        Assert.True(p.PontosPorModulo >= 2);

        // zona de silêncio de 10 módulos dentro da própria etiqueta
        var silencio = 10 * p.ModuloDip - 0.01;
        Assert.True(p.InicioDip - colunaMm * DipPorMm >= silencio);
        Assert.True((colunaMm + 50) * DipPorMm - (p.InicioDip + modulos * p.ModuloDip) >= silencio);
    }

    [Fact]
    public void Codigo_comprido_usa_a_margem_da_etiqueta_para_ficar_com_3_pontos()
    {
        Assert.Equal(3, NaTag(112).PontosPorModulo);   // 14 números
        Assert.Equal(3, NaTag(95, silencio: 11).PontosPorModulo); // EAN-13
        Assert.Equal(2, NaTag(123).PontosPorModulo);   // com 3 pontos não sobraria margem branca
    }

    [Fact]
    public void Codigo_curto_nao_passa_de_meio_milimetro_por_modulo() =>
        Assert.True(NaTag(35).PontosPorModulo <= 4);

    [Fact]
    public void Terminar_em_numeros_encurta_o_Code128_com_letras()
    {
        Assert.Equal(112, Code128.Modulos("BRM10110").Count);
        Assert.Equal(new[] { Code128.InicioB, 'B' - ' ', 'R' - ' ', 'M' - ' ', '1' - ' ', Code128.TrocaParaC, 1, 10 },
            Code128.Simbolos("BRM10110").Take(8));
        Assert.Equal(Code128.InicioB, Code128.Simbolos("1509X")[0]);
    }
}
