using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Services;

namespace NexusLS.App.Etiquetas;

/// <summary>
/// Desenha UMA etiqueta (nome, preço, tamanho e código de barras) numa área de largura × altura (em unidades do WPF:
/// 96 por polegada). O mesmo desenho serve para a prévia na tela e para a impressora.
/// </summary>
internal static class EtiquetaDesenho
{
    public const double DipPorMm = 96.0 / 25.4;

    public static double Dip(double milimetros) => milimetros * DipPorMm;

    private static readonly Typeface Normal = new("Segoe UI");
    private static readonly Typeface Negrito =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static void Desenhar(DrawingContext dc, ItemEtiqueta item, double largura, double altura)
    {
        var margem = largura * 0.05;
        var util = largura - 2 * margem;

        // Nome do produto: até 2 linhas, com reticências se não couber.
        var tamanhoNome = Math.Clamp(altura * 0.115, 7, 14);
        var nome = Texto(item.Nome, Negrito, tamanhoNome, util);
        nome.MaxTextHeight = tamanhoNome * 2.5;
        nome.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(nome, new Point(margem, margem * 0.5));

        // Tamanho: numa caixinha à direita da linha do preço.
        var larguraCaixa = 0.0;
        var yPreco = altura * 0.31;
        if (!string.IsNullOrEmpty(item.Tamanho))
        {
            var tam = Texto(item.Tamanho, Negrito, Math.Clamp(altura * 0.2, 9, 26), util * 0.4);
            tam.Trimming = TextTrimming.CharacterEllipsis;
            larguraCaixa = tam.Width + 12;
            var caixa = new Rect(largura - margem - larguraCaixa, yPreco, larguraCaixa, tam.Height + 4);
            dc.DrawRoundedRectangle(null, new Pen(Brushes.Black, 1.2), caixa, 3, 3);
            dc.DrawText(tam, new Point(caixa.X + 6, caixa.Y + 2));
        }

        // Preço grande; se não couber ao lado da caixa do tamanho, encolhe até caber.
        var textoPreco = Dinheiro.Formatar(item.PrecoCentavos).Replace(' ', ' ');
        var tamanhoPreco = Math.Clamp(altura * 0.26, 10, 40);
        var larguraPreco = util - larguraCaixa - (larguraCaixa > 0 ? 6 : 0);
        var preco = Texto(textoPreco, Negrito, tamanhoPreco, null);
        while (preco.Width > larguraPreco && tamanhoPreco > 7)
        {
            tamanhoPreco -= 1;
            preco = Texto(textoPreco, Negrito, tamanhoPreco, null);
        }
        dc.DrawText(preco, new Point(margem, yPreco - 2));

        // Código de barras (só se o código tiver caracteres que o Code 128 aceita).
        if (Code128.PodeCodificar(item.CodigoBarras))
            DesenharCodigo(dc, item.CodigoBarras!, margem, largura, altura);
    }

    private static void DesenharCodigo(DrawingContext dc, string codigo, double margem, double largura, double altura)
    {
        var modulos = Code128.Modulos(codigo);
        var silencio = 10; // módulos brancos de cada lado, exigidos para o leitor achar o começo e o fim
        var larguraModulo = Math.Max((largura - 2 * margem) / (modulos.Count + 2 * silencio), 0.5);
        var x0 = (largura - modulos.Count * larguraModulo) / 2;
        var y0 = altura * 0.62;
        var alturaBarras = altura * 0.22;

        for (var i = 0; i < modulos.Count;)
        {
            if (!modulos[i]) { i++; continue; }
            var fim = i;
            while (fim < modulos.Count && modulos[fim]) fim++;   // junta módulos pretos seguidos numa barra só
            dc.DrawRectangle(Brushes.Black, null, new Rect(x0 + i * larguraModulo, y0, (fim - i) * larguraModulo, alturaBarras));
            i = fim;
        }

        var tamanhoTexto = Math.Clamp(altura * 0.075, 5, 10);
        var texto = Texto(codigo, Normal, tamanhoTexto, largura - 2 * margem);
        texto.TextAlignment = TextAlignment.Center;
        dc.DrawText(texto, new Point(margem, y0 + alturaBarras + 1));
    }

    private static FormattedText Texto(string texto, Typeface fonte, double tamanho, double? larguraMaxima)
    {
        var t = new FormattedText(texto, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight,
            fonte, tamanho, Brushes.Black, 1.0);
        if (larguraMaxima is double max) t.MaxTextWidth = Math.Max(max, 1);
        return t;
    }
}
