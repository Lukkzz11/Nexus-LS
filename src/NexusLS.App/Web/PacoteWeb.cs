using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace NexusLS.App.Web;

/// <summary>
/// O editor e o catálogo em JSX vão dentro do .exe (um zip embutido, gerado por web\construir.ps1). Na primeira
/// abertura de cada versão o zip é extraído numa pasta do usuário, de onde o WebView2 os serve. Assim o programa
/// continua sendo um arquivo só e o atualizador não precisa copiar pastas.
/// </summary>
public static class PacoteWeb
{
    private const string NomeRecurso = "catalogo.zip";

    public static string PastaBase() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "web");

    /// <summary>Extrai (se ainda não foi) e devolve a pasta com o index.html. Lança <see cref="InvalidOperationException"/> se o pacote não estiver no programa.</summary>
    public static string Preparar(Assembly? assembly = null)
    {
        assembly ??= typeof(PacoteWeb).Assembly;
        using var recurso = assembly.GetManifestResourceStream(NomeRecurso)
            ?? throw new InvalidOperationException("O pacote do editor do catálogo não está dentro do programa (rode web\\construir.ps1 e recompile).");
        using var memoria = new MemoryStream();
        recurso.CopyTo(memoria);
        var bytes = memoria.ToArray();

        var selo = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
        var pasta = Path.Combine(PastaBase(), $"catalogo-{selo}");
        if (File.Exists(Path.Combine(pasta, "index.html"))) return pasta;

        // Extrai numa pasta temporária e renomeia: uma extração interrompida nunca deixa meia pasta válida.
        var temporaria = pasta + ".tmp";
        if (Directory.Exists(temporaria)) Directory.Delete(temporaria, true);
        Directory.CreateDirectory(temporaria);
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            zip.ExtractToDirectory(temporaria, overwriteFiles: true);
        if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        Directory.Move(temporaria, pasta);

        LimparVersoesAntigas(pasta);
        return pasta;
    }

    private static void LimparVersoesAntigas(string atual)
    {
        try
        {
            foreach (var outra in Directory.GetDirectories(PastaBase(), "catalogo-*"))
                if (!string.Equals(outra, atual, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(outra, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* fica para a próxima */ }
    }
}
