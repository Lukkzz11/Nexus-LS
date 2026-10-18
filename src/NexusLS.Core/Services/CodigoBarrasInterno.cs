namespace NexusLS.Core.Services;

/// <summary>
/// Códigos de barras EAN-13 "da casa": começam com 2 (faixa que o mundo reserva para uso interno das lojas),
/// seguidos de 11 dígitos de sequência e do dígito verificador. Assim nunca colidem com códigos de fabricante.
/// </summary>
public static class CodigoBarrasInterno
{
    public const char Prefixo = '2';

    /// <summary>Dígito verificador do EAN-13: alterna pesos 1 e 3 nos 12 primeiros dígitos.</summary>
    public static int DigitoVerificador(string primeiros12)
    {
        if (primeiros12.Length != 12 || !primeiros12.All(char.IsAsciiDigit))
            throw new ArgumentException("São necessários 12 dígitos.", nameof(primeiros12));

        var soma = 0;
        for (var i = 0; i < 12; i++)
            soma += (primeiros12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - soma % 10) % 10;
    }

    /// <summary>Monta o código completo (13 dígitos) da sequência dada (1 a 99.999.999.999).</summary>
    public static string Montar(long sequencia)
    {
        if (sequencia < 1 || sequencia > 99_999_999_999)
            throw new ArgumentOutOfRangeException(nameof(sequencia));
        var base12 = Prefixo + sequencia.ToString("D11");
        return base12 + DigitoVerificador(base12);
    }

    /// <summary>13 dígitos com o verificador certo.</summary>
    public static bool EhEan13Valido(string? codigo) =>
        codigo is { Length: 13 } && codigo.All(char.IsAsciiDigit)
        && DigitoVerificador(codigo[..12]) == codigo[12] - '0';

    /// <summary>Se o código é um interno válido, devolve a sequência dele; senão, nulo.</summary>
    public static long? SequenciaDe(string? codigo) =>
        codigo is not null && codigo[0] == Prefixo && EhEan13Valido(codigo)
            ? long.Parse(codigo.AsSpan(1, 11))
            : null;
}
