using System.Text.RegularExpressions;

namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>
/// Os dados que o console do Firebase mostra em "Configurações do projeto → Seus apps" (o trecho <c>firebaseConfig</c>
/// em JavaScript ou um JSON). Cada cliente cola o do projeto dele; nada fica fixo no programa.
/// </summary>
public record ConfiguracaoColada(string? ApiKey, string? ProjectId, string? Bucket)
{
    public bool TemAlgo => !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(ProjectId);

    /// <summary>Lê o texto colado (aceita aspas simples ou duplas e chaves com ou sem aspas). Devolve vazio se não achar nada.</summary>
    public static ConfiguracaoColada Ler(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return new(null, null, null);
        return new(Valor(texto, "apiKey"), Valor(texto, "projectId"), Valor(texto, "storageBucket"));
    }

    private static string? Valor(string texto, string chave)
    {
        var m = Regex.Match(texto, $"[\"']?{chave}[\"']?\\s*:\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
