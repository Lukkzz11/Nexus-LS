using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusLS.Core.Atualizacao;

/// <summary>
/// Um app do catálogo do Nexus Hub. Ou é um programa (<c>pacote</c> .zip na release + <c>exe</c> na raiz do zip +
/// <c>versao</c>) ou é um sistema web (<c>url</c> https).
/// </summary>
public record AppDoCatalogo(
    string Id, string Nome, string Subtitulo, string Descricao, string Cor,
    VersaoPrograma Versao, string? Pacote, string? Exe, Uri? Url)
{
    public bool EhWeb => Url is not null;
}

/// <summary>
/// Catálogo de apps do Hub: o arquivo <c>catalogo-apps.json</c> da release mais recente. App novo entra no Hub só
/// publicando esse arquivo e o zip do app, sem mexer no código do Hub.
/// </summary>
public static class CatalogoDeApps
{
    public const string NomeDoArquivo = "catalogo-apps.json";
    private const string CorPadrao = "#0067B8";

    /// <summary>Apps que já têm card próprio no Hub (e pasta própria): o catálogo não pode repetir.</summary>
    private static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
        { "nexus", "nexus-ls", "nexusls", "bartender", "alinhavo", "atualizador", "loja-online" };

    /// <summary>Lê o JSON. Entrada incompleta ou perigosa é ignorada; as outras continuam valendo.</summary>
    public static IReadOnlyList<AppDoCatalogo> Ler(string? json)
    {
        var apps = new List<AppDoCatalogo>();
        if (string.IsNullOrWhiteSpace(json)) return apps;
        try
        {
            using var doc = JsonDocument.Parse(json.TrimStart('﻿'));
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("apps", out var lista) || lista.ValueKind != JsonValueKind.Array) return apps;

            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in lista.EnumerateArray())
                if (LerApp(item) is { } app && vistos.Add(app.Id)) apps.Add(app);
        }
        catch (JsonException) { }
        return apps;
    }

    private static AppDoCatalogo? LerApp(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        string Texto(string campo) =>
            e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

        var id = Texto("id");
        var nome = Texto("nome");
        // O id vira nome de pasta: só letras minúsculas, números e traço.
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{1,39}$") || Reservados.Contains(id) || nome.Length == 0) return null;

        var cor = Regex.IsMatch(Texto("cor"), "^#[0-9A-Fa-f]{6}$") ? Texto("cor") : CorPadrao;

        var endereco = Texto("url");
        if (endereco.Length > 0)
            return Uri.TryCreate(endereco, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
                ? new AppDoCatalogo(id, nome, Texto("subtitulo"), Texto("descricao"), cor, default, null, null, url)
                : null;

        var pacote = Texto("pacote");
        var exe = Texto("exe");
        if (!NomeSimples(pacote, ".zip") || !NomeSimples(exe, ".exe") || !VersaoPrograma.TryParse(Texto("versao"), out var versao)) return null;
        return new AppDoCatalogo(id, nome, Texto("subtitulo"), Texto("descricao"), cor, versao, pacote, exe, null);
    }

    /// <summary>Só o nome do arquivo, sem pasta nem "..", com a extensão esperada.</summary>
    private static bool NomeSimples(string nome, string extensao) =>
        Regex.IsMatch(nome, @"^[A-Za-z0-9][A-Za-z0-9 _.-]{0,79}$") && !nome.Contains("..") &&
        nome.EndsWith(extensao, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Baixa o catálogo da release mais recente conferindo o SHA-256. Nulo se a release não traz catálogo ou se o
    /// arquivo não confere (quem chama usa a cópia guardada).
    /// </summary>
    public static async Task<string?> BaixarAsync(HttpClient http, string repositorio, CancellationToken ct = default,
        Func<Uri, bool>? urlPermitida = null)
    {
        var fonte = new FonteGitHub(http, repositorio, NomeDoArquivo, NomeDoArquivo + ".sha256", urlPermitida);
        var info = await fonte.UltimaAsync(ct).ConfigureAwait(false);
        if (info is null || string.IsNullOrWhiteSpace(info.Sha256) || info.Tamanho > 1_000_000) return null;

        using var ms = new MemoryStream();
        await fonte.BaixarAsync(info, ms, null, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return string.Equals(hash, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8.GetString(bytes) : null;
    }
}
