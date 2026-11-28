using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models.Designer;
using ZXing;
using ZXing.Common;

namespace NexusLS.BarTender.Services;

public static class BarcodeGeneratorService
{
    public static BitmapSource? Gerar(string conteudo, TipoSimboloCodigo tipo, int larguraPx, int alturaPx, int margem = 1)
    {
        if (string.IsNullOrWhiteSpace(conteudo))
        {
            conteudo = "0000000000000";
        }

        larguraPx = Math.Max(20, larguraPx);
        alturaPx = Math.Max(20, alturaPx);

        var formato = tipo switch
        {
            TipoSimboloCodigo.Code128 => BarcodeFormat.CODE_128,
            TipoSimboloCodigo.Ean13 => BarcodeFormat.EAN_13,
            TipoSimboloCodigo.QrCode => BarcodeFormat.QR_CODE,
            TipoSimboloCodigo.Code39 => BarcodeFormat.CODE_39,
            _ => BarcodeFormat.CODE_128
        };

        var textoAjustado = conteudo.Trim();

        // O que não serve como EAN-13 (14 dígitos, 5 dígitos, dígito que não confere) sai em Code 128:
        // cortar, completar com zeros ou recalcular o dígito mudaria o código que o PDV conhece.
        if (formato == BarcodeFormat.EAN_13 && !BarrasVetoriais.ServeComoEan13(textoAjustado))
        {
            formato = BarcodeFormat.CODE_128;
        }

        try
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = formato,
                Options = new EncodingOptions
                {
                    Width = larguraPx,
                    Height = alturaPx,
                    Margin = margem,
                    PureBarcode = true // Desenhamos o texto legível separadamente para controle de tipografia e alinhamento
                }
            };

            var pixelData = writer.Write(textoAjustado);
            var bmp = BitmapSource.Create(
                pixelData.Width,
                pixelData.Height,
                96, 96,
                PixelFormats.Bgr32,
                null,
                pixelData.Pixels,
                pixelData.Width * 4);

            bmp.Freeze();
            return bmp;
        }
        catch
        {
            // Fallback para Code 128 caso o formato selecionado não aceite os caracteres
            try
            {
                var writerFallback = new BarcodeWriterPixelData
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions
                    {
                        Width = larguraPx,
                        Height = alturaPx,
                        Margin = margem,
                        PureBarcode = true
                    }
                };
                var pixelData = writerFallback.Write(conteudo);
                var bmp = BitmapSource.Create(
                    pixelData.Width,
                    pixelData.Height,
                    96, 96,
                    PixelFormats.Bgr32,
                    null,
                    pixelData.Pixels,
                    pixelData.Width * 4);
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }

}
