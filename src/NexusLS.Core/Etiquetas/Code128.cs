namespace NexusLS.Core.Etiquetas;

/// <summary>
/// Código de barras Code 128 (o mais usado em etiquetas de produto: aceita letras, números e símbolos).
/// Usa o conjunto C (dois dígitos por símbolo, mais curto) quando o texto é só de números com quantidade par
/// (e, com quantidade ímpar, depois do primeiro dígito), e o conjunto B nos demais casos. Devolve a sequência de módulos (true = barra preta, false = espaço branco),
/// pronta para ser desenhada com qualquer largura de módulo. Não desenha nada: só calcula.
/// </summary>
public static class Code128
{
    // Largura, em módulos, de cada barra e espaço alternados (barra, espaço, barra, espaço, barra, espaço) de cada
    // símbolo. Índice = valor do símbolo (0 a 102 = caracteres, 103/104/105 = início A/B/C, 106 = fim).
    private static readonly string[] Padroes =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    };

    public const int InicioB = 104;
    public const int InicioC = 105;
    public const int TrocaParaC = 99;
    public const int Fim = 106;

    /// <summary>Tamanho do padrão de um símbolo (normal 11 módulos; o símbolo de fim tem 13).</summary>
    public static IReadOnlyList<string> TabelaDePadroes => Padroes;

    /// <summary>Quantos caracteres cabem: tudo que for ASCII imprimível (espaço até "~").</summary>
    public static bool PodeCodificar(string? texto) =>
        !string.IsNullOrEmpty(texto) && texto.All(c => c is >= ' ' and <= '~');

    /// <summary>Os símbolos (valores 0 a 106) do código, com início, dígito verificador e fim.</summary>
    public static List<int> Simbolos(string texto)
    {
        if (!PodeCodificar(texto))
            throw new ArgumentException("O código de barras só aceita letras, números e símbolos comuns (sem acentos).", nameof(texto));

        var simbolos = new List<int>();
        if (texto.All(char.IsAsciiDigit) && texto.Length % 2 == 0)
        {
            simbolos.Add(InicioC);
            for (var i = 0; i < texto.Length; i += 2)
                simbolos.Add(int.Parse(texto.AsSpan(i, 2)));     // dois dígitos = um símbolo
        }
        else if (texto.All(char.IsAsciiDigit) && texto.Length >= 5)
        {
            // Quantidade ímpar de dígitos (ex.: EAN-13): o primeiro dígito vai no conjunto B, depois troca para o C
            // (símbolo 99) e o resto sai em pares. Fica bem mais curto que tudo no B, o que ajuda a caber em etiqueta pequena.
            simbolos.Add(InicioB);
            simbolos.Add(texto[0] - ' ');
            simbolos.Add(TrocaParaC);
            for (var i = 1; i < texto.Length; i += 2)
                simbolos.Add(int.Parse(texto.AsSpan(i, 2)));
        }
        else
        {
            simbolos.Add(InicioB);
            foreach (var c in texto) simbolos.Add(c - ' ');       // conjunto B: valor = código ASCII - 32
        }

        // Dígito verificador: soma de (valor × posição), a posição do início vale 1, e resto da divisão por 103.
        var soma = simbolos[0];
        for (var i = 1; i < simbolos.Count; i++) soma += simbolos[i] * i;
        simbolos.Add(soma % 103);
        simbolos.Add(Fim);
        return simbolos;
    }

    /// <summary>Módulos do código completo: true = barra, false = espaço. O "silêncio" (margem branca) fica por conta de quem desenha.</summary>
    public static List<bool> Modulos(string texto)
    {
        var modulos = new List<bool>();
        foreach (var simbolo in Simbolos(texto))
        {
            var preto = true; // todo símbolo começa por uma barra
            foreach (var largura in Padroes[simbolo])
            {
                for (var i = 0; i < largura - '0'; i++) modulos.Add(preto);
                preto = !preto;
            }
        }
        return modulos;
    }
}
