using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.Core.Services;

namespace NexusLS.App.Imagens;

/// <summary>Reduz e recomprime imagens com o próprio WPF (sem biblioteca extra): o maior lado fica limitado e a saída é JPEG.</summary>
public sealed class ProcessadorImagemWpf : IProcessadorImagem
{
    public byte[] ReduzirParaJpeg(byte[] original, int maiorLado)
    {
        BitmapSource origem;
        try
        {
            var quadro = BitmapDecoder.Create(new MemoryStream(original), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            origem = quadro;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            throw new RegraDeNegocioException("Esse arquivo não parece ser uma imagem (use JPG, PNG, BMP ou GIF).");
        }

        var maior = Math.Max(origem.PixelWidth, origem.PixelHeight);
        if (maior > maiorLado)
        {
            var escala = (double)maiorLado / maior;
            origem = new TransformedBitmap(origem, new ScaleTransform(escala, escala));
        }
        // JPEG não tem transparência: fundo branco para PNG com transparência.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, origem.PixelWidth, origem.PixelHeight));
            dc.DrawImage(origem, new System.Windows.Rect(0, 0, origem.PixelWidth, origem.PixelHeight));
        }
        var alvo = new RenderTargetBitmap(origem.PixelWidth, origem.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        alvo.Render(visual);

        var codificador = new JpegBitmapEncoder { QualityLevel = 82 };
        codificador.Frames.Add(BitmapFrame.Create(alvo));
        using var saida = new MemoryStream();
        codificador.Save(saida);
        return saida.ToArray();
    }

    /// <summary>Carrega uma imagem do disco sem travar o arquivo (para poder trocar/apagar depois).</summary>
    public static ImageSource? Carregar(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho) || !File.Exists(caminho)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            img.UriSource = new Uri(caminho);
            img.DecodePixelWidth = 400;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException) { return null; }
    }
}
