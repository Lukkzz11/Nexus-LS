namespace NexusLS.BarTender.Models.Designer;

/// <summary>
/// Retângulo em milímetros, sem depender de WPF (para poder ser testado).
/// Aceita canto em qualquer ordem: use <see cref="DeDoisPontos"/>.
/// </summary>
public readonly record struct RetanguloMm(double X, double Y, double Largura, double Altura)
{
    public double Direita => X + Largura;
    public double Base => Y + Altura;

    public static RetanguloMm DeDoisPontos(double x1, double y1, double x2, double y2)
        => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    /// <summary>Interseção inclusiva: encostar na borda conta como tocar.</summary>
    public bool Toca(RetanguloMm outro)
        => X <= outro.Direita && outro.X <= Direita
        && Y <= outro.Base && outro.Y <= Base;

    /// <summary>
    /// Caixa que envolve o retângulo depois de girar em torno do centro
    /// (é assim que o editor desenha a rotação).
    /// </summary>
    public RetanguloMm ComRotacao(double graus)
    {
        var g = ((graus % 360.0) + 360.0) % 360.0;
        if (g == 0.0) return this;

        var rad = g * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        var novaLargura = (Largura * cos) + (Altura * sin);
        var novaAltura = (Largura * sin) + (Altura * cos);
        var cx = X + (Largura / 2.0);
        var cy = Y + (Altura / 2.0);
        return new RetanguloMm(cx - (novaLargura / 2.0), cy - (novaAltura / 2.0), novaLargura, novaAltura);
    }
}

/// <summary>Cálculo puro da seleção por retângulo (marquee) na área de criação.</summary>
public static class SelecaoPorRetangulo
{
    /// <summary>Arraste menor que isto (em pixels de tela) é tratado como um clique simples.</summary>
    public const double LimiteArrastePx = 4.0;

    public static bool PassouDoLimite(double dxPx, double dyPx)
        => Math.Abs(dxPx) > LimiteArrastePx || Math.Abs(dyPx) > LimiteArrastePx;

    /// <summary>Área ocupada pelo elemento, já com rotação. Linhas finas ganham espessura mínima.</summary>
    public static RetanguloMm AreaDoElemento(ElementoDesign el)
    {
        const double minimo = 0.5;
        var largura = Math.Max(el.Largura, minimo);
        var altura = Math.Max(el.Altura, minimo);
        return new RetanguloMm(el.X, el.Y, largura, altura).ComRotacao(el.Rotacao);
    }

    /// <summary>Elementos que o retângulo toca. Ocultos e bloqueados nunca são selecionáveis.</summary>
    public static List<ElementoDesign> ElementosTocados(IEnumerable<ElementoDesign> elementos, RetanguloMm retangulo)
        => elementos
            .Where(e => e.Visivel && !e.Bloqueado && retangulo.Toca(AreaDoElemento(e)))
            .ToList();

    /// <summary>
    /// Seleção final: sem Ctrl/Shift só vale o que o retângulo tocou; com Ctrl/Shift soma
    /// ao que já estava selecionado. Mantém a ordem de empilhamento original.
    /// </summary>
    public static List<ElementoDesign> Resolver(
        IReadOnlyList<ElementoDesign> todos,
        IReadOnlyCollection<ElementoDesign> selecaoInicial,
        RetanguloMm retangulo,
        bool somar)
    {
        var tocados = ElementosTocados(todos, retangulo);
        return todos
            .Where(e => tocados.Contains(e) || (somar && selecaoInicial.Contains(e) && e.Visivel && !e.Bloqueado))
            .ToList();
    }
}
