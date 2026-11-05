namespace NexusLS.Core.Etiquetas;

/// <summary>
/// Um modelo de papel de etiqueta: o tamanho da página e como as etiquetas estão distribuídas nela.
/// Todas as medidas em milímetros (o desenho converte para a unidade da impressora).
/// </summary>
public record FormatoEtiqueta(
    string Nome,
    double PaginaLarguraMm, double PaginaAlturaMm,
    int Colunas, int Linhas,
    double EtiquetaLarguraMm, double EtiquetaAlturaMm,
    double MargemEsquerdaMm, double MargemSuperiorMm,
    double EspacoHorizontalMm, double EspacoVerticalMm)
{
    public int EtiquetasPorPagina => Colunas * Linhas;

    /// <summary>Bobina de impressora de etiquetas: uma etiqueta por "página" (o papel é do tamanho da etiqueta).</summary>
    public static readonly FormatoEtiqueta Bobina50x30 = new(
        "Bobina 50 × 30 mm (uma por vez)", 50, 30, 1, 1, 50, 30, 0, 0, 0, 0);

    public static readonly FormatoEtiqueta Bobina40x25 = new(
        "Bobina 40 × 25 mm (uma por vez)", 40, 25, 1, 1, 40, 25, 0, 0, 0, 0);

    public static readonly FormatoEtiqueta Bobina60x40 = new(
        "Bobina 60 × 40 mm (uma por vez)", 60, 40, 1, 1, 60, 40, 0, 0, 0, 0);

    /// <summary>Folha A4 adesiva comum (modelo Avery L7160 e equivalentes): 3 colunas × 7 linhas.</summary>
    public static readonly FormatoEtiqueta FolhaA4Tres_por_Sete = new(
        "Folha A4 · 3 × 7 (63,5 × 38,1 mm)", 210, 297, 3, 7, 63.5, 38.1, 7.2, 15.15, 2.5, 0);

    public static IReadOnlyList<FormatoEtiqueta> Todos { get; } =
        new[] { Bobina50x30, Bobina40x25, FolhaA4Tres_por_Sete };
}

/// <summary>Uma etiqueta a imprimir: o que aparece nela e quantas cópias.</summary>
/// <param name="Tamanho">Vazio quando o produto não tem tamanho.</param>
/// <param name="CodigoBarras">Nulo ou vazio: a etiqueta sai sem código de barras.</param>
public record ItemEtiqueta(
    int ProdutoId, string Nome, string Tamanho, long PrecoCentavos, string? CodigoBarras, int Quantidade);

/// <summary>Onde uma etiqueta fica na página (canto superior esquerdo, em mm).</summary>
public record PosicaoEtiqueta(double XMm, double YMm, ItemEtiqueta Item);

/// <summary>Espalha as etiquetas pelas páginas do formato escolhido.</summary>
public static class DiagramadorEtiquetas
{
    /// <summary>
    /// Devolve uma lista de páginas, cada uma com as posições das suas etiquetas. Cada cópia de cada item
    /// ocupa uma posição, da esquerda para a direita e de cima para baixo.
    /// </summary>
    /// <param name="primeiraPosicao">
    /// Em qual etiqueta da primeira folha começar (1 = a primeira). Serve para aproveitar uma folha
    /// que já foi usada em parte: as posições anteriores ficam vazias.
    /// </param>
    public static List<List<PosicaoEtiqueta>> Paginar(
        IEnumerable<ItemEtiqueta> itens, FormatoEtiqueta formato, int primeiraPosicao = 1)
    {
        var porPagina = formato.EtiquetasPorPagina;
        if (porPagina < 1) throw new ArgumentException("O formato não tem etiquetas.", nameof(formato));
        if (primeiraPosicao < 1 || primeiraPosicao > porPagina)
            throw new ArgumentOutOfRangeException(nameof(primeiraPosicao),
                $"A primeira posição deve ficar entre 1 e {porPagina}.");

        var paginas = new List<List<PosicaoEtiqueta>>();
        var indice = primeiraPosicao - 1; // posição absoluta contando do início da primeira página

        foreach (var item in itens)
        {
            for (var copia = 0; copia < item.Quantidade; copia++, indice++)
            {
                var pagina = indice / porPagina;
                var naPagina = indice % porPagina;
                var coluna = naPagina % formato.Colunas;
                var linha = naPagina / formato.Colunas;

                while (paginas.Count <= pagina) paginas.Add(new List<PosicaoEtiqueta>());
                paginas[pagina].Add(new PosicaoEtiqueta(
                    formato.MargemEsquerdaMm + coluna * (formato.EtiquetaLarguraMm + formato.EspacoHorizontalMm),
                    formato.MargemSuperiorMm + linha * (formato.EtiquetaAlturaMm + formato.EspacoVerticalMm),
                    item));
            }
        }
        return paginas;
    }

    public static int TotalDeEtiquetas(IEnumerable<ItemEtiqueta> itens) => itens.Sum(i => i.Quantidade);
}
