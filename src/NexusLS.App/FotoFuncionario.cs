using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.Core.Services;

namespace NexusLS.App;

/// <summary>Foto do funcionário: reduz qualquer imagem para um JPEG pequeno (cabe no banco e no backup) e converte de volta para mostrar.</summary>
public static class FotoFuncionario
{
    public const int LadoMaximo = 320;

    public static byte[] Preparar(byte[] arquivo)
    {
        try
        {
            var original = new BitmapImage();
            using (var entrada = new MemoryStream(arquivo))
            {
                original.BeginInit();
                original.CacheOption = BitmapCacheOption.OnLoad;
                original.StreamSource = entrada;
                original.EndInit();
            }
            original.Freeze();

            BitmapSource final = original;
            var maior = Math.Max(original.PixelWidth, original.PixelHeight);
            if (maior > LadoMaximo)
            {
                var escala = (double)LadoMaximo / maior;
                var reduzida = new TransformedBitmap(original, new ScaleTransform(escala, escala));
                reduzida.Freeze();
                final = reduzida;
            }

            var codificador = new JpegBitmapEncoder { QualityLevel = 85 };
            codificador.Frames.Add(BitmapFrame.Create(final));
            using var saida = new MemoryStream();
            codificador.Save(saida);
            return saida.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            throw new RegraDeNegocioException("Não consegui abrir essa imagem. Escolha um arquivo JPG ou PNG.");
        }
    }

    public static BitmapSource? ParaImagem(byte[]? foto)
    {
        if (foto is not { Length: > 0 }) return null;
        try
        {
            var img = new BitmapImage();
            using var entrada = new MemoryStream(foto);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = entrada;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
