using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.BarTender.ViewModels;
using NexusLS.Core.Models;
using Xunit;

namespace NexusLS.App.Tests;

/// <summary>Modelos prontos de tag de roupa 50 × 75 mm.</summary>
public sealed class TagRoupaTests
{
    // PNG de 1 × 1, só para servir de "logotipo".
    private const string Logo = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static readonly Func<ModeloEtiquetaDesign>[] Fabricas =
    {
        ModeloEtiquetaDesign.CriarTagRoupaSemPreco50x75,
        ModeloEtiquetaDesign.CriarTagRoupaComPreco50x75,
        ModeloEtiquetaDesign.CriarTagRoupaCompacta50x75,
    };

    private static Produto Camisa(string? referencia = "1104") => new()
    {
        Id = 990058,
        Nome = "CAMISA DE BOTÃO MANGA CURTA TECIDO PREMIUM IMPORTADO",
        Referencia = referencia,
        CodigoBarras = "2000000000169",
        PrecoVendaCentavos = 129990,
        Tamanhos = new List<ProdutoTamanho> { new() { Tamanho = "G10", Ordem = 1 } }
    };

    [Fact]
    public void As_tres_variantes_estao_no_catalogo_de_modelos_prontos()
    {
        var tags = PredefinicaoEtiqueta.ObterCatalogoCompleto()
            .Where(p => p.Nome.StartsWith("Tag Vestuário 50 × 75 mm", StringComparison.Ordinal))
            .Select(p => p.Fabrica())
            .ToList();

        Assert.Equal(3, tags.Count);
        Assert.All(tags, m => Assert.Equal((50.0, 75.0), (m.LarguraMm, m.AlturaMm)));
        Assert.All(tags, m => Assert.Contains(m.Elementos, e => e.Variavel == VariavelEtiqueta.Tamanho));
        Assert.All(tags, m => Assert.Contains(m.Elementos, e => e.Tipo == TipoElemento.CodigoBarras && e.Altura >= 19));
        Assert.Single(tags, m => m.Elementos.Any(e => e.Variavel == VariavelEtiqueta.PrecoVarejo));
    }

    [Fact]
    public void Logotipo_substitui_o_nome_da_loja_e_sem_logotipo_o_nome_fica()
    {
        var semLogo = ModeloEtiquetaDesign.CriarTagRoupaSemPreco50x75().AplicarLogo(null);
        Assert.DoesNotContain(semLogo.Elementos, e => e.Tipo == TipoElemento.Imagem);
        Assert.Contains(semLogo.Elementos, e => e.Variavel == VariavelEtiqueta.NomeLoja);

        var comLogo = ModeloEtiquetaDesign.CriarTagRoupaSemPreco50x75().AplicarLogo(Logo);
        var marca = Assert.Single(comLogo.Elementos, e => e.Nome == ModeloEtiquetaDesign.NomeCamadaMarca);
        Assert.Equal(TipoElemento.Imagem, marca.Tipo);
        Assert.Equal(Logo, marca.ImagemBase64);
        Assert.Equal(Logo, PadronizarEtiquetaViewModel.LogoDaLoja(new[] { null, comLogo }));
    }

    [Fact]
    public void Padronizar_etiqueta_oferece_as_tags_com_o_logotipo_do_estudio()
    {
        var estudio = ModeloEtiquetaDesign.CriarTagRoupaSemPreco50x75().AplicarLogo(Logo);
        var vm = new PadronizarEtiquetaViewModel(estudio, new[] { Camisa() }, 1, Camisa(), "LOJA");

        var prontas = vm.OpcoesModelos
            .Where(o => !o.EhDoUsuario && o.Titulo.StartsWith("Tag Vestuário 50 × 75", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, prontas.Count);
        Assert.All(prontas, o => Assert.Contains(o.Modelo.Elementos, e => e.Tipo == TipoElemento.Imagem));
    }

    [Fact]
    public void Padronizar_para_a_grade_nao_muda_os_campos_da_tag()
    {
        foreach (var fabrica in Fabricas)
        {
            var modelo = fabrica();
            var antes = modelo.Elementos.Select(e => (e.Variavel, e.Texto, e.X, e.Largura)).ToList();

            DesignerViewModel.PadronizarModeloParaGrade(modelo, Camisa(), new[] { "G10", "42" });

            Assert.Equal(antes, modelo.Elementos.Select(e => (e.Variavel, e.Texto, e.X, e.Largura)).ToList());
        }
    }

    [Fact]
    public void Referencia_sai_com_rotulo_e_produto_sem_referencia_nao_deixa_rotulo_solto()
    {
        var campo = ModeloEtiquetaDesign.CriarTagRoupaSemPreco50x75().Elementos
            .Single(e => e.Variavel == VariavelEtiqueta.Referencia);

        Assert.Equal("REF. 1104", EditorDesenhoService.ResolverTexto(campo, Camisa(), "G10", "LOJA"));
        Assert.Equal("", EditorDesenhoService.ResolverTexto(campo, Camisa(referencia: null), "G10", "LOJA"));

        // Modelos antigos, só com "{Ref}", continuam iguais.
        campo.Texto = "{Ref}";
        Assert.Equal("1104", EditorDesenhoService.ResolverTexto(campo, Camisa(), "G10", "LOJA"));
    }

    [Theory]
    [InlineData(75)]
    [InlineData(63)] // "Descer a impressão" em 12 mm
    public void Nada_e_impresso_no_furo_nem_perto_das_bordas(int alturaMm)
    {
        Exception? erro = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var fabrica in Fabricas)
                {
                    var modelo = ModeloMidiaService.Ajustar(fabrica(), 50, alturaMm).Modelo;
                    var (esquerda, topo, direita, pe) = AreaImpressaMm(modelo, Camisa());
                    var escala = alturaMm / 75.0;

                    Assert.True(topo >= 10 * escala, $"{modelo.Nome}: topo {topo:0.0} mm");
                    Assert.True(esquerda >= 3.5, $"{modelo.Nome}: esquerda {esquerda:0.0} mm");
                    Assert.True(direita <= 46.5, $"{modelo.Nome}: direita {direita:0.0} mm");
                    Assert.True(pe <= alturaMm - 3, $"{modelo.Nome}: pé {pe:0.0} mm");
                }
            }
            catch (Exception ex) { erro = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (erro is not null) throw new InvalidOperationException(erro.Message, erro);
    }

    /// <summary>Retângulo, em mm, que contém tudo o que sai preto na etiqueta.</summary>
    private static (double, double, double, double) AreaImpressaMm(ModeloEtiquetaDesign modelo, Produto produto)
    {
        const double pixelsPorMm = 8;
        var bmp = EditorDesenhoService.RenderizarBitmap(modelo, produto, "G10", "LOJA",
            escalaDpi: pixelsPorMm * 25.4 / 96);
        var cinza = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
        var pixels = new byte[cinza.PixelWidth * cinza.PixelHeight];
        cinza.CopyPixels(pixels, cinza.PixelWidth, 0);

        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            int x = i % cinza.PixelWidth, y = i / cinza.PixelWidth;
            // O arredondamento da imagem pode sobrar uma linha transparente (lida como preta) fora da etiqueta.
            if (pixels[i] >= 128 || x >= (int)(modelo.LarguraMm * pixelsPorMm) - 1 || y >= (int)(modelo.AlturaMm * pixelsPorMm) - 1)
                continue;
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }

        return (x0 / pixelsPorMm, y0 / pixelsPorMm, (x1 + 1) / pixelsPorMm, (y1 + 1) / pixelsPorMm);
    }
}
