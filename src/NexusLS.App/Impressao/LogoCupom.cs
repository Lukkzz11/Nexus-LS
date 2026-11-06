using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.Core.Services;

namespace NexusLS.App.Impressao;

/// <summary>
/// Prepara e desenha o logo do cupom. Ao enviar, a imagem é reduzida (no máximo 384 x 160 pontos), colocada sobre
/// fundo branco e guardada como PNG. Na prévia e na impressão ela vai em tons de cinza, que é como a térmica imprime
/// (o ERP faz o mesmo com um filtro de cinza). A impressão passa pelo driver do Windows, como o texto do cupom.
/// </summary>
public static class LogoCupom
{
    public const int LarguraMaxima = 384;
    public const int AlturaMaxima = 160;

    /// <summary>Lê uma imagem (PNG, JPG, BMP...) e devolve um PNG pequeno. Falha em português se não for imagem.</summary>
    public static byte[] Preparar(byte[] arquivo)
    {
        BitmapSource origem;
        try
        {
            using var fluxo = new MemoryStream(arquivo);
            var decodificador = BitmapDecoder.Create(fluxo, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            origem = decodificador.Frames[0];
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            throw new RegraDeNegocioException("Não consegui abrir esta imagem. Escolha um arquivo PNG, JPG ou BMP.");
        }

        var escala = Math.Min(1.0, Math.Min((double)LarguraMaxima / origem.PixelWidth, (double)AlturaMaxima / origem.PixelHeight));
        var largura = Math.Max(1, (int)Math.Round(origem.PixelWidth * escala));
        var altura = Math.Max(1, (int)Math.Round(origem.PixelHeight * escala));

        var desenho = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(desenho, BitmapScalingMode.HighQuality);
        using (var g = desenho.RenderOpen())
        {
            g.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, largura, altura)); // transparência vira branco
            g.DrawImage(origem, new System.Windows.Rect(0, 0, largura, altura));
        }
        var bitmap = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(desenho);
        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(bitmap));
        using var saida = new MemoryStream();
        codificador.Save(saida);
        return saida.ToArray();
    }

    /// <summary>A imagem em tons de cinza, como sai na impressora. Nulo se não houver logo.</summary>
    public static BitmapSource? EmCinza(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        using var fluxo = new MemoryStream(png);
        var quadro = BitmapDecoder.Create(fluxo, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        // Transparência vira branco (senão o cinza a pintaria de preto) e só então vai para tons de cinza.
        var desenho = new DrawingVisual();
        using (var g = desenho.RenderOpen())
        {
            g.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, quadro.PixelWidth, quadro.PixelHeight));
            g.DrawImage(quadro, new System.Windows.Rect(0, 0, quadro.PixelWidth, quadro.PixelHeight));
        }
        var sobreBranco = new RenderTargetBitmap(quadro.PixelWidth, quadro.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        sobreBranco.Render(desenho);
        var cinza = new FormatConvertedBitmap(sobreBranco, PixelFormats.Gray8, null, 0);
        cinza.Freeze();
        return cinza;
    }
}
