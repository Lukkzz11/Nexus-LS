using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace NexusLS.Core.Atualizacao;

/// <summary>Alinhavo publicado na nuvem: a versão do app (não é a do Nexus) e o zip com o pacote.</summary>
public record InfoAlinhavo(VersaoPrograma Versao, InfoVersao Zip);

/// <summary>
/// Alinhavo (Moldes e costura 3D) pela nuvem. Usa a MESMA release do Nexus no GitHub e o mesmo mecanismo do
/// Nexus LS e do BarTender (FonteGitHub: HTTPS só do GitHub, SHA-256 do arquivo .sha256). A release traz:
/// <c>Alinhavo.zip</c>, <c>Alinhavo.zip.sha256</c> e <c>alinhavo-versao.txt</c> (só a versão do app, ex.: 0.1.0,
/// porque a tag da release é a versão do Nexus e não a do Alinhavo).
/// </summary>
public class FonteAlinhavo
{
    public const string NomeDoZip = "Alinhavo.zip";
    public const string NomeDoHash = "Alinhavo.zip.sha256";
    public const string NomeDaVersao = "alinhavo-versao.txt";

    private readonly HttpClient _http;
    private readonly string _repositorio;
    private readonly Func<Uri, bool>? _urlPermitida;

    public FonteAlinhavo(HttpClient http, string repositorio, Func<Uri, bool>? urlPermitida = null)
    {
        _http = http;
        _repositorio = repositorio;
        _urlPermitida = urlPermitida;
    }

    /// <summary>Fonte de download do zip (a mesma lógica de baixar do Nexus LS).</summary>
    public IFonteAtualizacao FonteDoZip => new FonteGitHub(_http, _repositorio, NomeDoZip, NomeDoHash, _urlPermitida);

    /// <summary>Última versão do Alinhavo publicada, ou nulo se a release mais recente não traz o Alinhavo.</summary>
    public async Task<InfoAlinhavo?> UltimaAsync(CancellationToken ct = default)
    {
        var zip = await new FonteGitHub(_http, _repositorio, NomeDoZip, NomeDoHash, _urlPermitida).UltimaAsync(ct).ConfigureAwait(false);
        if (zip is null) return null;

        var fonteVersao = new FonteGitHub(_http, _repositorio, NomeDaVersao, NomeDaVersao + ".sha256", _urlPermitida);
        var infoVersao = await fonteVersao.UltimaAsync(ct).ConfigureAwait(false);
        if (infoVersao is null) return null;

        using var ms = new MemoryStream();
        await fonteVersao.BaixarAsync(infoVersao, ms, null, ct).ConfigureAwait(false);
        var versao = LerVersao(Encoding.UTF8.GetString(ms.ToArray()));
        return versao is VersaoPrograma v ? new InfoAlinhavo(v, zip) : null;
    }

    /// <summary>Lê a versão de um texto como "0.1.0", "v0.1.0" ou "0.1.0\n". Inválido vira nulo.</summary>
    public static VersaoPrograma? LerVersao(string? texto)
    {
        var linha = (texto ?? "").Replace("﻿", "").Trim().Split('\n', '\r').FirstOrDefault()?.Trim();
        return VersaoPrograma.TryParse(linha, out var v) ? v : null;
    }
}

/// <summary>Baixar, conferir e instalar o pacote .zip do Alinhavo (e dos apps do catálogo). Cada falha vira uma mensagem simples em português.</summary>
public static class InstalacaoAlinhavo
{
    public const string ArquivoVersao = "versao.txt";
    public const string ExeAlinhavo = "alinhavo.exe";

