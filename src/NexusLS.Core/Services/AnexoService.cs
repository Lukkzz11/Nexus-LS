using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Guarda os anexos dos lançamentos da empresa (foto, PDF, documento) numa pasta "anexos" ao lado do banco. O banco só tem o
/// caminho RELATIVO e os dados do arquivo. Nome sanitizado, nome final sempre novo (nunca sobrescreve) e nenhum caminho
/// escapa da pasta de anexos. O backup copia e restaura esta pasta (ver <c>BackupService</c>).
/// </summary>
public class AnexoService
{
    /// <summary>10 MB por arquivo (limite da proposta da Gestão).</summary>
    public const long TamanhoMaximoBytes = 10L * 1024 * 1024;

    public const string NomePasta = "anexos";

    private static readonly Dictionary<string, string> Permitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp", [".bmp"] = "image/bmp",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".csv"] = "text/csv",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    private static readonly HashSet<string> NomesReservados = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly string _raiz;

    public AnexoService(IDbContextFactory<PdvDbContext> fabrica, string pastaAnexos)
    {
        if (string.IsNullOrWhiteSpace(pastaAnexos)) throw new ArgumentException("Pasta de anexos vazia.", nameof(pastaAnexos));
        _fabrica = fabrica;
        _raiz = Path.GetFullPath(pastaAnexos);
    }

