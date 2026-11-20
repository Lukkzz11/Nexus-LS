namespace NexusLS.Core.Etiquetas;

/// <summary>
/// Como o papel da etiqueta vem na impressora. Muda o sensor que a impressora usa para achar
/// o começo de cada etiqueta: espaço (gap), marca preta (black mark) ou papel contínuo sem marca.
/// </summary>
public enum TipoMidiaEtiqueta
{
    /// <summary>Rolo com etiquetas separadas por um espaço vazio (gap).</summary>
    RoloComGap,

    /// <summary>Rolo com uma marca preta atrás de cada etiqueta (black mark).</summary>
    RoloMarcaPreta,

    /// <summary>Rolo contínuo, sem gap nem marca preta: o corte é pela altura configurada.</summary>
    RoloContinuo,

    /// <summary>Folha (A4 ou carta) com várias etiquetas adesivas.</summary>
    Folha
}

/// <summary>
/// Perfil de referência de uma impressora. Não é uma promessa de compatibilidade universal:
/// as medidas vêm de fichas técnicas públicas e devem ser conferidas no manual do modelo.
/// </summary>
public sealed record PerfilImpressora(
    string Nome,
    double LarguraMaximaMm,
    int ResolucaoDpi,
    IReadOnlyList<TipoMidiaEtiqueta> MidiasSuportadas,
    string Observacao)
{
    public bool Aceita(TipoMidiaEtiqueta tipo) => MidiasSuportadas.Contains(tipo);

    /// <summary>Perfis usados como ponto de partida. O operador sempre pode informar outra largura.</summary>
    public static IReadOnlyList<PerfilImpressora> Perfis { get; } = new[]
    {
        // Ficha técnica Zebra ZD230: impressão de até 104 mm, rolo ou fan-fold,
        // sensor de gap e sensor de marca preta móvel. Servem de base para as térmicas de 4".
        new PerfilImpressora(
            "Térmica 4\" (ex.: Zebra ZD230, Elgin L42, Argox OS-214)",
            104, 203,
            new[] { TipoMidiaEtiqueta.RoloComGap, TipoMidiaEtiqueta.RoloMarcaPreta, TipoMidiaEtiqueta.RoloContinuo },
            "Perfil de referência de 4 polegadas / 203 dpi. A ficha da Zebra ZD230 indica até 104 mm de impressão e sensores de gap e marca preta. Confirme mídia e sensor no manual do seu modelo."),

        new PerfilImpressora(
            "Térmica compacta 2\"",
            56, 203,
            new[] { TipoMidiaEtiqueta.RoloComGap, TipoMidiaEtiqueta.RoloMarcaPreta, TipoMidiaEtiqueta.RoloContinuo },
            "Perfil de referência de 2 polegadas. Use somente etiquetas dentro da largura máxima informada no manual."),

        new PerfilImpressora(
            "Impressora de folhas A4 (jato de tinta/laser)",
            210, 0,
            new[] { TipoMidiaEtiqueta.Folha },
            "Para folhas adesivas A4. Ajuste o tipo de papel no driver do Windows e use exatamente o gabarito do fabricante da folha."),

        new PerfilImpressora(
            "Outra / personalizada",
            210, 203,
            new[] { TipoMidiaEtiqueta.RoloComGap, TipoMidiaEtiqueta.RoloMarcaPreta, TipoMidiaEtiqueta.RoloContinuo, TipoMidiaEtiqueta.Folha },
            "Informe a largura máxima aceita pela sua impressora. Consulte o manual antes de imprimir.")
    };
}

