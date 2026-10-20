using System.Net;
using System.Text.Json;

namespace NexusLS.Core.Atualizacao;

/// <summary>Uma versão publicada: o que mudou, onde baixar e como conferir que o arquivo veio inteiro e sem alteração.</summary>
public record InfoVersao(VersaoPrograma Versao, string Notas, Uri UrlExe, long Tamanho, string? Sha256, DateTime? PublicadaEm);

/// <summary>De onde vêm as versões. Fica atrás de uma interface para testar sem internet.</summary>
public interface IFonteAtualizacao
{
    /// <summary>A versão mais nova publicada, ou nulo se ainda não há nenhuma.</summary>
    Task<InfoVersao?> UltimaAsync(CancellationToken ct = default);

    /// <summary>Baixa o arquivo da versão para <paramref name="destino"/>, avisando quantos bytes já vieram.</summary>
    Task BaixarAsync(InfoVersao versao, Stream destino, IProgress<long>? progresso, CancellationToken ct = default);
}

/// <summary>
/// Lê as versões de um repositório do GitHub (Releases). O repositório precisa ser PÚBLICO e conter só os instaladores:
/// o programa instalado no cliente não pode ter senha nem token do seu GitHub. Cada release tem o arquivo
/// <c>NexusLS.exe</c> e (se o GitHub não informar o hash sozinho) <c>NexusLS.exe.sha256</c> com o SHA-256 dele.
/// </summary>
public class FonteGitHub : IFonteAtualizacao
{
    public const string NomeDoExecutavel = "NexusLS.exe";
    public const string NomeDoHash = "NexusLS.exe.sha256";

    private static readonly string[] SufixosPermitidos = { "github.com", "githubusercontent.com" };

    private readonly HttpClient _http;
    private readonly string _repositorio;
    private readonly Func<Uri, bool> _urlPermitida;

    /// <param name="repositorio">No formato dono/nome, por exemplo "Lukkzz11/Nexus-LS-Atualizacoes".</param>
    /// <param name="urlPermitida">Só para testes: troca a regra de quais endereços podem ser baixados.</param>
    public FonteGitHub(HttpClient http, string repositorio, Func<Uri, bool>? urlPermitida = null)
    {
        _http = http;
        _repositorio = repositorio.Trim().Trim('/');
        _urlPermitida = urlPermitida ?? EnderecoDoGitHub;
    }

    /// <summary>Só baixamos por HTTPS e só de endereços do próprio GitHub: um release adulterado não pode nos mandar para outro site.</summary>
    public static bool EnderecoDoGitHub(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && SufixosPermitidos.Any(s => url.Host == s || url.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));

    public async Task<InfoVersao?> UltimaAsync(CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repositorio}/releases/latest");
        pedido.Headers.UserAgent.ParseAdd("NexusLS-Atualizador");
        pedido.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var resposta = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null; // repositório sem nenhuma versão publicada
        resposta.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        var raiz = json.RootElement;
        if (raiz.TryGetProperty("draft", out var rascunho) && rascunho.GetBoolean()) return null;
        if (raiz.TryGetProperty("prerelease", out var previa) && previa.GetBoolean()) return null;
        if (!raiz.TryGetProperty("tag_name", out var tag) || !VersaoPrograma.TryParse(tag.GetString(), out var versao)) return null;

        JsonElement? exe = null, hash = null;
        if (raiz.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var nome = a.GetProperty("name").GetString();
                if (string.Equals(nome, NomeDoExecutavel, StringComparison.OrdinalIgnoreCase)) exe = a;
                else if (string.Equals(nome, NomeDoHash, StringComparison.OrdinalIgnoreCase)) hash = a;
            }
        if (exe is not JsonElement arquivo) return null;

        var url = new Uri(arquivo.GetProperty("browser_download_url").GetString()!);
        if (!_urlPermitida(url)) throw new InvalidDataException("O endereço do arquivo da atualização não é do GitHub e foi recusado.");

        var sha = Sha256Do(arquivo);
        if (sha is null && hash is JsonElement arquivoHash)
        {
            var urlHash = new Uri(arquivoHash.GetProperty("browser_download_url").GetString()!);
            if (!_urlPermitida(urlHash)) throw new InvalidDataException("O endereço do hash da atualização foi recusado.");
            var texto = await _http.GetStringAsync(urlHash, ct).ConfigureAwait(false);
            sha = texto.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        }

        var tamanho = arquivo.TryGetProperty("size", out var t) ? t.GetInt64() : 0;
        DateTime? publicada = raiz.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && DateTime.TryParse(p.GetString(), out var d) ? d : null;
        var notas = raiz.TryGetProperty("body", out var corpo) && corpo.ValueKind == JsonValueKind.String ? corpo.GetString() ?? "" : "";
        return new InfoVersao(versao, notas.Trim(), url, tamanho, sha, publicada);
    }

    public async Task BaixarAsync(InfoVersao versao, Stream destino, IProgress<long>? progresso, CancellationToken ct = default)
    {
        if (!_urlPermitida(versao.UrlExe)) throw new InvalidDataException("O endereço do arquivo da atualização foi recusado.");
        using var pedido = new HttpRequestMessage(HttpMethod.Get, versao.UrlExe);
        pedido.Headers.UserAgent.ParseAdd("NexusLS-Atualizador");
        using var resposta = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();

        await using var origem = await resposta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var bloco = new byte[81920];
        long total = 0;
        int lidos;
        while ((lidos = await origem.ReadAsync(bloco, ct).ConfigureAwait(false)) > 0)
        {
            await destino.WriteAsync(bloco.AsMemory(0, lidos), ct).ConfigureAwait(false);
            total += lidos;
            progresso?.Report(total);
        }
    }

    /// <summary>O GitHub informa o hash do arquivo no campo "digest" ("sha256:...").</summary>
    private static string? Sha256Do(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var d) || d.ValueKind != JsonValueKind.String) return null;
        var texto = d.GetString() ?? "";
        return texto.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? texto[7..].ToLowerInvariant() : null;
    }
}
