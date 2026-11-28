using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using ZXing;

namespace NexusLS.App.Tests;

/// <summary>Imprime a página da tag em 203 dpi (como a térmica) e lê o código de volta.</summary>
public class CodigoDeBarrasImpressoTests
{
    private const int Dpi = 203;

    [Theory]
    [InlineData("14021", 1, 3, BarcodeFormat.CODE_128)]
    [InlineData("14021", 2, 3, BarcodeFormat.CODE_128)]
    [InlineData("2000000000138", 1, 3, BarcodeFormat.EAN_13)]     // dígito certo: EAN-13, barras mais grossas
    [InlineData("1000000016024", 2, 3, BarcodeFormat.EAN_13)]
    [InlineData("20000000001531", 2, 3, BarcodeFormat.CODE_128)]  // 14 números
    [InlineData("0000000000022", 1, 2, BarcodeFormat.CODE_128)]   // dígito errado: não muda o código
    [InlineData("1509X", 1, 3, BarcodeFormat.CODE_128)]
    [InlineData("BRM10110", 2, 3, BarcodeFormat.CODE_128)]
    public void Barras_saem_em_pontos_inteiros_e_o_leitor_le(string codigo, int coluna, int pontosPorModulo, BarcodeFormat formato)
    {
        Resultado.Verificar(codigo, coluna, pontosPorModulo, formato, rotacao: 0);
    }

    [Fact]
    public void Codigo_de_cabeca_para_baixo_continua_legivel() =>
        Resultado.Verificar("14021", 1, 3, BarcodeFormat.CODE_128, rotacao: 180);

    private static class Resultado
    {
        public static void Verificar(string codigo, int coluna, int pontosPorModulo, BarcodeFormat formato, double rotacao)
        {
            string? lido = null;
            BarcodeFormat? lidoComo = null;
            var larguras = new List<int>();

            Exception? erro = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var modelo = new ModeloEtiquetaDesign { LarguraMm = 50, AlturaMm = 75 };
                    modelo.Elementos.Add(new ElementoDesign
                    {
                        Tipo = TipoElemento.CodigoBarras, TipoCodigo = TipoSimboloCodigo.Code128, ValorCodigo = codigo,
                        X = 6.25, Y = 45, Largura = 37.5, Altura = 18.75, Rotacao = rotacao
                    });

                    var paginador = new BarTenderMidiaPaginator(
                        new[] { new ItemFila(new Produto { Nome = "Teste" }) },
                        modelo, "LOJA", PerfisMidia.RoloTagRoupas2Colunas50x75, coluna);

                    int largura = 799, altura = 599;
                    var imagem = new RenderTargetBitmap(largura, altura, Dpi, Dpi, PixelFormats.Pbgra32);
                    imagem.Render(paginador.GetPage(0).Visual);
                    var pixels = new byte[largura * altura * 4];
                    imagem.CopyPixels(pixels, largura * 4, 0);

                    // faixa das barras, só a coluna usada
                    int x0 = (coluna - 1) * 399, w = 399, y0 = (int)(47 / 25.4 * Dpi), h = (int)(10 / 25.4 * Dpi);
                    var cinza = new byte[w * h];
                    for (var y = 0; y < h; y++)
                        for (var x = 0; x < w; x++)
                            cinza[y * w + x] = pixels[((y0 + y) * largura + x0 + x) * 4];

                    // nenhum cinza: cada ponto da impressora é todo preto ou todo branco
                    Assert.All(cinza, v => Assert.True(v < 8 || v > 247, $"borda cinza ({v})"));

                    var anterior = -1;
                    for (var x = 0; x < w; x++)
                    {
                        var preto = cinza[(h / 2) * w + x] < 128;
                        if (preto && anterior < 0) anterior = x;
                        else if (!preto && anterior >= 0) { larguras.Add(x - anterior); anterior = -1; }
                    }

                    var leitor = new MultiFormatReader();
                    var resultado = leitor.decode(new BinaryBitmap(new ZXing.Common.HybridBinarizer(
                            new RGBLuminanceSource(cinza, w, h, RGBLuminanceSource.BitmapFormat.Gray8))),
                        new Dictionary<DecodeHintType, object>
                        {
                            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.CODE_128, BarcodeFormat.EAN_13 }
                        });
                    lido = resultado?.Text;
                    lidoComo = resultado?.BarcodeFormat;
                }
                catch (Exception ex) { erro = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(erro);
            Assert.Equal(codigo, lido);
            Assert.Equal(formato, lidoComo);
            Assert.NotEmpty(larguras);
            Assert.All(larguras, l => Assert.Equal(0, l % pontosPorModulo));
            Assert.Equal(pontosPorModulo, larguras.Min());
        }
    }
}
