namespace NexusLS.Core.Services;

/// <summary>
/// Preparação para a NFC-e: regimes, ambientes e a lista "o que falta para poder emitir". Não emite nada
/// e não guarda certificado nem o código secreto do CSC.
/// </summary>
public static class PreparoFiscal
{
    public const string Homologacao = "Homologacao";
    public const string Producao = "Producao";

    public static readonly IReadOnlyDictionary<string, string> Regimes = new Dictionary<string, string>
    {
        ["SimplesNacional"] = "Simples Nacional",
        ["Mei"] = "MEI",
        ["LucroPresumidoReal"] = "Lucro presumido / real",
    };

    public sealed record Item(string Texto, bool Ok);

    /// <summary>Confere o que já está pronto. O certificado digital e o CSC secreto ficam de fora: dependem de contrato com o provedor ou da SEFAZ.</summary>
    public static IReadOnlyList<Item> Verificar(string? documentoLoja, string? inscricaoEstadual, string? cidadeUf,
        string? regime, int serie, string? cscId)
    {
        var doc = ValidadorDocumento.Validar(documentoLoja);
        return new Item[]
        {
            new("CNPJ da loja válido (em Loja)", doc.Valido && doc.Tipo == TipoDocumento.Cnpj),
            new("Inscrição estadual preenchida (em Loja)", !string.IsNullOrWhiteSpace(inscricaoEstadual)),
            new("Cidade/UF preenchida (em Loja)", !string.IsNullOrWhiteSpace(cidadeUf)),
            new("Regime tributário escolhido", !string.IsNullOrWhiteSpace(regime) && Regimes.ContainsKey(regime)),
            new("Série da nota definida", serie is >= 1 and <= 999),
            new("ID do CSC informado", !string.IsNullOrWhiteSpace(cscId)),
        };
    }
}
