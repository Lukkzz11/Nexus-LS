using System.Globalization;
using System.Text;

namespace NexusLS.Core.Services;

/// <summary>
/// Montagem de CSV dos relatórios da Gestão (mesmo padrão do <see cref="ExportadorCsv"/>: ponto e vírgula, vírgula decimal,
/// dd/MM/aaaa, texto que parece fórmula ganha apóstrofo). Para gravar o arquivo use <see cref="ExportadorCsv.Salvar"/> (UTF-8 com BOM).
/// </summary>
internal sealed class CsvRelatorios
{
    private const char Separador = ';';
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private readonly StringBuilder _csv = new();

    public CsvRelatorios Linha(params string?[] campos)
    {
        _csv.Append(string.Join(Separador, campos.Select(Campo))).Append("\r\n");
        return this;
    }

    public override string ToString() => _csv.ToString();

    public static string Valor(long centavos) => Dinheiro.FormatarSimples(centavos);
    public static string Valor(long? centavos) => centavos is long c ? Dinheiro.FormatarSimples(c) : "";
    public static string N(long n) => n.ToString(CultureInfo.InvariantCulture);
    public static string Data(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string Pct(decimal? v) => v is decimal d ? Math.Round(d, 1, MidpointRounding.AwayFromZero).ToString("0.0", Pt) : "";

    private static string Campo(string? texto)
    {
        texto ??= "";
        if (texto.Length > 0 && texto[0] is '=' or '+' or '-' or '@' && !EhNumero(texto)) texto = "'" + texto;
        return texto.IndexOfAny(new[] { Separador, '"', '\r', '\n' }) >= 0
            ? "\"" + texto.Replace("\"", "\"\"") + "\""
            : texto;
    }

    private static bool EhNumero(string texto) => decimal.TryParse(texto, NumberStyles.Number, Pt, out _);
}
