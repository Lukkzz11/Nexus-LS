using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Services;

public class DesenhoEtiquetaService
{
    public const double DipPorMm = 96.0 / 25.4; // 1 polegada = 25.4 mm, WPF = 96 DIPs/polegada

    public static double Dip(double mm) => mm * DipPorMm;

    private static readonly Typeface FontePadrao = new("Segoe UI");
    private static readonly Typeface FonteNegrito =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface FontePreco =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal);

    public static void Desenhar(
        DrawingContext dc,
        Produto produto,
        string? tamanho,
        ConfiguracaoEtiqueta config,
        double larguraDip,
        double alturaDip)
    {
        // Fundo branco da etiqueta
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, larguraDip, alturaDip));

        var margemX = Math.Max(4, larguraDip * 0.04);
        var margemY = Math.Max(4, alturaDip * 0.04);
        var areaUtilLargura = larguraDip - (2 * margemX);

        var yAtual = margemY;

        // 1. Nome da Loja (Topo)
        if (config.MostrarNomeLoja && !string.IsNullOrWhiteSpace(config.NomeLoja))
        {
            var tamLoja = Math.Clamp(alturaDip * 0.09, 7.5, 12);
            var txtLoja = FormatarTexto(config.NomeLoja.ToUpper(), FonteNegrito, tamLoja, areaUtilLargura);
            txtLoja.TextAlignment = TextAlignment.Center;
            dc.DrawText(txtLoja, new Point(margemX, yAtual));
            yAtual += txtLoja.Height + 2;

            // Linha sutil separadora abaixo do cabeçalho da loja
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), 0.8),
                new Point(margemX, yAtual), new Point(larguraDip - margemX, yAtual));
            yAtual += 3;
        }

        // 2. Nome do Produto (até 2 linhas)
        if (config.MostrarNomeProduto)
        {
            var tamNome = Math.Clamp(alturaDip * 0.11, 8.5, 14);
            var nomeExibicao = !string.IsNullOrWhiteSpace(produto.Nome) ? produto.Nome : "PRODUTO TESTE NEXUS";
            var txtNome = FormatarTexto(nomeExibicao, FonteNegrito, tamNome, areaUtilLargura);
            txtNome.MaxTextHeight = tamNome * 2.3;
            txtNome.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(txtNome, new Point(margemX, yAtual));
            yAtual += txtNome.Height + 2;
        }

        // 3. Referência / Código Interno / Tamanho
        var temRef = config.MostrarReferencia && !string.IsNullOrWhiteSpace(produto.Referencia);
        var temCodInt = config.MostrarCodigoInterno && !string.IsNullOrWhiteSpace(produto.CodigoInterno);
        var temTamanho = config.MostrarTamanho && !string.IsNullOrWhiteSpace(tamanho);

        if (temRef || temCodInt || temTamanho)
        {
            var tamInfo = Math.Clamp(alturaDip * 0.075, 7, 10);
            var sbInfo = new StringBuilder();
            if (temRef) sbInfo.Append($"REF: {produto.Referencia}   ");
            if (temCodInt) sbInfo.Append($"{produto.CodigoInterno}");

            if (sbInfo.Length > 0)
            {
                var txtInfo = FormatarTexto(sbInfo.ToString().Trim(), FontePadrao, tamInfo, areaUtilLargura * 0.7);
                txtInfo.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(txtInfo, new Point(margemX, yAtual));
            }

            if (temTamanho)
            {
                var tamBadge = FormatarTexto(tamanho!, FonteNegrito, Math.Clamp(alturaDip * 0.09, 8, 12), areaUtilLargura * 0.28);
                var larguraCaixa = tamBadge.Width + 8;
                var alturaCaixa = tamBadge.Height + 2;
                var xCaixa = larguraDip - margemX - larguraCaixa;
                dc.DrawRoundedRectangle(null, new Pen(Brushes.Black, 1.0), new Rect(xCaixa, yAtual - 1, larguraCaixa, alturaCaixa), 2, 2);
                dc.DrawText(tamBadge, new Point(xCaixa + 4, yAtual));
            }

            yAtual += Math.Clamp(alturaDip * 0.09, 9, 13);
        }

        // 4. Preços (Varejo e Atacado)
        if (config.MostrarPrecoVarejo || (config.MostrarPrecoAtacado && produto.PrecoAtacadoCentavos > 0))
        {
            var textoVarejo = Dinheiro.Formatar(produto.PrecoVendaCentavos);
            var tamVarejo = Math.Clamp(alturaDip * 0.20, 11, 26);
            var txtVarejo = FormatarTexto(textoVarejo, FontePreco, tamVarejo, areaUtilLargura);

            // Se couber atacado ao lado ou abaixo
            if (config.MostrarPrecoAtacado && produto.PrecoAtacadoCentavos > 0)
            {
                var textoAtacado = $"Atacado: {Dinheiro.Formatar(produto.PrecoAtacadoCentavos)}";
                var tamAtacado = Math.Clamp(tamVarejo * 0.55, 7.5, 12);
                var txtAtacado = FormatarTexto(textoAtacado, FonteNegrito, tamAtacado, areaUtilLargura);

                // Desenha Varejo à esquerda e Atacado à direita ou abaixo dependendo da altura
                dc.DrawText(txtVarejo, new Point(margemX, yAtual - 2));
                var xAtacado = larguraDip - margemX - txtAtacado.Width;
                if (xAtacado > margemX + txtVarejo.Width + 6)
                {
                    dc.DrawText(txtAtacado, new Point(xAtacado, yAtual + (txtVarejo.Height - txtAtacado.Height) - 2));
                }
                else
                {
                    yAtual += txtVarejo.Height;
                    dc.DrawText(txtAtacado, new Point(margemX, yAtual - 2));
                }
            }
            else
            {
                dc.DrawText(txtVarejo, new Point(margemX, yAtual - 2));
            }

            yAtual += txtVarejo.Height + 2;
        }

        // 5. Código de Barras (ao fundo/rodapé)
        if (config.MostrarCodigoBarras)
        {
            var codigo = !string.IsNullOrWhiteSpace(produto.CodigoBarras)
                ? produto.CodigoBarras
                : (!string.IsNullOrWhiteSpace(produto.CodigoInterno) ? produto.CodigoInterno : "2000000000018");

            var espacoRestante = alturaDip - margemY - yAtual;
            if (espacoRestante > 14 && Code128.PodeCodificar(codigo))
            {
                var alturaBarras = Math.Clamp(espacoRestante - 12, 10, alturaDip * 0.28);
                var yBarras = alturaDip - margemY - alturaBarras - 11;
                DesenharBarrasCode128(dc, codigo, margemX, areaUtilLargura, yBarras, alturaBarras);
            }
        }
    }

    private static void DesenharBarrasCode128(
        DrawingContext dc,
        string codigo,
        double xBase,
        double larguraDisponivel,
        double yBase,
        double alturaBarras)
    {
        try
        {
            var modulos = Code128.Modulos(codigo);
            const int margemSilencioModulos = 6;
            var totalModulos = modulos.Count + (2 * margemSilencioModulos);
            var larguraModulo = larguraDisponivel / totalModulos;

            // Centraliza o código
            var xInicio = xBase + (margemSilencioModulos * larguraModulo);

            for (var i = 0; i < modulos.Count;)
            {
                if (!modulos[i])
                {
                    i++;
                    continue;
                }

                var fim = i;
                while (fim < modulos.Count && modulos[fim]) fim++;

                var larguraBarra = (fim - i) * larguraModulo;
                dc.DrawRectangle(
                    Brushes.Black,
                    null,
                    new Rect(xInicio + (i * larguraModulo), yBase, larguraBarra, alturaBarras));

                i = fim;
            }

            // Dígitos do código centralizados abaixo das barras
            var tamFonteTexto = Math.Clamp(alturaBarras * 0.35, 6.5, 9.5);
            var txtCodigo = FormatarTexto(codigo, FontePadrao, tamFonteTexto, larguraDisponivel);
            txtCodigo.TextAlignment = TextAlignment.Center;
            dc.DrawText(txtCodigo, new Point(xBase, yBase + alturaBarras + 1));
        }
        catch
        {
            // Fallback caso ocorra caractere inválido
            var txtErro = FormatarTexto(codigo, FontePadrao, 8, larguraDisponivel);
            txtErro.TextAlignment = TextAlignment.Center;
            dc.DrawText(txtErro, new Point(xBase, yBase));
        }
    }

    public static BitmapSource RenderizarBitmap(
        Produto produto,
        string? tamanho,
        ConfiguracaoEtiqueta config,
        double escalaDpi = 2.0)
    {
        var formato = config.Formato.FormatoCore;
        var larguraDip = Dip(formato.EtiquetaLarguraMm);
        var alturaDip = Dip(formato.EtiquetaAlturaMm);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Desenhar(dc, produto, tamanho, config, larguraDip, alturaDip);
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

    public static string GerarZpl(Produto produto, ConfiguracaoEtiqueta config, int quantidade = 1)
    {
        var formato = config.Formato.FormatoCore;
        // 203 DPI (8 dots/mm) padrão térmico Zebra / Argox / Elgin
        const double dotsPorMm = 8.0;
        var larguraDots = (int)(formato.EtiquetaLarguraMm * dotsPorMm);
        var alturaDots = (int)(formato.EtiquetaAlturaMm * dotsPorMm);

        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine($"^PW{larguraDots}");
        sb.AppendLine($"^LL{alturaDots}");
        sb.AppendLine("^LH0,0");

        var y = 20;

        // Loja
        if (config.MostrarNomeLoja && !string.IsNullOrWhiteSpace(config.NomeLoja))
        {
            sb.AppendLine($"^FO20,{y}^A0N,22,22^FB{larguraDots - 40},1,0,C^FD{config.NomeLoja}^FS");
            y += 28;
        }

        // Produto
        if (config.MostrarNomeProduto)
        {
            sb.AppendLine($"^FO20,{y}^A0N,24,24^FB{larguraDots - 40},2,0,L^FD{produto.Nome}^FS");
            y += 54;
        }

        // Referência e Preço
        var textoVarejo = Dinheiro.Formatar(produto.PrecoVendaCentavos);
        sb.AppendLine($"^FO20,{y}^A0N,38,38^FD{textoVarejo}^FS");

        if (config.MostrarPrecoAtacado && produto.PrecoAtacadoCentavos > 0)
        {
            var textoAtacado = $"Atacado: {Dinheiro.Formatar(produto.PrecoAtacadoCentavos)}";
            sb.AppendLine($"^FO{larguraDots / 2},{y + 8}^A0N,22,22^FD{textoAtacado}^FS");
        }

        y += 46;

        // Código de Barras Code 128
        if (config.MostrarCodigoBarras)
        {
            var codigo = !string.IsNullOrWhiteSpace(produto.CodigoBarras)
                ? produto.CodigoBarras
                : (!string.IsNullOrWhiteSpace(produto.CodigoInterno) ? produto.CodigoInterno : "2000000000018");

            var alturaBarrasDots = Math.Max(30, alturaDots - y - 35);
            sb.AppendLine($"^FO30,{y}^BY2,2,{alturaBarrasDots}^BCN,{alturaBarrasDots},Y,N,N^FD{codigo}^FS");
        }

        sb.AppendLine($"^PQ{Math.Max(1, quantidade)}");
        sb.AppendLine("^XZ");
        return sb.ToString();
    }

    private static FormattedText FormatarTexto(string texto, Typeface fonte, double tamanho, double? larguraMax)
    {
        var ft = new FormattedText(
            texto,
            CultureInfo.GetCultureInfo("pt-BR"),
            FlowDirection.LeftToRight,
            fonte,
            tamanho,
            Brushes.Black,
            1.0);

        if (larguraMax is double max && max > 0)
        {
            ft.MaxTextWidth = max;
        }

        return ft;
    }
}
