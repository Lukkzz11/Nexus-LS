namespace NexusLS.Core.Etiquetas;

/// <summary>
/// Um código de barras só é lido pelo leitor se a barra mais fina (o "módulo") for larga o bastante.
/// Abaixo de 0,25 mm a maioria dos leitores falha. Código comprido numa etiqueta estreita fica assim.
/// </summary>
public static class LegibilidadeCodigo
{
    public const double ModuloMinimoMm = 0.25;

    /// <summary>Fração da largura da etiqueta que o desenho usa para o código (o resto são as margens).</summary>
    public const double FracaoUtilDaLargura = 0.9;

    /// <summary>Módulos brancos exigidos de cada lado do código (zona de silêncio).</summary>
    public const int ModulosDeSilencio = 10;

    /// <summary>Largura da barra mais fina, em mm, se o código for impresso numa etiqueta desta largura.</summary>
    public static double ModuloMm(string codigo, double larguraEtiquetaMm) =>
        larguraEtiquetaMm * FracaoUtilDaLargura / (Code128.Modulos(codigo).Count + 2 * ModulosDeSilencio);

    /// <summary>Sem código (ou código que o Code 128 não aceita) não há o que ler: não conta como ilegível.</summary>
    public static bool Legivel(string? codigo, double larguraEtiquetaMm) =>
        !Code128.PodeCodificar(codigo) || ModuloMm(codigo!, larguraEtiquetaMm) >= ModuloMinimoMm;

    /// <summary>Largura mínima de etiqueta, em mm, para este código ficar legível.</summary>
    public static double LarguraMinimaMm(string codigo) =>
        ModuloMinimoMm * (Code128.Modulos(codigo).Count + 2 * ModulosDeSilencio) / FracaoUtilDaLargura;
}
