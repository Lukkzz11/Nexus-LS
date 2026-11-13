namespace NexusLS.BarTender.Services;

/// <summary>
/// Geometria da mesa de trabalho do Estúdio, no mesmo espírito do CorelDRAW e do Illustrator:
/// a etiqueta é a página e, em volta dela, existe uma área de trabalho (desktop) onde objetos
/// podem ficar fora da página. O que está fora da página continua visível para editar, mas
/// não é impresso.
/// Todas as medidas são em milímetros, exceto quando o nome termina em "Dip".
/// </summary>
public static class Bancada
{
    /// <summary>Área de trabalho visível ao redor da etiqueta, de cada lado (30 mm).</summary>
    public const double MargemMesaMm = 30.0;

    public static double LimiteMinimoMm => -MargemMesaMm;

    public static double LimiteMaximoX(double larguraEtiquetaMm) => larguraEtiquetaMm + MargemMesaMm;

    public static double LimiteMaximoY(double alturaEtiquetaMm) => alturaEtiquetaMm + MargemMesaMm;

    /// <summary>
    /// Prende a origem do objeto na mesa de trabalho: pode sair da etiqueta e ficar no
    /// desktop, mas não some para fora da área visível (como o "arrastar e perder" do Corel).
    /// </summary>
    public static (double X, double Y) Prender(double xMm, double yMm, double larguraEtiquetaMm, double alturaEtiquetaMm)
    {
        var x = Math.Clamp(xMm, LimiteMinimoMm, LimiteMaximoX(larguraEtiquetaMm));
        var y = Math.Clamp(yMm, LimiteMinimoMm, LimiteMaximoY(alturaEtiquetaMm));
        return (x, y);
    }

    /// <summary>O objeto tem alguma parte dentro da etiqueta? Só essa parte sai na impressão.</summary>
    public static bool IntersectaEtiqueta(
        double xMm,
        double yMm,
        double larguraMm,
        double alturaMm,
        double larguraEtiquetaMm,
        double alturaEtiquetaMm)
    {
        return xMm < larguraEtiquetaMm
            && yMm < alturaEtiquetaMm
            && xMm + Math.Max(0, larguraMm) > 0
            && yMm + Math.Max(0, alturaMm) > 0;
    }

    /// <summary>O objeto está inteiro dentro da etiqueta (nada dele será cortado)?</summary>
    public static bool DentroDaEtiqueta(
        double xMm,
        double yMm,
        double larguraMm,
        double alturaMm,
        double larguraEtiquetaMm,
        double alturaEtiquetaMm)
    {
        const double toleranciaMm = 0.01;
        return xMm >= -toleranciaMm
            && yMm >= -toleranciaMm
            && xMm + larguraMm <= larguraEtiquetaMm + toleranciaMm
            && yMm + alturaMm <= alturaEtiquetaMm + toleranciaMm;
    }

    /// <summary>
    /// Zoom que faz a mesa inteira caber na área visível, em passos de 5% (como o
    /// "Ajustar à janela" do Corel e o "Fit Artboard in Window" do Illustrator).
    /// </summary>
    public static double ZoomParaCaber(
        double larguraConteudoDip,
        double alturaConteudoDip,
        double larguraVisivelDip,
        double alturaVisivelDip,
        double minimo = 0.2,
        double maximo = 8.0,
        double folgaDip = 24.0)
    {
        if (larguraConteudoDip <= 0 || alturaConteudoDip <= 0 || larguraVisivelDip <= 0 || alturaVisivelDip <= 0)
        {
            return 1.0;
        }

        var larguraUtil = Math.Max(1.0, larguraVisivelDip - folgaDip);
        var alturaUtil = Math.Max(1.0, alturaVisivelDip - folgaDip);
        var zoom = Math.Min(larguraUtil / larguraConteudoDip, alturaUtil / alturaConteudoDip);

        zoom = Math.Floor((zoom * 20.0) + 0.0001) / 20.0;
        return Math.Clamp(zoom, minimo, maximo);
    }
}
