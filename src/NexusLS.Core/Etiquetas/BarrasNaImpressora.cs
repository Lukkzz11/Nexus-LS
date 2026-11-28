namespace NexusLS.Core.Etiquetas;

/// <summary>Onde o código de barras começa e quanto mede cada módulo, em DIPs (1/96 pol).</summary>
public sealed record PosicaoBarras(double InicioDip, double ModuloDip, int PontosPorModulo);

/// <summary>
/// Encaixa o código de barras na grade de pontos da impressora. Numa térmica de 203 dpi cada ponto mede 0,125 mm:
/// se o módulo (barra mais fina) não for um número inteiro de pontos, o driver arredonda cada barra de um jeito
/// (2, 3, 2, 2...) e o leitor falha. Aqui o módulo é sempre inteiro e o começo cai em cima de um ponto.
/// </summary>
public static class BarrasNaImpressora
{
    public const int DpiPadrao = 203;

    /// <summary>Módulo maior que isto não ajuda o leitor e deixa o código largo demais.</summary>
    public const double ModuloMaximoMm = 0.5;

    /// <summary>Módulo confortável para o leitor (3 pontos em 203 dpi).</summary>
    public const double ModuloConfortavelMm = 0.375;

    /// <summary>Um ponto da impressora, em DIPs.</summary>
    public static double PontoDip(int dpi) => 96.0 / (dpi > 0 ? dpi : DpiPadrao);

    /// <summary>Arredonda uma medida (em DIPs, a partir do canto da página) para o ponto mais próximo.</summary>
    public static double NoPonto(double dip, int dpi) => Math.Round(dip / PontoDip(dpi)) * PontoDip(dpi);

    /// <summary>
    /// <paramref name="inicioDip"/> e <paramref name="larguraDip"/> são o espaço reservado ao código, medido na página.
    /// As folgas são o branco que sobra de cada lado até a borda da etiqueta: a zona de silêncio pode usá-lo.
    /// </summary>
    public static PosicaoBarras Calcular(
        int modulos,
        double inicioDip,
        double larguraDip,
        double folgaEsquerdaDip,
        double folgaDireitaDip,
        int dpi = DpiPadrao,
        int silencio = LegibilidadeCodigo.ModulosDeSilencio)
    {
        if (modulos <= 0) throw new ArgumentOutOfRangeException(nameof(modulos));
        if (dpi <= 0) dpi = DpiPadrao;

        var ponto = PontoDip(dpi);
        var folgaE = Math.Max(0, folgaEsquerdaDip);
        var folgaD = Math.Max(0, folgaDireitaDip);
        var largura = larguraDip / ponto;
        var comSilencio = modulos + 2 * silencio;
        var etiqueta = (larguraDip + folgaE + folgaD) / ponto;

        var maximo = Math.Max(1, (int)Math.Round(ModuloMaximoMm / 25.4 * dpi));
        // 0,25 mm (2 pontos em 203 dpi) é o menor módulo que os leitores leem bem.
        var minimo = Math.Max(1, (int)Math.Round(LegibilidadeCodigo.ModuloMinimoMm / 25.4 * dpi));

        var pontos = (int)Math.Min(largura / modulos, (largura + 2 * Math.Min(folgaE, folgaD) / ponto) / comSilencio);
        pontos = Math.Min(pontos, maximo);

        // Quando o espaço reservado já pega quase a etiqueta toda, o que sobra dos lados é só margem: vale usá-la
        // para as barras não ficarem no limite do leitor.
        var confortavel = (int)Math.Round(ModuloConfortavelMm / 25.4 * dpi);
        if (pontos < confortavel && largura >= 0.7 * etiqueta && comSilencio * confortavel <= etiqueta)
            pontos = confortavel;

        // Código comprido num espaço estreito: melhor passar um pouco do espaço reservado do que sair ilegível.
        if (pontos < minimo)
            pontos = Math.Max(1, Math.Min(minimo, (int)(etiqueta / comSilencio)));

        var barras = pontos * modulos;
        var inicio = Math.Round(inicioDip / ponto + (largura - barras) / 2);

        // Não deixa a zona de silêncio sair da etiqueta (quando ela cabe).
        var menor = Math.Ceiling((inicioDip - folgaE) / ponto + silencio * pontos - 0.001);
        var maior = Math.Floor((inicioDip + larguraDip + folgaD) / ponto - silencio * pontos - barras + 0.001);
        if (menor <= maior) inicio = Math.Clamp(inicio, menor, maior);

        return new PosicaoBarras(inicio * ponto, pontos * ponto, pontos);
    }
}
