using System.Text;

namespace NexusLS.Core.Services;

public enum TipoDocumento { Nenhum, Cpf, Cnpj }

/// <summary>Resultado de conferir um CPF/CNPJ: o tipo, só os dígitos (o que é guardado) e o motivo quando é inválido.</summary>
public record ResultadoDocumento(bool Valido, TipoDocumento Tipo, string Digitos, string? Erro);

/// <summary>CPF e CNPJ: aceita com ou sem máscara, confere os dígitos verificadores e devolve só os dígitos. Código puro.</summary>
public static class ValidadorDocumento
{
    public static string SoDigitos(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto) if (c is >= '0' and <= '9') sb.Append(c);
        return sb.ToString();
    }

    public static ResultadoDocumento Validar(string? texto)
    {
        var d = SoDigitos(texto);
        if (d.Length == 0) return new(false, TipoDocumento.Nenhum, "", "Informe o CPF ou o CNPJ.");
        if (d.Length == 11)
            return CpfValido(d) ? new(true, TipoDocumento.Cpf, d, null) : new(false, TipoDocumento.Cpf, d, "CPF inválido: confira os números.");
        if (d.Length == 14)
            return CnpjValido(d) ? new(true, TipoDocumento.Cnpj, d, null) : new(false, TipoDocumento.Cnpj, d, "CNPJ inválido: confira os números.");
        return new(false, TipoDocumento.Nenhum, d, "O CPF tem 11 números e o CNPJ tem 14.");
    }

    public static bool CpfValido(string? texto)
    {
        var d = SoDigitos(texto);
        if (d.Length != 11 || TodosIguais(d)) return false;
        return Digito(d, 9, 10) == d[9] - '0' && Digito(d, 10, 11) == d[10] - '0';
    }

    public static bool CnpjValido(string? texto)
    {
        var d = SoDigitos(texto);
        if (d.Length != 14 || TodosIguais(d)) return false;
        int[] p1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        int[] p2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        return Digito(d, p1) == d[12] - '0' && Digito(d, p2) == d[13] - '0';
    }

    /// <summary>"12345678909" vira "123.456.789-09"; CNPJ vira "12.345.678/0001-95". Texto fora do padrão volta como veio.</summary>
    public static string Formatar(string? texto)
    {
        var d = SoDigitos(texto);
        return d.Length switch
        {
            11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            _ => texto ?? ""
        };
    }

    /// <summary>
    /// Máscara enquanto se digita: até 11 dígitos vira CPF ("123.456.7"), de 12 a 14 vira CNPJ ("12.345.678/0001"); o que
    /// passar de 14 dígitos é cortado. Letras e símbolos são descartados.
    /// </summary>
    public static string FormatarDigitando(string? texto)
    {
        var d = SoDigitos(texto);
        if (d.Length > 14) d = d[..14];
        if (d.Length <= 11)
        {
            return d.Length switch
            {
                <= 3 => d,
                <= 6 => $"{d[..3]}.{d[3..]}",
                <= 9 => $"{d[..3]}.{d[3..6]}.{d[6..]}",
                _ => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}"
            };
        }
        return d.Length switch
        {
            <= 12 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..]}",
            _ => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}"
        };
    }

    private static bool TodosIguais(string d) => d.All(c => c == d[0]);

    private static int Digito(string d, int quantos, int pesoInicial)
    {
        var soma = 0;
        for (var i = 0; i < quantos; i++) soma += (d[i] - '0') * (pesoInicial - i);
        var resto = soma * 10 % 11;
        return resto == 10 ? 0 : resto;
    }

    private static int Digito(string d, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++) soma += (d[i] - '0') * pesos[i];
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
