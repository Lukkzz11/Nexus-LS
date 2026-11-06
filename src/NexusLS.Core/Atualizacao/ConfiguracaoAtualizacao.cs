using System.Text.Json;

namespace NexusLS.Core.Atualizacao;

/// <summary>
/// De onde o programa busca as atualizações. O repositório precisa ser PÚBLICO e conter só os instaladores (nunca o
/// código-fonte), porque o programa instalado no cliente não pode carregar senha nem token. Para trocar sem recompilar,
/// crie <c>%LOCALAPPDATA%\NexusLS\atualizacao.json</c> com <c>{ "repositorio": "dono/nome" }</c>.
/// </summary>
public static class ConfiguracaoAtualizacao
{
    public const string RepositorioPadrao = "Lukkzz11/Nexus-LS---Update";

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "atualizacao.json");

    public static string Repositorio(string? caminho = null)
    {
        try
        {
            caminho ??= CaminhoPadrao();
            if (!File.Exists(caminho)) return RepositorioPadrao;
            using var json = JsonDocument.Parse(File.ReadAllText(caminho));
            var texto = json.RootElement.TryGetProperty("repositorio", out var r) ? r.GetString() : null;
            return RepositorioValido(texto) ? texto!.Trim() : RepositorioPadrao;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return RepositorioPadrao;
        }
    }

    /// <summary>Precisa ser "dono/nome", só com letras, números, ponto, traço e sublinhado.</summary>
    public static bool RepositorioValido(string? texto) =>
        !string.IsNullOrWhiteSpace(texto) &&
        System.Text.RegularExpressions.Regex.IsMatch(texto.Trim(), @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
}