/// <summary>
/// Um formato de mídia: o tamanho da etiqueta e como ela se distribui na página/rolo.
/// Todas as medidas em milímetros. Os valores são copiados para a tela de preparação,
/// onde o operador pode ajustar e cadastrar uma mídia personalizada.
/// </summary>
public sealed record PerfilMidia(
    string Nome,
    TipoMidiaEtiqueta Tipo,
    double LarguraEtiquetaMm,
    double AlturaEtiquetaMm,
    double EspacoHorizontalMm,
    double EspacoVerticalMm,
    int Colunas,
    int Linhas,
    double PaginaLarguraMm,
    double PaginaAlturaMm,
    double MargemEsquerdaMm,
    double MargemSuperiorMm,
    string Observacao = "")
{
    public int EtiquetasPorPagina => Math.Max(0, Colunas) * Math.Max(0, Linhas);

    public bool EhRolo => Tipo != TipoMidiaEtiqueta.Folha;

    /// <summary>Formato que o paginador existente entende. Reaproveita a regra de impressão do Nexus.</summary>
    public FormatoEtiqueta Formato => new(
        Nome,
        PaginaLarguraMm, PaginaAlturaMm,
        Colunas, Linhas,
        LarguraEtiquetaMm, AlturaEtiquetaMm,
        MargemEsquerdaMm, MargemSuperiorMm,
        EspacoHorizontalMm, EspacoVerticalMm);

    /// <summary>Frase curta para a conferência na tela.</summary>
    public string Resumo => EhRolo
        ? $"{Nome} · etiqueta {LarguraEtiquetaMm:0.#} × {AlturaEtiquetaMm:0.#} mm · {DescreverSensor()}"
        : $"{Nome} · {EtiquetasPorPagina} etiqueta(s) por folha · {LarguraEtiquetaMm:0.#} × {AlturaEtiquetaMm:0.#} mm";

    /// <summary>Medida da etiqueta, usada nos cartões de escolha de mídia.</summary>
    public string Dimensoes => $"{LarguraEtiquetaMm:0.#} × {AlturaEtiquetaMm:0.#} mm";

    /// <summary>Como as etiquetas se distribuem, em uma linha curta.</summary>
    public string Distribuicao => EhRolo
        ? Colunas > 1 ? $"{Colunas} colunas no rolo" : "1 etiqueta por vez"
        : $"{EtiquetasPorPagina} por folha · {Colunas} × {Linhas}";

    public string DescreverSensor() => Tipo switch
    {
        TipoMidiaEtiqueta.RoloComGap => $"sensor de gap ({EspacoVerticalMm:0.#} mm entre etiquetas)",
        TipoMidiaEtiqueta.RoloMarcaPreta => "sensor de marca preta (black mark)",
        TipoMidiaEtiqueta.RoloContinuo => "papel contínuo (sem gap nem marca preta)",
        TipoMidiaEtiqueta.Folha => "folha adesiva",
        _ => "mídia"
    };

    /// <summary>Confere as medidas e a largura máxima da impressora. Lista vazia = pode imprimir.</summary>
    public IReadOnlyList<string> Validar(double? larguraMaximaDaImpressoraMm = null)
    {
        var erros = new List<string>();

        if (Colunas < 1 || Linhas < 1)
            erros.Add("A mídia precisa de pelo menos 1 coluna e 1 linha.");
        if (LarguraEtiquetaMm <= 0 || AlturaEtiquetaMm <= 0)
            erros.Add("A etiqueta precisa ter largura e altura maiores que zero.");
        if (PaginaLarguraMm <= 0 || PaginaAlturaMm <= 0)
            erros.Add("O papel precisa ter largura e altura maiores que zero.");
        if (EspacoHorizontalMm < 0 || EspacoVerticalMm < 0)
            erros.Add("O espaço entre etiquetas não pode ser negativo.");
        if (MargemEsquerdaMm < 0 || MargemSuperiorMm < 0)
            erros.Add("As margens não podem ser negativas.");
        if (Tipo == TipoMidiaEtiqueta.RoloComGap && EspacoVerticalMm <= 0)
            erros.Add("Mídia com gap precisa de um espaço maior que zero entre as etiquetas.");

        erros.AddRange(Formato.Validar(larguraMaximaDaImpressoraMm));

        // Repete mensagens iguais quando a mesma inconsistência é vista por dois caminhos.
        return erros.Distinct(StringComparer.Ordinal).ToList();
    }

    public PerfilMidia ComNome(string nome) => this with { Nome = nome };
}

