using System.Reflection;
using System.Text.RegularExpressions;

namespace NexusLS.Core.Atualizacao;

/// <summary>Versão no formato 1.2.3 (maior.menor.correção). Aceita "v1.2.3", "1.2" e "1.2.3+abc123".</summary>
public readonly record struct VersaoPrograma(int Maior, int Menor, int Correcao) : IComparable<VersaoPrograma>
{
    private static readonly Regex Padrao = new(@"^\s*v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.\d+)?(?:[-+].*)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParse(string? texto, out VersaoPrograma versao)
    {
        versao = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var m = Padrao.Match(texto);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out var maior)) return false;
        var menor = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var mn) ? mn : 0;
        var correcao = m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var c) ? c : 0;
        versao = new VersaoPrograma(maior, menor, correcao);
        return true;
    }

    public static VersaoPrograma Parse(string texto) =>
        TryParse(texto, out var v) ? v : throw new FormatException($"Versão inválida: \"{texto}\".");

    public int CompareTo(VersaoPrograma o) =>
        Maior != o.Maior ? Maior.CompareTo(o.Maior) : Menor != o.Menor ? Menor.CompareTo(o.Menor) : Correcao.CompareTo(o.Correcao);

    public static bool operator <(VersaoPrograma a, VersaoPrograma b) => a.CompareTo(b) < 0;
    public static bool operator >(VersaoPrograma a, VersaoPrograma b) => a.CompareTo(b) > 0;
    public static bool operator <=(VersaoPrograma a, VersaoPrograma b) => a.CompareTo(b) <= 0;
    public static bool operator >=(VersaoPrograma a, VersaoPrograma b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Maior}.{Menor}.{Correcao}";

    /// <summary>A versão do programa que está rodando (vem do arquivo, definida na hora de publicar). Sem informação, vale 1.0.0.</summary>
    public static VersaoPrograma Atual(Assembly? programa = null)
    {
        programa ??= Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informacional = programa.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParse(informacional, out var v)) return v;
        var arquivo = programa.GetName().Version;
        return arquivo is null ? new VersaoPrograma(1, 0, 0) : new VersaoPrograma(arquivo.Major, Math.Max(arquivo.Minor, 0), Math.Max(arquivo.Build, 0));
    }
}
