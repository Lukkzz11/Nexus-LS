using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models.Designer;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Services;

/// <summary>Salva só os códigos de barras (com o número embaixo), uma imagem PNG por produto e tamanho.</summary>
public static class ExportadorCodigosDeBarras
{
    private const int Largura = 600, Altura = 260;

    public static int Exportar(IEnumerable<Produto> produtos, string pasta)
    {
        Directory.CreateDirectory(pasta);
        var salvos = 0;

        foreach (var produto in produtos)
        {
            var tamanhos = (produto.Tamanhos ?? new List<ProdutoTamanho>())
                .Where(t => !t.EhSemTamanho)
                .OrderBy(t => t.Ordem)
                .Select(t => t.Tamanho)
                .DefaultIfEmpty("")
                .ToList();

            foreach (var tamanho in tamanhos)
            {
                var codigo = CodigoBarrasTamanho.Resolver(produto, tamanho);
                if (string.IsNullOrWhiteSpace(codigo)) continue;

                var imagem = Desenhar(codigo);
                if (imagem is null) continue;

                var nome = Limpar(string.Join(" ", new[] { produto.Referencia, produto.Nome, tamanho, codigo }
                    .Where(p => !string.IsNullOrWhiteSpace(p))));

                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(imagem));
                using var arquivo = File.Create(Path.Combine(pasta, nome + ".png"));
                png.Save(arquivo);
                salvos++;
            }
        }

        return salvos;
    }

    private static BitmapSource? Desenhar(string codigo)
    {
        // EAN-13 só quando o dígito verificador confere: o gerador recalcularia o dígito e mudaria o código.
        var tipo = CodigoBarrasInterno.EhEan13Valido(codigo) ? TipoSimboloCodigo.Ean13 : TipoSimboloCodigo.Code128;
        var barras = BarcodeGeneratorService.Gerar(codigo, tipo, Largura - 40, Altura - 70, margem: 10);
        if (barras is null) return null;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Largura, Altura));
            var largura = Math.Min(barras.PixelWidth, Largura - 20);
            dc.DrawImage(barras, new Rect((Largura - largura) / 2.0, 15, largura, Altura - 70));

            var texto = new FormattedText(codigo, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 30, Brushes.Black, 1.0);
            dc.DrawText(texto, new Point((Largura - texto.Width) / 2, Altura - 48));
        }

        var alvo = new RenderTargetBitmap(Largura, Altura, 96, 96, PixelFormats.Pbgra32);
        alvo.Render(visual);
        alvo.Freeze();
        return alvo;
    }

    private static string Limpar(string nome)
    {
        var limpo = string.Concat(nome.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
        return limpo.Length > 120 ? limpo[^120..] : limpo;
    }
}
