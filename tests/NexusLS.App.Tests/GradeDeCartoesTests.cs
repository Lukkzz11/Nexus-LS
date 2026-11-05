using System.Windows;
using System.Windows.Controls;
using NexusLS.App.Views;

namespace NexusLS.App.Tests;

/// <summary>O painel que reparte os cartões na largura toda: quantas colunas cabem e onde cada cartão fica.</summary>
public class GradeDeCartoesTests
{
    [Theory]
    [InlineData(1860, 380, 16, 3, 5, 3)]   // monitor largo: o máximo de colunas
    [InlineData(826, 380, 16, 3, 5, 2)]    // janela de ~1100 px (sem o menu lateral): duas colunas
    [InlineData(600, 380, 16, 3, 5, 1)]    // estreito: uma coluna
    [InlineData(1860, 380, 16, 3, 2, 2)]   // nunca mais colunas do que cartões
    [InlineData(1860, 380, 16, 3, 0, 1)]
    [InlineData(double.PositiveInfinity, 380, 16, 3, 5, 3)]
    public void Calcula_quantas_colunas_cabem(double largura, double minima, double espaco, int maximo, int visiveis, int esperado) =>
        Assert.Equal(esperado, GradeDeCartoes.ColunasPara(largura, minima, espaco, maximo, visiveis));

    private static List<Rect> Arrumar(GradeDeCartoes grade, double largura, params double[] alturas)
    {
        var cartoes = alturas.Select(a => new Border { Height = a }).ToList();
        foreach (var c in cartoes) grade.Children.Add(c);
        grade.Measure(new Size(largura, double.PositiveInfinity));
        grade.Arrange(new Rect(0, 0, largura, grade.DesiredSize.Height));
        return cartoes.Select(c => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(c)).ToList();
    }

    [Fact]
    public void Cartoes_ocupam_a_largura_toda_e_cada_um_entra_na_coluna_mais_curta()
    {
        List<Rect>? r = null;
        double altura = 0;
        Capturador.Rodar(() =>
        {
            var grade = new GradeDeCartoes { LarguraMinima = 300, MaximoColunas = 2, Espaco = 20 };
            r = Arrumar(grade, 1000, 100, 300, 50);
            altura = grade.DesiredSize.Height;
        });

        Assert.Equal(new Rect(0, 0, 490, 100), r![0]);
        Assert.Equal(new Rect(510, 0, 490, 300), r[1]);
        Assert.Equal(new Rect(0, 120, 490, 50), r[2]);   // a coluna da esquerda era a mais curta
        Assert.Equal(300, altura);
    }

    [Fact]
    public void Com_igualar_altura_os_cartoes_ficam_em_linhas_da_mesma_altura()
    {
        List<Rect>? r = null;
        Capturador.Rodar(() =>
        {
            var grade = new GradeDeCartoes { LarguraMinima = 200, MaximoColunas = 2, Espaco = 10, IgualarAltura = true };
            r = Arrumar(grade, 410, 40, 80, 60);
        });

        Assert.Equal(new Rect(0, 0, 200, 80), r![0]);
        Assert.Equal(new Rect(210, 0, 200, 80), r[1]);
        Assert.Equal(new Rect(0, 90, 200, 60), r[2]);
    }

    [Fact]
    public void Cartao_escondido_nao_ocupa_coluna()
    {
        List<Rect>? r = null;
        Capturador.Rodar(() =>
        {
            var grade = new GradeDeCartoes { LarguraMinima = 300, MaximoColunas = 3, Espaco = 0 };
            var escondido = new Border { Height = 100, Visibility = Visibility.Collapsed };
            grade.Children.Add(escondido);
            r = Arrumar(grade, 900, 100, 100);
        });

        Assert.Equal(450, r![0].Width);   // dois cartões visíveis: duas colunas, não três
        Assert.Equal(450, r[1].X);
    }
}