    /// <summary>A pasta "anexos" ao lado do arquivo do banco.</summary>
    public static string PastaPadrao(string caminhoBanco) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(caminhoBanco)) ?? ".", NomePasta);

    public string Pasta => _raiz;

    public static IReadOnlyCollection<string> ExtensoesPermitidas => Permitidas.Keys;

    public static bool ExtensaoPermitida(string? nomeOuExtensao)
    {
        var ext = Path.GetExtension(nomeOuExtensao ?? "");
        if (ext.Length == 0 && nomeOuExtensao?.StartsWith('.') == true) ext = nomeOuExtensao;
        return Permitidas.ContainsKey(ext);
    }

    /// <summary>
    /// Nome seguro: só o nome do arquivo (sem pastas, nem "..", nem "/" ou "\"), sem caracteres proibidos no Windows, sem nomes
    /// reservados, no máximo 80 caracteres e extensão em minúsculas. Nunca devolve vazio.
    /// </summary>
    public static string SanitizarNome(string? nome)
    {
        var bruto = nome ?? "";
        var corte = bruto.LastIndexOfAny(new[] { '/', '\\' });
        if (corte >= 0) bruto = bruto[(corte + 1)..];

        var invalidos = new HashSet<char>(Path.GetInvalidFileNameChars()) { ':', '*', '?', '"', '<', '>', '|' };
        var limpo = new string(bruto.Select(c => char.IsControl(c) || invalidos.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');

        var ext = Path.GetExtension(limpo).ToLowerInvariant();
        var baseNome = Path.GetFileNameWithoutExtension(limpo).Trim().TrimEnd('.', ' ');
        if (baseNome.Length > 80) baseNome = baseNome[..80].TrimEnd('.', ' ');
        if (baseNome.Length == 0 || baseNome.All(c => c == '_' || c == '.')) baseNome = "arquivo";
        if (NomesReservados.Contains(baseNome)) baseNome = "_" + baseNome;
        return baseNome + ext;
    }

    /// <summary>
    /// Copia o arquivo para a pasta de anexos e o liga ao lançamento. Confere extensão, tamanho (1 byte a 10 MB) e, para JPG, PNG
    /// e PDF, o início do conteúdo. Calcula o SHA-256 durante a cópia. Nada fica no disco se a gravação no banco falhar.
    /// </summary>
    public async Task<AnexoLancamento> AdicionarAsync(int lancamentoId, string caminhoOrigem, DateTime? agora = null)
    {
        if (string.IsNullOrWhiteSpace(caminhoOrigem) || !File.Exists(caminhoOrigem))
            throw new RegraDeNegocioException("Não encontrei o arquivo escolhido.");

        var nomeOriginal = Path.GetFileName(caminhoOrigem);
        var ext = Path.GetExtension(nomeOriginal).ToLowerInvariant();
        if (!Permitidas.TryGetValue(ext, out var mime))
            throw new RegraDeNegocioException($"Tipo de arquivo não permitido. Use: {string.Join(", ", Permitidas.Keys.OrderBy(x => x))}.");

        var momento = agora ?? DateTime.Now;
        var seguro = SanitizarNome(nomeOriginal);
        var relativo = $"{momento:yyyy}/{momento:MM}/{Guid.NewGuid():N}-{seguro}";
        var destino = CaminhoCompleto(relativo);
        var tmp = destino + ".tmp";

        long tamanho;
        string hash;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            (tamanho, hash) = await CopiarAsync(caminhoOrigem, tmp, ext).ConfigureAwait(false);
            File.Move(tmp, destino);
        }
        catch
        {
            ApagarSeExistir(tmp);
            ApagarSeExistir(destino);
            throw;
        }

        try
        {
            await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
            if (!await db.LancamentosEmpresa.AnyAsync(l => l.Id == lancamentoId).ConfigureAwait(false))
                throw new RegraDeNegocioException("O lançamento não existe mais.");
            var anexo = new AnexoLancamento
            {
                LancamentoEmpresaId = lancamentoId,
                NomeOriginal = nomeOriginal.Length > 200 ? nomeOriginal[^200..] : nomeOriginal,
                CaminhoRelativo = relativo,
                TipoMime = mime,
                TamanhoBytes = tamanho,
                Sha256 = hash,
                CriadoEm = momento
            };
            db.AnexosLancamento.Add(anexo);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return anexo;
        }
        catch
        {
            ApagarSeExistir(destino);
            throw;
        }
    }

    /// <summary>Apaga o registro e o arquivo. Se o arquivo já tinha sumido, só o registro é removido.</summary>
    public async Task RemoverAsync(int anexoId)
    {
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var anexo = await db.AnexosLancamento.FirstOrDefaultAsync(a => a.Id == anexoId).ConfigureAwait(false)
            ?? throw new RegraDeNegocioException("Anexo não encontrado.");
        db.AnexosLancamento.Remove(anexo);
        await db.SaveChangesAsync().ConfigureAwait(false);
        string? completo = null;
        try { completo = CaminhoCompleto(anexo.CaminhoRelativo); } catch (RegraDeNegocioException) { /* caminho inválido no banco: nada a apagar */ }
        if (completo is not null) ApagarSeExistir(completo);
    }

    public async Task<List<AnexoLancamento>> ListarAsync(int lancamentoId)
    {
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        return await db.AnexosLancamento.AsNoTracking().Where(a => a.LancamentoEmpresaId == lancamentoId)
            .OrderBy(a => a.CriadoEm).ThenBy(a => a.Id).ToListAsync().ConfigureAwait(false);
    }

    /// <summary>O arquivo do anexo existe na pasta?</summary>
    public bool Existe(AnexoLancamento anexo)
    {
        try { return File.Exists(CaminhoCompleto(anexo.CaminhoRelativo)); }
        catch (RegraDeNegocioException) { return false; }
    }

    /// <summary>
    /// Caminho completo de um caminho relativo guardado, SEMPRE dentro da pasta de anexos. Caminho absoluto, com ".." ou que
    /// escape da pasta é recusado.
    /// </summary>
    public string CaminhoCompleto(string caminhoRelativo)
    {
        if (string.IsNullOrWhiteSpace(caminhoRelativo) || Path.IsPathRooted(caminhoRelativo) ||
            caminhoRelativo.Contains(':') || caminhoRelativo.Split('/', '\\').Any(p => p == ".."))
            throw new RegraDeNegocioException("Caminho de anexo inválido.");
        var completo = Path.GetFullPath(Path.Combine(_raiz, caminhoRelativo.Replace('/', Path.DirectorySeparatorChar)));
        var prefixo = _raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!completo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            throw new RegraDeNegocioException("Caminho de anexo inválido.");
        return completo;
    }

    private static async Task<(long Tamanho, string Hash)> CopiarAsync(string origem, string destino, string ext)
    {
        await using var entrada = new FileStream(origem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
        if (entrada.Length == 0) throw new RegraDeNegocioException("O arquivo está vazio.");
        if (entrada.Length > TamanhoMaximoBytes)
            throw new RegraDeNegocioException($"O arquivo passa do limite de {TamanhoMaximoBytes / (1024 * 1024)} MB.");

        var cabecalho = new byte[8];
        var lidos = 0;
        while (lidos < cabecalho.Length)
        {
            var n = await entrada.ReadAsync(cabecalho.AsMemory(lidos)).ConfigureAwait(false);
            if (n == 0) break;
            lidos += n;
        }
        if (!CabecalhoConfere(ext, cabecalho, lidos))
            throw new RegraDeNegocioException("O conteúdo do arquivo não bate com a extensão (" + ext + ").");
        entrada.Position = 0;

        using var sha = SHA256.Create();
        long total = 0;
        await using var saida = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        int lido;
        while ((lido = await entrada.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            total += lido;
            if (total > TamanhoMaximoBytes) throw new RegraDeNegocioException("O arquivo passa do limite de 10 MB.");
            sha.TransformBlock(buffer, 0, lido, null, 0);
            await saida.WriteAsync(buffer.AsMemory(0, lido)).ConfigureAwait(false);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return (total, Convert.ToHexString(sha.Hash!).ToLowerInvariant());
    }

    private static bool CabecalhoConfere(string ext, byte[] b, int n) => ext switch
    {
        ".jpg" or ".jpeg" => n >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        ".png" => n >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
        ".pdf" => n >= 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F',
        _ => true
    };

    private static void ApagarSeExistir(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); }
        catch (IOException) { /* arquivo preso: não derruba a operação */ }
    }
}
