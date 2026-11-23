using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NexusLS.Core.Etiquetas;

namespace NexusLS.BarTender.Views;

/// <summary>
/// Desenho esquemático do formato de mídia e do sensor da impressora (rolo com gap,
/// rolo com marca preta, rolo contínuo ou folha A4 3×7) para facilitar a visualização na tela de preparação.
/// </summary>
public sealed class IlustracaoMidia : FrameworkElement
{
    public static readonly DependencyProperty MidiaProperty = DependencyProperty.Register(
        nameof(Midia),
        typeof(PerfilMidia),
        typeof(IlustracaoMidia),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TipoProperty = DependencyProperty.Register(
        nameof(Tipo),
        typeof(TipoMidiaEtiqueta),
        typeof(IlustracaoMidia),
        new FrameworkPropertyMetadata(TipoMidiaEtiqueta.RoloComGap, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush FundoBase = Congelar(new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)));
    private static readonly Pen BordaBase = CongelarPen(new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), 1.0);
    private static readonly Brush LinerRolo = Congelar(new SolidColorBrush(Color.FromRgb(0xE0, 0xE7, 0xFF)));
    private static readonly Pen BordaLiner = CongelarPen(new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)), 1.0);
    private static readonly Brush PapelEtiqueta = Brushes.White;
    private static readonly Pen BordaEtiqueta = CongelarPen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.2);
    private static readonly Brush MarcaPretaBrush = Congelar(new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)));
    private static readonly Brush GapDestaqueBrush = Congelar(new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)));
    private static readonly Pen LinhaCortePen = CongelarPenTracejado(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.1);
    private static readonly Brush BarraTextoSimulado = Congelar(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)));
    private static readonly Brush BarraCodigoSimulado = Congelar(new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)));

    public PerfilMidia? Midia
    {
        get => (PerfilMidia?)GetValue(MidiaProperty);
        set => SetValue(MidiaProperty, value);
    }

    public TipoMidiaEtiqueta Tipo
    {
        get => (TipoMidiaEtiqueta)GetValue(TipoProperty);
        set => SetValue(TipoProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 72);
        var h = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 52);
        if (w <= 4 || h <= 4) return;

        dc.DrawRoundedRectangle(FundoBase, BordaBase, new Rect(0.5, 0.5, w - 1, h - 1), 6, 6);

        var midia = Midia;
        var tipo = midia?.Tipo ?? Tipo;
        var colunas = midia?.Colunas ?? 1;
        var largMm = midia?.LarguraEtiquetaMm ?? 50;
        var altMm = midia?.AlturaEtiquetaMm ?? 30;

        if (tipo == TipoMidiaEtiqueta.Folha)
        {
            DesenharFolhaA4(dc, w, h, midia?.Colunas ?? 3, midia?.Linhas ?? 7);
            return;
        }

        DesenharRolo(dc, w, h, tipo, colunas, largMm, altMm);
    }

    private static void DesenharFolhaA4(DrawingContext dc, double w, double h, int cols, int rows)
    {
        cols = Math.Clamp(cols, 1, 5);
        rows = Math.Clamp(rows, 1, 8);

        var folhaH = Math.Max(20, h - 10);
        var folhaW = Math.Min(w - 12, folhaH * (210.0 / 297.0));
        var x0 = (w - folhaW) / 2.0;
        var y0 = (h - folhaH) / 2.0;

        dc.DrawRectangle(PapelEtiqueta, BordaLiner, new Rect(x0, y0, folhaW, folhaH));

        var padX = 3.0;
        var padY = 3.5;
        var gapX = 1.5;
        var gapY = 1.2;

        var utilW = Math.Max(6, folhaW - (2 * padX) - ((cols - 1) * gapX));
        var utilH = Math.Max(6, folhaH - (2 * padY) - ((rows - 1) * gapY));
        var cellW = utilW / cols;
        var cellH = utilH / rows;

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var cx = x0 + padX + c * (cellW + gapX);
                var cy = y0 + padY + r * (cellH + gapY);
                dc.DrawRoundedRectangle(
                    r == 0 && c == 0 ? GapDestaqueBrush : PapelEtiqueta,
                    BordaEtiqueta,
                    new Rect(cx, cy, Math.Max(2, cellW), Math.Max(2, cellH)),
                    1.2, 1.2);
            }
        }
    }

    private static void DesenharRolo(
        DrawingContext dc,
        double w,
        double h,
        TipoMidiaEtiqueta tipo,
        int colunas,
        double largMm,
        double altMm)
    {
        colunas = Math.Clamp(colunas, 1, 3);
        var proporcao = altMm > 0 ? Math.Clamp(largMm / altMm, 0.7, 2.2) : 1.6;

        var maxLinerW = w - 14;
        var linerW = colunas > 1
            ? Math.Min(maxLinerW, Math.Max(34, h * 1.15))
            : Math.Min(maxLinerW, Math.Max(26, Math.Min(maxLinerW, 24 * proporcao)));

        var linerH = Math.Max(20, h - 8);
        var x0 = (w - linerW) / 2.0;
        var y0 = (h - linerH) / 2.0;

        // Fita base (liner) do rolo térmico
        dc.DrawRoundedRectangle(LinerRolo, BordaLiner, new Rect(x0, y0, linerW, linerH), 3, 3);

        if (tipo == TipoMidiaEtiqueta.RoloContinuo)
        {
            if (colunas > 1)
            {
                DesenharContinuoEmColunas(dc, x0, y0, linerW, linerH, colunas);
                return;
            }

            // Papel contínuo sem espaço entre etiquetas, com linhas tracejadas de corte
            var fitaRect = new Rect(x0 + 2.5, y0 + 2, Math.Max(8, linerW - 5), Math.Max(10, linerH - 4));
            dc.DrawRectangle(PapelEtiqueta, BordaEtiqueta, fitaRect);

            var yCorte1 = fitaRect.Top + (fitaRect.Height * 0.35);
            var yCorte2 = fitaRect.Top + (fitaRect.Height * 0.70);
            dc.DrawLine(LinhaCortePen, new Point(x0 + 1, yCorte1), new Point(x0 + linerW - 1, yCorte1));
            dc.DrawLine(LinhaCortePen, new Point(x0 + 1, yCorte2), new Point(x0 + linerW - 1, yCorte2));

            DesenharSimulacaoConteudo(dc, new Rect(fitaRect.Left + 2, fitaRect.Top + 2, fitaRect.Width - 4, (yCorte1 - fitaRect.Top) - 3));
            DesenharSimulacaoConteudo(dc, new Rect(fitaRect.Left + 2, yCorte1 + 2, fitaRect.Width - 4, (yCorte2 - yCorte1) - 3));
            return;
        }

        // Rolo com Gap ou Rolo com Marca Preta (2 etiquetas visíveis empilhadas na vertical)
        const int linhasVisiveis = 2;
        var padX = 2.5;
        var padY = 3.0;
        var gapY = tipo == TipoMidiaEtiqueta.RoloMarcaPreta ? 5.0 : 4.5;
        var gapX = colunas > 1 ? 2.5 : 0.0;

        var utilW = Math.Max(8, linerW - (2 * padX) - ((colunas - 1) * gapX));
        var utilH = Math.Max(8, linerH - (2 * padY) - ((linhasVisiveis - 1) * gapY));
        var etqW = utilW / colunas;
        var etqH = utilH / linhasVisiveis;

        for (var r = 0; r < linhasVisiveis; r++)
        {
            var ey = y0 + padY + r * (etqH + gapY);

            for (var c = 0; c < colunas; c++)
            {
                var ex = x0 + padX + c * (etqW + gapX);
                var rect = new Rect(ex, ey, Math.Max(4, etqW), Math.Max(4, etqH));
                dc.DrawRoundedRectangle(PapelEtiqueta, BordaEtiqueta, rect, 2.2, 2.2);

                if (colunas == 1)
                    DesenharSimulacaoConteudo(dc, rect);
            }

            if (r < linhasVisiveis - 1)
            {
                var yGapMeio = ey + etqH + (gapY / 2.0);
                if (tipo == TipoMidiaEtiqueta.RoloMarcaPreta)
                {
                    // Tarja preta (black mark) do sensor reflexivo
                    var tarjaH = Math.Min(3.2, gapY - 1.0);
                    dc.DrawRectangle(
                        MarcaPretaBrush,
                        null,
                        new Rect(x0 + 2, yGapMeio - (tarjaH / 2.0), Math.Max(6, linerW - 4), tarjaH));
                }
                else
                {
                    // Indicador visual do gap (espaço translúcido entre etiquetas)
                    dc.DrawRectangle(
                        GapDestaqueBrush,
                        null,
                        new Rect(x0 + 1, ey + etqH + 0.8, Math.Max(6, linerW - 2), Math.Max(1.5, gapY - 1.6)));
                }
            }
        }
    }

    /// <summary>
    /// Tag/rolo contínuo com mais de uma coluna: o papel é uma faixa única, cortada por linhas
    /// tracejadas na largura (colunas) e no comprimento (altura da etiqueta).
    /// </summary>
    private static void DesenharContinuoEmColunas(
        DrawingContext dc,
        double x0,
        double y0,
        double linerW,
        double linerH,
        int colunas)
    {
        const double padX = 2.5;
        const double padY = 2.0;
        var utilW = Math.Max(8, linerW - (2 * padX));
        var utilH = Math.Max(8, linerH - (2 * padY));
        var colW = utilW / colunas;
        var linhaH = utilH / 2.0;

        dc.DrawRectangle(PapelEtiqueta, BordaEtiqueta, new Rect(x0 + padX, y0 + padY, utilW, utilH));

        for (var c = 1; c < colunas; c++)
        {
            var xCorte = x0 + padX + (c * colW);
            dc.DrawLine(LinhaCortePen, new Point(xCorte, y0 + padY), new Point(xCorte, y0 + padY + utilH));
        }

        dc.DrawLine(
            LinhaCortePen,
            new Point(x0 + padX, y0 + padY + linhaH),
            new Point(x0 + padX + utilW, y0 + padY + linhaH));

        for (var r = 0; r < 2; r++)
        {
            for (var c = 0; c < colunas; c++)
            {
                var celula = new Rect(
                    x0 + padX + (c * colW) + 1.5,
                    y0 + padY + (r * linhaH) + 1.5,
                    Math.Max(2, colW - 3),
                    Math.Max(2, linhaH - 3));

                DesenharSimulacaoConteudo(dc, celula);
            }
        }
    }

    private static void DesenharSimulacaoConteudo(DrawingContext dc, Rect rect)
    {
        if (rect.Width < 10 || rect.Height < 8) return;

        var margemX = Math.Min(3.5, rect.Width * 0.15);
        var largUtil = Math.Max(4, rect.Width - (2 * margemX));

        // Linha simulando título
        var yTitulo = rect.Top + Math.Max(2, rect.Height * 0.22);
        dc.DrawRoundedRectangle(
            BarraTextoSimulado,
            null,
            new Rect(rect.Left + margemX, yTitulo, largUtil * 0.65, 1.8),
            0.8, 0.8);

        // Barrinhas verticais simulando código de barras
        var yCodigo = rect.Top + Math.Max(4.5, rect.Height * 0.52);
        var altCodigo = Math.Max(2.5, rect.Height * 0.30);
        var xAtual = rect.Left + margemX;
        var xMax = rect.Left + margemX + largUtil;
        var passo = 0;

        while (xAtual + 1.0 <= xMax)
        {
            var largBarra = (passo % 3 == 0) ? 1.3 : 0.8;
            if (xAtual + largBarra > xMax) break;
            dc.DrawRectangle(BarraCodigoSimulado, null, new Rect(xAtual, yCodigo, largBarra, altCodigo));
            xAtual += largBarra + 1.0;
            passo++;
        }
    }

    private static Brush Congelar(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen CongelarPen(Brush b, double espessura)
    {
        b.Freeze();
        var p = new Pen(b, espessura);
        p.Freeze();
        return p;
    }

    private static Pen CongelarPenTracejado(Brush b, double espessura)
    {
        b.Freeze();
        var p = new Pen(b, espessura) { DashStyle = new DashStyle(new double[] { 2.5, 2.0 }, 0) };
        p.Freeze();
        return p;
    }
}
