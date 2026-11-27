using NexusLS.App.Impressao;
using NexusLS.Core.Services;

namespace NexusLS.App.Tests;

public sealed class ComprovantePresenteTests
{
    [Fact]
    public void Presente_nao_mostra_precos_e_o_QR_le_o_numero_da_venda()
    {
        var venda = MontadorCupom.VendaDeExemplo();
        var texto = MontadorCupom.MontarPresente(venda, new NexusLS.Core.Models.ConfiguracaoLoja { NomeLoja = "Loja" });

        Assert.Contains("PARA PRESENTE", texto);
        Assert.Contains("Troca nº 123", texto);
        Assert.DoesNotContain("R$", texto);

        var imagem = ImpressoraWindows.QrCode(MontadorCupom.CodigoDeTroca(venda));
        var pontos = new byte[imagem.PixelWidth * imagem.PixelHeight];
        imagem.CopyPixels(pontos, imagem.PixelWidth, 0);
        var fonte = new ZXing.RGBLuminanceSource(pontos, imagem.PixelWidth, imagem.PixelHeight, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
        var lido = new ZXing.QrCode.QRCodeReader().decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(fonte)));

        Assert.Equal("123", lido?.Text);
    }
}
