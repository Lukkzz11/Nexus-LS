using System.Globalization;

namespace NexusLS.Core.Fiscal;

/// <summary>Chave de acesso de 44 dígitos da NF-e/NFC-e e o dígito verificador (módulo 11). Código puro, sem banco nem rede.</summary>
public static class ChaveAcessoNfce
{
    /// <summary>Código IBGE de cada UF (usado no começo da chave e em cUF).</summary>
    public static readonly IReadOnlyDictionary<string, int> CodigoUf = new Dictionary<string, int>
    {
        ["RO"] = 11, ["AC"] = 12, ["AM"] = 13, ["RR"] = 14, ["PA"] = 15, ["AP"] = 16, ["TO"] = 17,
        ["MA"] = 21, ["PI"] = 22, ["CE"] = 23, ["RN"] = 24, ["PB"] = 25, ["PE"] = 26, ["AL"] = 27, ["SE"] = 28, ["BA"] = 29,
        ["MG"] = 31, ["ES"] = 32, ["RJ"] = 33, ["SP"] = 35,
        ["PR"] = 41, ["SC"] = 42, ["RS"] = 43,
        ["MS"] = 50, ["MT"] = 51, ["GO"] = 52, ["DF"] = 53,
    };

    /// <summary>Dígito verificador (módulo 11, pesos 2 a 9 da direita para a esquerda) dos 43 primeiros dígitos.</summary>
    public static int CalcularDigito(string quarentaETresDigitos)
    {
        if (quarentaETresDigitos is null || quarentaETresDigitos.Length != 43 || !quarentaETresDigitos.All(char.IsAsciiDigit))
            throw new ArgumentException("A base da chave deve ter exatamente 43 números.", nameof(quarentaETresDigitos));
        int soma = 0, peso = 2;
        for (int i = quarentaETresDigitos.Length - 1; i >= 0; i--)
        {
            soma += (quarentaETresDigitos[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        int resto = soma % 11;
        return resto is 0 or 1 ? 0 : 11 - resto;
    }

    /// <summary>Monta a chave: cUF + AAMM + CNPJ + modelo + série(3) + número(9) + tpEmis + cNF(8) + DV.</summary>
    public static string Montar(int codigoUf, DateTime emissao, string cnpj14, int modelo, int serie, int numero, int tipoEmissao, int codigoNumerico)
    {
        if (cnpj14 is null || cnpj14.Length != 14 || !cnpj14.All(char.IsAsciiDigit)) throw new ArgumentException("O CNPJ deve ter 14 números.", nameof(cnpj14));
        if (serie is < 0 or > 999) throw new ArgumentOutOfRangeException(nameof(serie));
        if (numero is < 1 or > 999_999_999) throw new ArgumentOutOfRangeException(nameof(numero));
        if (codigoNumerico is < 0 or > 99_999_999) throw new ArgumentOutOfRangeException(nameof(codigoNumerico));
        var baseChave = string.Create(CultureInfo.InvariantCulture,
            $"{codigoUf:00}{emissao:yyMM}{cnpj14}{modelo:00}{serie:000}{numero:000000000}{tipoEmissao:0}{codigoNumerico:00000000}");
        return baseChave + CalcularDigito(baseChave).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A chave tem 44 números e o último confere com o cálculo?</summary>
    public static bool Valida(string? chave) =>
        chave is { Length: 44 } && chave.All(char.IsAsciiDigit) && CalcularDigito(chave[..43]) == chave[43] - '0';
}
