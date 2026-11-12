using System.Globalization;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

/// <summary>
/// Os números que aparecem nos avisos são texto para o operador e precisam sair sempre em português,
/// mesmo num Windows configurado em inglês (era o que deixava o CI vermelho: "0.25" em vez de "0,25").
/// </summary>
public class TextoBrTests
{
    [Fact]
    public void Numero_usa_virgula_decimal_e_ponto_de_milhar_mesmo_com_windows_em_ingles()
    {
        using var _ = new CulturaEmIngles();

        Assert.Equal("1.234,50", TextoBr.Numero(1234.5m));
        Assert.Equal("0,25", TextoBr.Numero(0.25, "0.00"));
        Assert.Equal("4,5", TextoBr.Numero(4.5, "0.0"));
    }

    [Fact]
    public void Aviso_de_codigo_de_barras_sai_em_portugues_com_windows_em_ingles()
    {
        using var _ = new CulturaEmIngles();

        var erros = CodigoBarrasTamanho.Validar("55551", 20);

        Assert.Contains(erros, e => e.Contains("0,25"));
        Assert.DoesNotContain(erros, e => e.Contains("0.25"));
    }

    /// <summary>Troca a cultura da thread atual e devolve a original ao sair, sem afetar o resto da suíte.</summary>
    private sealed class CulturaEmIngles : IDisposable
    {
        private readonly CultureInfo _anterior = CultureInfo.CurrentCulture;

        public CulturaEmIngles() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        public void Dispose() => CultureInfo.CurrentCulture = _anterior;
    }
}
