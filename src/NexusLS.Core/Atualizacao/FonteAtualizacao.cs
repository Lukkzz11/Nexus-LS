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
    private readonly string _nomeDoExecutavel;
    private readonly string _nomeDoHash;
    private readonly Func<Uri, bool> _urlPermitida;

    /// <param name="repositorio">No formato dono/nome, por exemplo "Lukkzz11/Nexus-LS---Update".</param>
    /// <param name="nomeDoExecutavel">Nome do executável (ex: "NexusLS.exe" ou "NexusBarTender.exe").</param>
    /// <param name="nomeDoHash">Nome do arquivo com o hash (ex: "NexusLS.exe.sha256").</param>
    /// <param name="urlPermitida">Só para testes: troca a regra de quais endereços podem ser baixados.</param>
    public FonteGitHub(HttpClient http, string repositorio, string nomeDoExecutavel = NomeDoExecutavel, string nomeDoHash = NomeDoHash, Func<Uri, bool>? urlPermitida = null)
    {
        _http = http;
        _repositorio = repositorio.Trim().Trim('/');
        _nomeDoExecutavel = string.IsNullOrWhiteSpace(nomeDoExecutavel) ? NomeDoExecutavel : nomeDoExecutavel;
        _nomeDoHash = string.IsNullOrWhiteSpace(nomeDoHash) ? NomeDoHash : nomeDoHash;
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
                if (string.Equals(nome, _nomeDoExecutavel, StringComparison.OrdinalIgnoreCase)) exe = a;
                else if (string.Equals(nome, _nomeDoHash, StringComparison.OrdinalIgnoreCase)) hash = a;
            }
        if (exe is not JsonElement arquivo) return null;

        // Se o asset fornecer "url" da API do GitHub, usamos ela como endereço preferencial.
        // O GitHub API com o cabeçalho 'Accept: application/octet-stream' redireciona via 302 direto
        // para a CDN Azure Blob (release-assets.githubusercontent.com), contornando o erro HTTP 504
        // que os gateways locais do Brasil enfrentam no endpoint web público /releases/download/.
        var urlApi = arquivo.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        var urlDownload = arquivo.TryGetProperty("browser_download_url", out var b) ? b.GetString() : null;
        var urlEscolhida = !string.IsNullOrWhiteSpace(urlApi) ? urlApi : urlDownload;
        if (string.IsNullOrWhiteSpace(urlEscolhida)) return null;

        var url = new Uri(urlEscolhida);
        if (!_urlPermitida(url)) throw new InvalidDataException("O endereço do arquivo da atualização não é do GitHub e foi recusado.");

        var sha = Sha256Do(arquivo);
        if (sha is null && hash is JsonElement arquivoHash)
        {
            var urlHashApi = arquivoHash.TryGetProperty("url", out var uh) && uh.ValueKind == JsonValueKind.String ? uh.GetString() : null;
            var urlHashDl = arquivoHash.TryGetProperty("browser_download_url", out var bh) ? bh.GetString() : null;
            var urlHashEscolhida = !string.IsNullOrWhiteSpace(urlHashApi) ? urlHashApi : urlHashDl;

            if (!string.IsNullOrWhiteSpace(urlHashEscolhida))
            {
                var urlHash = new Uri(urlHashEscolhida);
                if (!_urlPermitida(urlHash)) throw new InvalidDataException("O endereço do hash da atualização foi recusado.");

                using var pedidoHash = new HttpRequestMessage(HttpMethod.Get, urlHash);
                pedidoHash.Headers.UserAgent.ParseAdd("NexusLS-Atualizador/1.0");
                pedidoHash.Headers.Accept.ParseAdd("application/octet-stream");

                using var respHash = await _http.SendAsync(pedidoHash, ct).ConfigureAwait(false);
                if (respHash.IsSuccessStatusCode)
                {
                    var texto = await respHash.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    sha = texto.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
                }
            }
        }

        var tamanho = arquivo.TryGetProperty("size", out var t) ? t.GetInt64() : 0;
        DateTime? publicada = raiz.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && DateTime.TryParse(p.GetString(), out var d) ? d : null;
        var notas = raiz.TryGetProperty("body", out var corpo) && corpo.ValueKind == JsonValueKind.String ? corpo.GetString() ?? "" : "";
        return new InfoVersao(versao, notas.Trim(), url, tamanho, sha, publicada);
    }

    public async Task BaixarAsync(InfoVersao versao, Stream destino, IProgress<long>? progresso, CancellationToken ct = default)
    {
        if (!_urlPermitida(versao.UrlExe)) throw new InvalidDataException("O endereço do arquivo da atualização foi recusado.");

        // Se por algum motivo o UrlExe for o endpoint web direto do GitHub (/releases/download/) que costuma
        // dar 504 Time-out em conexões do Brasil, montamos a lista de URLs candidatas (API primeiro, web depois, ou vice-versa).
        var urlsParaTentar = new List<Uri> { versao.UrlExe };

        if (versao.UrlExe.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            versao.UrlExe.AbsolutePath.Contains("/releases/download/"))
        {
            try
            {
                var infoApi = await UltimaAsync(ct).ConfigureAwait(false);
                if (infoApi != null && infoApi.UrlExe != versao.UrlExe && !urlsParaTentar.Contains(infoApi.UrlExe))
                {
                    urlsParaTentar.Insert(0, infoApi.UrlExe); // Coloca o endpoint da API na frente
                }
            }
            catch
            {
                // Ignora se não conseguir consultar e segue com a lista existente
            }
        }

        Exception? ultimoErro = null;
        foreach (var urlAtual in urlsParaTentar)
        {
            for (var tentativa = 1; tentativa <= 3; tentativa++)
            {
                try
                {
                    using var pedido = new HttpRequestMessage(HttpMethod.Get, urlAtual);
                    pedido.Headers.UserAgent.ParseAdd("NexusLS-Atualizador/1.0");
                    pedido.Headers.Accept.ParseAdd("application/octet-stream");

                    using var resposta = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    // Se recebeu erro transitório do gateway (ex: 502, 503, 504), tenta novamente
                    if ((int)resposta.StatusCode is 502 or 503 or 504)
                    {
                        ultimoErro = new HttpRequestException($"Servidor temporariamente indisponível ({resposta.StatusCode}) ao acessar {urlAtual.Host}.");
                        if (tentativa < 3)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1.5 * tentativa), ct).ConfigureAwait(false);
                            continue;
                        }
                        break; // Tenta próxima URL candidata se esta falhou
                    }

                    resposta.EnsureSuccessStatusCode();

                    destino.SetLength(0); // Garante stream limpo caso seja uma nova tentativa
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

                    return; // Download concluído com sucesso
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && tentativa < 3 && !ct.IsCancellationRequested)
                {
                    ultimoErro = ex;
                    await Task.Delay(TimeSpan.FromSeconds(1.5 * tentativa), ct).ConfigureAwait(false);
                }
            }
        }

        if (ultimoErro != null) throw ultimoErro;
    }

    /// <summary>O GitHub informa o hash do arquivo no campo "digest" ("sha256:...").</summary>
    private static string? Sha256Do(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var d) || d.ValueKind != JsonValueKind.String) return null;
        var texto = d.GetString() ?? "";
        return texto.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? texto[7..].ToLowerInvariant() : null;
    }
}
