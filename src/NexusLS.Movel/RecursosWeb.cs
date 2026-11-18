using System.Reflection;

namespace NexusLS.Movel;

/// <summary>
/// A tela do celular vem embutida no executavel (recursos do assembly), nao de uma pasta ao lado.
/// Assim funciona igual rodando pelo codigo, pelo executavel publicado e nos testes.
/// </summary>
internal static class RecursosWeb
{
    private const string Prefixo = "web/";

    private static readonly Lazy<Dictionary<string, Arquivo>> Arquivos = new(Carregar, isThreadSafe: true);

    public sealed record Arquivo(string Tipo, byte[] Conteudo);

    public static bool TentarObter(string caminho, out Arquivo arquivo)
    {
        var nome = Normalizar(caminho);
        return Arquivos.Value.TryGetValue(nome, out arquivo!);
    }

    private static string Normalizar(string caminho)
    {
        var limpo = (caminho ?? "/").Split('?', '#')[0];
        limpo = Uri.UnescapeDataString(limpo).Trim();
        if (!limpo.StartsWith('/')) limpo = "/" + limpo;
        if (limpo == "/") return "index.html";
        return limpo.TrimStart('/').Replace('\\', '/');
    }

    private static Dictionary<string, Arquivo> Carregar()
    {
        var mapa = new Dictionary<string, Arquivo>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(RecursosWeb).Assembly;

        foreach (var nome in assembly.GetManifestResourceNames())
        {
            var normalizado = nome.Replace('\\', '/');
            if (!normalizado.StartsWith(Prefixo, StringComparison.Ordinal)) continue;

            var relativo = normalizado[Prefixo.Length..];

            using var fluxo = assembly.GetManifestResourceStream(nome);
            if (fluxo is null) continue;

            using var memoria = new MemoryStream();
            fluxo.CopyTo(memoria);
            mapa[relativo] = new Arquivo(TipoDe(relativo), memoria.ToArray());
        }

        return mapa;
    }

    private static string TipoDe(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".webmanifest" => "application/manifest+json; charset=utf-8",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".json" => "application/json; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };
}
