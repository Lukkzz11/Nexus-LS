using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexusLS.App.Impressao;

/// <summary>Converte o logo (PNG, com ou sem transparência) em JPEG sobre fundo branco, o formato que o PDF do Nexus embute.</summary>
public static class LogoParaPdf
{
    public static byte[]? Converter(byte[] png)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var origem = decoder.Frames[0];
            var escala = Math.Min(1.0, 400.0 / Math.Max(origem.PixelWidth, 1));
            var w = Math.Max(1, (int)(origem.PixelWidth * escala));
            var h = Math.Max(1, (int)(origem.PixelHeight * escala));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawImage(origem, new Rect(0, 0, w, h));
            }
            var alvo = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            alvo.Render(visual);
            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(alvo));
            using var saida = new MemoryStream();
            encoder.Save(saida);
            return saida.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