    /// <summary>Versão instalada na pasta (versao.txt gravado pelo pacote), ou nulo.</summary>
    public static VersaoPrograma? VersaoInstalada(string pastaAlinhavo)
    {
        try
        {
            var arquivo = Path.Combine(pastaAlinhavo, ArquivoVersao);
            return File.Exists(arquivo) ? FonteAlinhavo.LerVersao(File.ReadAllText(arquivo)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>True se a nuvem tem versão MAIOR que a instalada. Sem versão instalada conhecida, qualquer nuvem vale.</summary>
    public static bool HaVersaoNova(VersaoPrograma? instalada, VersaoPrograma naNuvem) =>
        instalada is not VersaoPrograma atual || naNuvem > atual;

    /// <summary>
    /// Baixa o zip para a pasta temporária e confere tamanho e SHA-256. Devolve o caminho do zip conferido.
    /// Se algo falha, o arquivo parcial é apagado e nada muda no computador.
    /// </summary>
    public static async Task<string> BaixarAsync(IFonteAtualizacao fonte, InfoVersao zip, string pastaTemporaria,
        IProgress<double>? progresso = null, CancellationToken ct = default,
        string nomeDoApp = "Alinhavo", string nomeDoZip = FonteAlinhavo.NomeDoZip)
    {
        if (string.IsNullOrWhiteSpace(zip.Sha256))
            throw new RegraDeAtualizacaoException($"Esta versão do {nomeDoApp} não traz a assinatura de conferência (SHA-256) e por segurança não será instalada.");

        Directory.CreateDirectory(pastaTemporaria);
        VerificarEspaco(pastaTemporaria, zip.Tamanho * 3, "baixar", nomeDoApp);
        var destino = Path.Combine(pastaTemporaria, nomeDoZip);
        try
        {
            await using (var arquivo = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var relatorio = progresso is null || zip.Tamanho <= 0
                    ? null
                    : new Progress<long>(b => progresso.Report(Math.Min(1.0, (double)b / zip.Tamanho)));
                await fonte.BaixarAsync(zip, arquivo, relatorio, ct).ConfigureAwait(false);
            }

            var tamanho = new FileInfo(destino).Length;
            if (zip.Tamanho > 0 && tamanho != zip.Tamanho)
                throw new RegraDeAtualizacaoException($"O download do {nomeDoApp} veio incompleto. Tente de novo.");

            var hash = await AtualizadorService.Sha256DoArquivoAsync(destino, ct).ConfigureAwait(false);
            if (!string.Equals(hash, zip.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new RegraDeAtualizacaoException($"O arquivo baixado do {nomeDoApp} não confere com a assinatura (SHA-256) e foi descartado. Nada foi instalado.");

            progresso?.Report(1.0);
            return destino;
        }
        catch
        {
            try { if (File.Exists(destino)) File.Delete(destino); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Extrai o zip numa pasta ao lado, confere que tem alinhavo.exe e só então troca a pasta instalada
    /// (a anterior vai para ".antigo" e é apagada). O Alinhavo precisa estar fechado.
    /// </summary>
    public static void Instalar(string zip, string pastaDestino, string nomeDoApp = "Alinhavo", string exe = ExeAlinhavo)
    {
        var pai = Path.GetDirectoryName(Path.GetFullPath(pastaDestino).TrimEnd('\\', '/'))
            ?? throw new RegraDeAtualizacaoException("Pasta de instalação inválida.");
        Directory.CreateDirectory(pai);
        VerificarEspaco(pai, new FileInfo(zip).Length * 4, "instalar", nomeDoApp);

        var novo = pastaDestino.TrimEnd('\\', '/') + ".novo";
        var antigo = pastaDestino.TrimEnd('\\', '/') + AtualizadorService.SufixoDoAntigo;
        try
        {
            ApagarPasta(novo);
            ZipFile.ExtractToDirectory(zip, novo, overwriteFiles: true);
            if (!File.Exists(Path.Combine(novo, exe)))
                throw new RegraDeAtualizacaoException($"O pacote do {nomeDoApp} está incompleto (falta o {exe}). Nada foi instalado.");

            ApagarPasta(antigo);
            if (Directory.Exists(pastaDestino)) Directory.Move(pastaDestino, antigo);
            try { Directory.Move(novo, pastaDestino); }
            catch
            {
                if (!Directory.Exists(pastaDestino) && Directory.Exists(antigo)) Directory.Move(antigo, pastaDestino); // desfaz
                throw;
            }
            ApagarPasta(antigo);
        }
        catch (InvalidDataException)
        {
            ApagarPasta(novo);
            throw new RegraDeAtualizacaoException($"O arquivo do {nomeDoApp} está corrompido e não pôde ser aberto. Tente de novo.");
        }
        catch
        {
            ApagarPasta(novo);
            throw;
        }
    }

    /// <summary>Texto simples para o usuário, sem jargão, para qualquer falha do download/instalação.</summary>
    public static string Traduzir(Exception ex, string nomeDoApp = "Alinhavo") => ex switch
    {
        RegraDeAtualizacaoException r => r.Message,
        OperationCanceledException => "A operação foi cancelada.",
        HttpRequestException or TaskCanceledException => "Não foi possível falar com a nuvem. Confira a internet e tente de novo.",
        InvalidDataException => "O arquivo recebido da nuvem foi recusado por segurança.",
        UnauthorizedAccessException => $"O Windows não deixou gravar na pasta do {nomeDoApp}. Feche o {nomeDoApp} e tente de novo.",
        IOException io when SemEspaco(io) => "Não há espaço livre suficiente no disco. Libere espaço e tente de novo.",
        IOException => $"Não foi possível gravar os arquivos do {nomeDoApp} (algum arquivo pode estar aberto). Feche o {nomeDoApp} e tente de novo.",
        _ => "Não foi possível concluir: " + ex.Message
    };

    private static bool SemEspaco(IOException io) => (io.HResult & 0xFFFF) is 112 or 39; // ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL

    private static void VerificarEspaco(string pasta, long necessario, string acao, string nomeDoApp)
    {
        if (necessario <= 0) return;
        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(pasta));
            if (!string.IsNullOrEmpty(raiz) && new DriveInfo(raiz).AvailableFreeSpace < necessario)
                throw new RegraDeAtualizacaoException($"Não há espaço livre suficiente no disco para {acao} o {nomeDoApp} (precisa de cerca de {necessario / 1048576 + 1} MB).");
        }
        catch (ArgumentException) { }
        catch (IOException) { }
    }

    private static void ApagarPasta(string pasta)
    {
        try { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
