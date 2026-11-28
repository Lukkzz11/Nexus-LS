using System.Windows;
using System.Windows.Media;
using NexusLS.BarTender.Models.Designer;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Services;
using ZXing;
using ZXing.Common;

namespace NexusLS.BarTender.Services;

/// <summary>
/// Desenha o código de barras como retângulos pretos (vetor), nunca como imagem esticada: cada barra tem um número
/// inteiro de pontos da impressora. A tela e a impressão usam este mesmo desenho.
/// </summary>
public static class BarrasVetoriais
{
    /// <summary>
    /// Os módulos do código (true = preto), uma linha para códigos de barras e uma grade para QR.
    /// Nunca muda o conteúdo: o que não serve como EAN-13 ou Code 39 sai em Code 128.
    /// </summary>
    public static (bool[,] Modulos, bool EhQr, int Silencio)? Codificar(string codigo, TipoSimboloCodigo tipo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;

        try
        {
            if (tipo == TipoSimboloCodigo.QrCode)
                return (Matriz(codigo, BarcodeFormat.QR_CODE), true, 4);

            var texto = codigo.Trim();
            if (tipo == TipoSimboloCodigo.Ean13 && ServeComoEan13(texto))
                return (Matriz(texto, BarcodeFormat.EAN_13), false, 11);

            if (tipo == TipoSimboloCodigo.Code39 && texto.All(c => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%".Contains(c)))
                return (Matriz(texto, BarcodeFormat.CODE_39), false, LegibilidadeCodigo.ModulosDeSilencio);

            if (!Code128.PodeCodificar(texto)) return null;

            var modulos = Code128.Modulos(texto);
            var linha = new bool[modulos.Count, 1];
            for (var i = 0; i < modulos.Count; i++) linha[i, 0] = modulos[i];
            return (linha, false, LegibilidadeCodigo.ModulosDeSilencio);
        }
        catch (Exception ex)
        {
            App.Log(ex, $"Não foi possível montar o código de barras '{codigo}'");
            return null;
        }
    }

    /// <summary>
    /// Um código da loja com 13 números e dígito certo ocupa 95 módulos como EAN-13 e 123 como Code 128. O leitor
    /// devolve os mesmos 13 números, então sai em EAN-13 quando isso deixa as barras mais grossas. Códigos começados
    /// em 0 ficam em Code 128: vários leitores os devolvem como UPC-A, sem o zero.
    /// </summary>
    public static TipoSimboloCodigo SimboloMaisLegivel(
        string codigo, TipoSimboloCodigo tipo, Rect area, Size etiqueta, int dpi, bool deitado = false)
    {
        var texto = codigo.Trim();
        if (tipo != TipoSimboloCodigo.Code128 || texto.Length != 13 || texto[0] == '0' || !CodigoBarrasInterno.EhEan13Valido(texto))
            return tipo;

        var inicio = deitado ? area.Y : area.X;
        var comprimento = deitado ? area.Height : area.Width;
        var limite = deitado ? etiqueta.Height : etiqueta.Width;
        int Pontos(int modulos, int silencio) => BarrasNaImpressora.Calcular(
            modulos, inicio, comprimento, inicio, limite - inicio - comprimento, dpi, silencio).PontosPorModulo;

        return Pontos(95, 11) > Pontos(Code128.Modulos(texto).Count, LegibilidadeCodigo.ModulosDeSilencio)
            ? TipoSimboloCodigo.Ean13
            : tipo;
    }

    /// <summary>12 números (o dígito verificador é calculado) ou 13 com o dígito certo.</summary>
    public static bool ServeComoEan13(string texto) =>
        texto.All(char.IsAsciiDigit) && (texto.Length == 12 || CodigoBarrasInterno.EhEan13Valido(texto));

    private static bool[,] Matriz(string texto, BarcodeFormat formato)
    {
        // Largura e altura zero: o ZXing devolve um elemento da matriz por módulo.
        var bits = new MultiFormatWriter().encode(texto, formato, 0, 0,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 });

        var inicio = 0;
        var fim = bits.Width - 1;
        var linhas = formato == BarcodeFormat.QR_CODE ? bits.Height : 1;
        if (linhas == 1)
        {
            while (inicio < fim && !bits[inicio, 0]) inicio++;
            while (fim > inicio && !bits[fim, 0]) fim--;
        }

        var matriz = new bool[fim - inicio + 1, linhas];
        for (var x = 0; x < matriz.GetLength(0); x++)
            for (var y = 0; y < linhas; y++)
                matriz[x, y] = bits[inicio + x, y];
        return matriz;
    }

    /// <summary>
    /// Desenha o código no retângulo <paramref name="area"/> (medidas da etiqueta, em DIPs).
    /// <paramref name="origem"/> é o canto da etiqueta dentro da página, para as barras caírem em cima dos pontos.
    /// <paramref name="rotacao"/> é a do elemento (em volta do centro): em 0, 90, 180 e 270 graus as barras
    /// continuam alinhadas aos pontos. Quem chama não deve aplicar a rotação por fora.
    /// </summary>
    public static bool Desenhar(
        DrawingContext dc,
        string codigo,
        TipoSimboloCodigo tipo,
        Rect area,
        Size etiqueta,
        Point origem,
        int dpi = BarrasNaImpressora.DpiPadrao,
        double rotacao = 0,
        Point? centroDaRotacao = null)
    {
        var angulo = ((rotacao % 360) + 360) % 360;
        var reto = angulo % 90 == 0;
        var deitado = angulo is 90 or 270;
        var tipoFinal = SimboloMaisLegivel(codigo, tipo,
            deitado ? new Rect(area.X + (area.Width - area.Height) / 2, area.Y + (area.Height - area.Width) / 2, area.Height, area.Width) : area,
            etiqueta, dpi, deitado);
        if (Codificar(codigo, tipoFinal) is not { } simbolo) return false;

        if (angulo != 0)
        {
            var centro = centroDaRotacao ?? new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            if (reto)
            {
                var giro = Matrix.Identity;
                giro.RotateAt(angulo, centro.X, centro.Y);
                area = Rect.Transform(area, giro);
            }
            else
            {
                dc.PushTransform(new RotateTransform(angulo, centro.X, centro.Y));
            }
        }

        if (simbolo.EhQr)
            DesenharQr(dc, simbolo.Modulos, area, origem, dpi);
        else
            DesenharLinha(dc, simbolo.Modulos, simbolo.Silencio, area, etiqueta, origem, dpi, reto ? angulo : 0);

        if (angulo != 0 && !reto) dc.Pop();
        return true;
    }

    private static void DesenharLinha(
        DrawingContext dc, bool[,] modulos, int silencio, Rect area, Size etiqueta, Point origem, int dpi, double angulo)
    {
        var deitado = angulo is 90 or 270;     // barras empilhadas de cima para baixo
        var invertido = angulo is 180 or 270;  // lido de trás para a frente
        var n = modulos.GetLength(0);

        var inicio = deitado ? area.Y : area.X;
        var comprimento = deitado ? area.Height : area.Width;
        var limite = deitado ? etiqueta.Height : etiqueta.Width;
        var deslocamento = deitado ? origem.Y : origem.X;

        var posicao = BarrasNaImpressora.Calcular(
            n, deslocamento + inicio, comprimento, inicio, limite - inicio - comprimento, dpi, silencio);

        // Altura (no sentido das barras) também em pontos inteiros.
        var outroDeslocamento = deitado ? origem.X : origem.Y;
        var de = BarrasNaImpressora.NoPonto(outroDeslocamento + (deitado ? area.X : area.Y), dpi) - outroDeslocamento;
        var ate = BarrasNaImpressora.NoPonto(outroDeslocamento + (deitado ? area.Right : area.Bottom), dpi) - outroDeslocamento;

        for (var i = 0; i < n;)
        {
            if (!modulos[invertido ? n - 1 - i : i, 0]) { i++; continue; }
            var fim = i;
            while (fim < n && modulos[invertido ? n - 1 - fim : fim, 0]) fim++;

            var a = posicao.InicioDip - deslocamento + i * posicao.ModuloDip;
            var tamanho = (fim - i) * posicao.ModuloDip;
            dc.DrawRectangle(Brushes.Black, null,
                deitado ? new Rect(de, a, ate - de, tamanho) : new Rect(a, de, tamanho, ate - de));
            i = fim;
        }
    }

    private static void DesenharQr(DrawingContext dc, bool[,] modulos, Rect area, Point origem, int dpi)
    {
        var n = modulos.GetLength(0);
        var ponto = BarrasNaImpressora.PontoDip(dpi);
        var modulo = Math.Max(1, (int)(Math.Min(area.Width, area.Height) / ponto / n)) * ponto;
        var x0 = BarrasNaImpressora.NoPonto(origem.X + area.X + (area.Width - n * modulo) / 2, dpi) - origem.X;
        var y0 = BarrasNaImpressora.NoPonto(origem.Y + area.Y + (area.Height - n * modulo) / 2, dpi) - origem.Y;

        for (var y = 0; y < modulos.GetLength(1); y++)
            for (var x = 0; x < n; x++)
            {
                if (!modulos[x, y]) continue;
                var fim = x;
                while (fim < n && modulos[fim, y]) fim++;
                dc.DrawRectangle(Brushes.Black, null, new Rect(x0 + x * modulo, y0 + y * modulo, (fim - x) * modulo, modulo));
                x = fim - 1;
            }
    }
}
