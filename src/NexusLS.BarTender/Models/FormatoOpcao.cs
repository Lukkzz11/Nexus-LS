using NexusLS.Core.Etiquetas;

namespace NexusLS.BarTender.Models;

public record FormatoOpcao(
    string Nome,
    string Descricao,
    FormatoEtiqueta FormatoCore)
{
    public static readonly FormatoOpcao Bobina50x30 = new(
        "Bobina 50 × 30 mm",
        "Padrão térmico comercial (joias, roupas, acessórios)",
        FormatoEtiqueta.Bobina50x30);

    public static readonly FormatoOpcao Bobina40x25 = new(
        "Bobina 40 × 25 mm",
        "Formato compacto para gôndolas e etiquetas pequenas",
        FormatoEtiqueta.Bobina40x25);

    public static readonly FormatoOpcao Bobina60x40 = new(
        "Bobina 60 × 40 mm",
        "Formato grande com alta visibilidade de informações e preços",
        FormatoEtiqueta.Bobina60x40);

    public static readonly FormatoOpcao FolhaA4_3x7 = new(
        "Folha A4 (Pimaco / 3 × 7)",
        "21 etiquetas por folha (63,5 × 38,1 mm) para impressoras jato de tinta ou laser",
        FormatoEtiqueta.FolhaA4Tres_por_Sete);

    public static IReadOnlyList<FormatoOpcao> ListaPadrao { get; } =
        new[] { Bobina50x30, Bobina40x25, Bobina60x40, FolhaA4_3x7 };
}