/// <summary>Formatos de mídia mais comuns, prontos para escolher. Não substituem o manual da impressora.</summary>
public static class PerfisMidia
{
    public static readonly PerfilMidia Rolo50x30Gap = new(
        "Rolo 50 × 30 mm (gap 3 mm)", TipoMidiaEtiqueta.RoloComGap,
        50, 30, 0, 3, 1, 1, 50, 30, 0, 0,
        "Formato térmico comercial muito usado em roupas e acessórios.");

    public static readonly PerfilMidia Rolo40x25Gap = new(
        "Rolo 40 × 25 mm (gap 2 mm)", TipoMidiaEtiqueta.RoloComGap,
        40, 25, 0, 2, 1, 1, 40, 25, 0, 0,
        "Formato compacto para etiquetas pequenas e gôndolas.");

    public static readonly PerfilMidia Rolo60x40Gap = new(
        "Rolo 60 × 40 mm (gap 3 mm)", TipoMidiaEtiqueta.RoloComGap,
        60, 40, 0, 3, 1, 1, 60, 40, 0, 0,
        "Formato maior, com espaço para mais informações e preço.");

    public static readonly PerfilMidia Rolo100x50Gap = new(
        "Rolo 100 × 50 mm (gap 3 mm)", TipoMidiaEtiqueta.RoloComGap,
        100, 50, 0, 3, 1, 1, 100, 50, 0, 0,
        "Formato de expedição, também aceito por térmicas de 4\" dentro da largura máxima.");

    public static readonly PerfilMidia Rolo50x30Continuo = new(
        "Rolo contínuo 50 × 30 mm", TipoMidiaEtiqueta.RoloContinuo,
        50, 30, 0, 0, 1, 1, 50, 30, 0, 0,
        "Sem gap nem marca preta: a impressora corta pela altura configurada.");

    public static readonly PerfilMidia Rolo50x30MarcaPreta = new(
        "Rolo 50 × 30 mm com marca preta", TipoMidiaEtiqueta.RoloMarcaPreta,
        50, 30, 0, 0, 1, 1, 50, 30, 0, 0,
        "A impressora posiciona pela marca preta atrás da etiqueta.");

    public static readonly PerfilMidia Rolo2Colunas50x30Gap = new(
        "Rolo 2 colunas · 50 × 30 mm (gap 3 mm)", TipoMidiaEtiqueta.RoloComGap,
        50, 30, 3, 0, 2, 1, 106, 30, 1.5, 0,
        "Duas etiquetas lado a lado por fileira; confira a largura do papel no manual.");

    // Folha A4 3 × 7: 63,5 × 38,1 mm. É o gabarito equivalente ao Pimaco A4260/A4360
    // (21 etiquetas por folha) e ao Avery L7160. Medidas conferidas com o catálogo do fabricante.
    public static readonly PerfilMidia FolhaA4TresPorSete = new(
        "Folha A4 · 3 × 7 (63,5 × 38,1 mm)", TipoMidiaEtiqueta.Folha,
        63.5, 38.1, 2.5, 0, 3, 7, 210, 297, 7.2, 15.15,
        "21 etiquetas por folha. Use o gabarito do fabricante (Pimaco A4260/A4360 ou equivalente).");

    public static readonly PerfilMidia Personalizado = new(
        "Personalizado (informar medidas)", TipoMidiaEtiqueta.RoloComGap,
        50, 30, 0, 3, 1, 1, 50, 30, 0, 0,
        "Ajuste largura, altura, gap, colunas e margens conforme a mídia que você usa.");

    public static IReadOnlyList<PerfilMidia> ListaPadrao { get; } = new[]
    {
        Rolo50x30Gap,
        Rolo40x25Gap,
        Rolo60x40Gap,
        Rolo100x50Gap,
        Rolo50x30Continuo,
        Rolo50x30MarcaPreta,
        Rolo2Colunas50x30Gap,
        FolhaA4TresPorSete,
        Personalizado
    };
}
