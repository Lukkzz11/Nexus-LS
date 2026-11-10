using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models.Designer;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Services;

public class EditorDesenhoService
{
    public const double DipPorMm = 96.0 / 25.4; // 1 polegada = 25.4 mm, WPF = 96 DIPs/pol

    public static double Dip(double mm) => mm * DipPorMm;
    public static double Mm(double dip) => dip / DipPorMm;

    private static readonly Typeface FontePadrao = new("Segoe UI");

    public static void Desenhar(
        DrawingContext dc,
        ModeloEtiquetaDesign modelo,
        Produto? produto,
        string? tamanho,
        string nomeLoja,
        bool desenharBordaExterna = false)
    {
        var larguraDip = Dip(modelo.LarguraMm);
        var alturaDip = Dip(modelo.AlturaMm);

        // Fundo branco da etiqueta
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, larguraDip, alturaDip));

        if (desenharBordaExterna)
        {
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), 1.0)
            {
                DashStyle = DashStyles.Dash
            }, new Rect(0, 0, larguraDip, alturaDip));
        }

        foreach (var el in modelo.Elementos)
        {
            DesenharElemento(dc, el, produto, tamanho, nomeLoja);
        }
    }

    public static Brush ConverterCor(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(hex) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void DesenharElemento(
        DrawingContext dc,
        ElementoDesign el,
        Produto? produto,
        string? tamanho,
        string nomeLoja)
    {
        if (!el.Visivel) return;

        var xDip = Dip(el.X);
        var yDip = Dip(el.Y);
        var largDip = Dip(el.Largura);
        var altDip = Dip(el.Altura);

        bool rotacionou = false;
        if (el.Rotacao != 0)
        {
            rotacionou = true;
            dc.PushTransform(new RotateTransform(el.Rotacao, xDip + (largDip / 2.0), yDip + (altDip / 2.0)));
        }

        switch (el.Tipo)
        {
            case TipoElemento.Texto:
                DesenharTexto(dc, el, produto, tamanho, nomeLoja, xDip, yDip, largDip, altDip);
                break;

            case TipoElemento.Imagem:
                DesenharImagem(dc, el, xDip, yDip, largDip, altDip);
                break;

            case TipoElemento.CodigoBarras:
                DesenharCodigoBarras(dc, el, produto, tamanho, xDip, yDip, largDip, altDip);
                break;

            case TipoElemento.Retangulo:
                var penRet = new Pen(ConverterCor(el.CorTexto, Brushes.Black), Math.Max(0.5, Dip(el.EspessuraBorda * 0.25)));
                var brushRet = el.Preenchido ? ConverterCor(el.CorFundo, Brushes.Black) : null;
                var raioDip = Dip(el.RaioCanto);
                if (raioDip > 0)
                {
                    dc.DrawRoundedRectangle(brushRet, penRet, new Rect(xDip, yDip, largDip, altDip), raioDip, raioDip);
                }
                else
                {
                    dc.DrawRectangle(brushRet, penRet, new Rect(xDip, yDip, largDip, altDip));
                }
                break;

            case TipoElemento.Linha:
                var penLinha = new Pen(ConverterCor(el.CorTexto, Brushes.Black), Math.Max(0.5, Dip(el.EspessuraBorda * 0.25)));
                dc.DrawLine(penLinha, new Point(xDip, yDip + (altDip / 2)), new Point(xDip + largDip, yDip + (altDip / 2)));
                break;
        }

        if (rotacionou)
        {
            dc.Pop();
        }
    }

    private static void DesenharTexto(
        DrawingContext dc,
        ElementoDesign el,
        Produto? produto,
        string? tamanho,
        string nomeLoja,
        double x, double y, double larg, double alt)
    {
        var textoFinal = ResolverTexto(el, produto, tamanho, nomeLoja);
        if (string.IsNullOrEmpty(textoFinal)) return;

        var peso = el.Negrito ? FontWeights.Bold : FontWeights.Normal;
        var estilo = el.Italico ? FontStyles.Italic : FontStyles.Normal;
        var nomeFonte = !string.IsNullOrWhiteSpace(el.Fonte) ? el.Fonte : "Segoe UI";
        var typeface = new Typeface(new FontFamily(nomeFonte), estilo, peso, FontStretches.Normal);
        var pincelTexto = ConverterCor(el.CorTexto, Brushes.Black);

        var formatted = new FormattedText(
            textoFinal,
            CultureInfo.GetCultureInfo("pt-BR"),
            FlowDirection.LeftToRight,
            typeface,
            el.TamanhoFonte,
            pincelTexto,
            1.0)
        {
            MaxTextWidth = Math.Max(1, larg),
            MaxTextHeight = Math.Max(1, alt),
            TextAlignment = el.Alinhamento,
            Trimming = TextTrimming.WordEllipsis
        };

        if (el.Riscado)
        {
            formatted.SetTextDecorations(TextDecorations.Strikethrough);
        }

        dc.DrawText(formatted, new Point(x, y));
    }

    private static void DesenharImagem(
        DrawingContext dc,
        ElementoDesign el,
        double x, double y, double larg, double alt)
    {
        BitmapSource? imagem = null;

        if (!string.IsNullOrWhiteSpace(el.ImagemBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(el.ImagemBase64);
                using var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                imagem = bi;
            }
            catch { }
        }
        else if (!string.IsNullOrWhiteSpace(el.CaminhoImagem) && File.Exists(el.CaminhoImagem))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(el.CaminhoImagem);
                bi.EndInit();
                bi.Freeze();
                imagem = bi;
            }
            catch { }
        }

        if (imagem != null)
        {
            if (el.ManterProporcao)
            {
                var aspectImg = imagem.Width / (double)imagem.Height;
                var aspectDest = larg / alt;

                double drawWidth, drawHeight, drawX, drawY;

                if (aspectImg > aspectDest)
                {
                    drawWidth = larg;
                    drawHeight = larg / aspectImg;
                    drawX = x;
                    drawY = y + ((alt - drawHeight) / 2);
                }
                else
                {
                    drawHeight = alt;
                    drawWidth = alt * aspectImg;
                    drawX = x + ((larg - drawWidth) / 2);
                    drawY = y;
                }

                dc.DrawImage(imagem, new Rect(drawX, drawY, drawWidth, drawHeight));
            }
            else
            {
                dc.DrawImage(imagem, new Rect(x, y, larg, alt));
            }
        }
        else
        {
            // Placeholder visual para imagens não carregadas
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)), new Pen(Brushes.Gray, 0.8) { DashStyle = DashStyles.Dash }, new Rect(x, y, larg, alt));
            var txtPlaceholder = new FormattedText(
                "[Logotipo]",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                FontePadrao,
                9.0,
                Brushes.Gray,
                1.0)
            {
                MaxTextWidth = larg,
                TextAlignment = TextAlignment.Center
            };
            dc.DrawText(txtPlaceholder, new Point(x, y + (alt / 2) - 6));
        }
    }

    private static void DesenharCodigoBarras(
        DrawingContext dc,
        ElementoDesign el,
        Produto? produto,
        string? tamanho,
        double x, double y, double larg, double alt)
    {
        var codigo = ResolverCodigo(el, produto, tamanho);
        if (string.IsNullOrWhiteSpace(codigo))
            return;

        var alturaTexto = el.MostrarTextoLegivel && el.TipoCodigo != TipoSimboloCodigo.QrCode ? 10.0 : 0.0;
        var alturaBarras = Math.Max(6.0, alt - alturaTexto);

        var largPx = (int)Math.Ceiling(larg * 2.0);
        var altPx = (int)Math.Ceiling(alturaBarras * 2.0);

        var barcodeBmp = BarcodeGeneratorService.Gerar(codigo, el.TipoCodigo, largPx, altPx, margem: 0);
        if (barcodeBmp != null)
        {
            dc.DrawImage(barcodeBmp, new Rect(x, y, larg, alturaBarras));
        }

        if (el.MostrarTextoLegivel && el.TipoCodigo != TipoSimboloCodigo.QrCode)
        {
            var txtLegivel = new FormattedText(
                codigo,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                FontePadrao,
                7.5,
                Brushes.Black,
                1.0)
            {
                MaxTextWidth = larg,
                TextAlignment = TextAlignment.Center
            };
            dc.DrawText(txtLegivel, new Point(x, y + alturaBarras + 0.5));
        }
    }

    public static string ResolverTexto(ElementoDesign el, Produto? produto, string? tamanho, string nomeLoja)
    {
        if (produto == null)
        {
            // Mock de demonstração para visualização no Designer
            if (el.Variavel != VariavelEtiqueta.Nenhuma)
            {
                return el.Variavel switch
                {
                    VariavelEtiqueta.NomeProduto => "CAMISETA OVERSIZED LIFE SURF ALGODÃO",
                    VariavelEtiqueta.PrecoVarejo => "R$ 119,90",
                    VariavelEtiqueta.PrecoAtacado => "Atacado: R$ 79,90",
                    VariavelEtiqueta.Referencia => "LS-2026-M",
                    VariavelEtiqueta.Tamanho => !string.IsNullOrEmpty(tamanho) ? tamanho : "G",
                    VariavelEtiqueta.CodigoBarras => "7891234567890",
                    VariavelEtiqueta.CodigoInterno => "00452",
                    VariavelEtiqueta.NomeLoja => !string.IsNullOrEmpty(nomeLoja) ? nomeLoja : "NEXUS LS",
                    _ => el.Texto
                };
            }

            return el.Texto
                .Replace("{Nome}", "CAMISETA OVERSIZED LIFE SURF ALGODÃO")
                .Replace("R$ {Preco}", "R$ 119,90")
                .Replace("{Preco}", "119,90")
                .Replace("R$ {PrecoVarejo}", "R$ 119,90")
                .Replace("{PrecoVarejo}", "119,90")
                .Replace("R$ {PrecoAtacado}", "R$ 79,90")
                .Replace("{PrecoAtacado}", "79,90")
                .Replace("{Ref}", "LS-2026-M")
                .Replace("{Tamanho}", !string.IsNullOrEmpty(tamanho) ? tamanho : "G")
                .Replace("{Codigo}", "7891234567890")
                .Replace("{Loja}", !string.IsNullOrEmpty(nomeLoja) ? nomeLoja : "NEXUS LS");
        }

        var baseTexto = el.Texto;

        if (el.Variavel != VariavelEtiqueta.Nenhuma)
        {
            return el.Variavel switch
            {
                VariavelEtiqueta.NomeProduto => produto.Nome,
                VariavelEtiqueta.PrecoVarejo => Dinheiro.Formatar(produto.PrecoVendaCentavos),
                VariavelEtiqueta.PrecoAtacado => produto.PrecoAtacadoCentavos > 0 ? Dinheiro.Formatar(produto.PrecoAtacadoCentavos) : "",
                VariavelEtiqueta.Referencia => produto.Referencia ?? "",
                VariavelEtiqueta.Tamanho => tamanho ?? "",
                VariavelEtiqueta.CodigoBarras => CodigoBarrasTamanho.Resolver(produto, tamanho) ?? "",
                VariavelEtiqueta.CodigoInterno => produto.CodigoInterno ?? "",
                VariavelEtiqueta.NomeLoja => nomeLoja,
                _ => baseTexto
            };
        }

        var precoVenda = Dinheiro.FormatarParaEdicao(produto.PrecoVendaCentavos);
        var precoAtacado = produto.PrecoAtacadoCentavos > 0 ? Dinheiro.FormatarParaEdicao(produto.PrecoAtacadoCentavos) : "";

        // Regra única do Core: EAN-13 do produto, senão referência, senão código curto, senão código interno + número do tamanho.
        var codigoFormatado = CodigoBarrasTamanho.Resolver(produto, tamanho) ?? "";

        // Suporte a substituição inline {Nome}, {Preco}, {Ref}, etc.
        return baseTexto
            .Replace("{Nome}", produto.Nome)
            .Replace("R$ {Preco}", "R$ " + precoVenda)
            .Replace("{Preco}", precoVenda)
            .Replace("R$ {PrecoVarejo}", "R$ " + precoVenda)
            .Replace("{PrecoVarejo}", precoVenda)
            .Replace("R$ {PrecoAtacado}", "R$ " + precoAtacado)
            .Replace("{PrecoAtacado}", precoAtacado)
            .Replace("{Ref}", produto.Referencia ?? "")
            .Replace("{Tamanho}", tamanho ?? "")
            .Replace("{Codigo}", codigoFormatado)
            .Replace("{Loja}", nomeLoja);
    }

    public static string ResolverCodigo(ElementoDesign el, Produto? produto, string? tamanho = null)
    {
        // Se a variável é Nenhuma ou se tem ValorCodigo fixo e a variável não foi definida para um campo específico
        if (el.Variavel == VariavelEtiqueta.Nenhuma && !string.IsNullOrWhiteSpace(el.ValorCodigo))
        {
            return el.ValorCodigo;
        }

        if (produto == null)
        {
            var codPadrao = !string.IsNullOrWhiteSpace(el.ValorCodigo) ? el.ValorCodigo : "7891234567890";
            return CodigoBarrasTamanho.Montar(codPadrao, CodigoBarrasTamanho.ObterOrdemGenerica(tamanho)) ?? codPadrao;
        }

        if (el.Variavel == VariavelEtiqueta.CodigoInterno && !string.IsNullOrWhiteSpace(produto.CodigoInterno))
        {
            // O código interno é um identificador do produto inteiro: não recebe número de tamanho.
            return produto.CodigoInterno;
        }

        if (el.Variavel == VariavelEtiqueta.Referencia && !string.IsNullOrWhiteSpace(produto.Referencia))
        {
            return CodigoBarrasTamanho.ResolverReferencia(produto, tamanho) ?? produto.Referencia;
        }

        if (el.Variavel == VariavelEtiqueta.NomeProduto && !string.IsNullOrWhiteSpace(produto.Nome))
        {
            return produto.Nome;
        }

        if (el.Variavel == VariavelEtiqueta.PrecoVarejo)
        {
            return (produto.PrecoVendaCentavos / 100.0).ToString("F2");
        }

        // Padrão: código oficial do produto/tamanho (EAN-13, referência, código curto ou código interno).
        var codigoBase = CodigoBarrasTamanho.Resolver(produto, tamanho);
        if (!string.IsNullOrWhiteSpace(codigoBase)) return codigoBase;

        // Produto real sem nenhum código: não inventa um EAN que não é dele (a fila avisa antes de imprimir).
        if (!string.IsNullOrWhiteSpace(el.ValorCodigo)) return el.ValorCodigo;
        return produto is null ? "7891234567890" : "";
    }

    public static BitmapSource RenderizarBitmap(
        ModeloEtiquetaDesign modelo,
        Produto? produto,
        string? tamanho,
        string nomeLoja,
        double escalaDpi = 2.0)
    {
        var larguraDip = Dip(modelo.LarguraMm);
        var alturaDip = Dip(modelo.AlturaMm);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Desenhar(dc, modelo, produto, tamanho, nomeLoja, desenharBordaExterna: false);
        }

        var pixelWidth = (int)Math.Ceiling(larguraDip * escalaDpi);
        var pixelHeight = (int)Math.Ceiling(alturaDip * escalaDpi);

        var rtb = new RenderTargetBitmap(
            Math.Max(1, pixelWidth),
            Math.Max(1, pixelHeight),
            96 * escalaDpi,
            96 * escalaDpi,
            PixelFormats.Pbgra32);

        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static string GerarZpl(ModeloEtiquetaDesign modelo, Produto? produto, string nomeLoja, int copias = 1, string? tamanho = null)
    {
        const double dotsPorMm = 8.0; // 203 DPI padrão térmico
        var larguraDots = (int)(modelo.LarguraMm * dotsPorMm);
        var alturaDots = (int)(modelo.AlturaMm * dotsPorMm);

        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine($"^PW{larguraDots}");
        sb.AppendLine($"^LL{alturaDots}");
        sb.AppendLine("^LH0,0");

        foreach (var el in modelo.Elementos)
        {
            if (!el.Visivel) continue;

            var xDots = (int)(el.X * dotsPorMm);
            var yDots = (int)(el.Y * dotsPorMm);
            var largDots = (int)(el.Largura * dotsPorMm);
            var altDots = (int)(el.Altura * dotsPorMm);

            char orientacaoZpl = el.Rotacao switch
            {
                90 => 'R',
                180 => 'I',
                270 => 'B',
                _ => 'N'
            };

            switch (el.Tipo)
            {
                case TipoElemento.Texto:
                    var txt = ResolverTexto(el, produto, tamanho, nomeLoja);
                    var tamFonteZpl = Math.Clamp((int)(el.TamanhoFonte * 2.8), 16, 80);
                    sb.AppendLine($"^FO{xDots},{yDots}^A0{orientacaoZpl},{tamFonteZpl},{tamFonteZpl}^FD{txt}^FS");
                    break;

                case TipoElemento.CodigoBarras:
                    var cod = ResolverCodigo(el, produto, tamanho);
                    if (string.IsNullOrWhiteSpace(cod)) break;
                    if (el.TipoCodigo == TipoSimboloCodigo.QrCode)
                    {
                        var mag = Math.Clamp(altDots / 25, 2, 8);
                        sb.AppendLine($"^FO{xDots},{yDots}^BQ{orientacaoZpl},2,{mag}{CampoZpl.Montar($"QA,{cod}")}");
                    }
                    else
                    {
                        if (!Code128.PodeCodificar(cod)) break;
                        var hBarras = Math.Max(20, altDots - 20);
                        var legivel = el.MostrarTextoLegivel ? "Y" : "N";
                        sb.AppendLine($"^FO{xDots},{yDots}^BC{orientacaoZpl},{hBarras},{legivel},N,N{CampoZpl.Montar(cod)}");
                    }
                    break;

                case TipoElemento.Retangulo:
                    var esp = Math.Max(1, (int)(el.EspessuraBorda * 2));
                    var raioZpl = Math.Clamp((int)(el.RaioCanto * 2), 0, 8);
                    sb.AppendLine($"^FO{xDots},{yDots}^GB{largDots},{altDots},{esp},B,{raioZpl}^FS");
                    break;

                case TipoElemento.Linha:
                    var espLinha = Math.Max(1, (int)(el.EspessuraBorda * 2));
                    sb.AppendLine($"^FO{xDots},{yDots}^GB{largDots},1,{espLinha}^FS");
                    break;
            }
        }

        sb.AppendLine($"^PQ{copias}");
        sb.AppendLine("^XZ");
        return sb.ToString();
    }
}
