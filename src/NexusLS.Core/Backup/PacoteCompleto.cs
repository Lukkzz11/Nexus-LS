using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexusLS.Core.Services;

namespace NexusLS.Core.Backup;

/// <summary>
/// Backup COMPLETO para formatar o computador: um único arquivo (.nexusbackup), protegido por senha, com o banco,
/// os anexos, a gaveta de documentos, as configurações, a licença, o certificado e as chaves de integração
/// (tudo o que mora em %LOCALAPPDATA%\NexusLS, menos o que se recria sozinho: logs, atualizações, cache do navegador).
/// Como guarda credenciais, o arquivo é criptografado (AES-256-GCM, chave derivada da senha com PBKDF2).
/// </summary>
public class PacoteCompleto
{
    public const string Extensao = ".nexusbackup";
    public const int MinimoSenha = 6;

    private static readonly byte[] Magia = "NXBK1"u8.ToArray();
    private const int Iteracoes = 210_000;
    private const int TamanhoBloco = 1024 * 1024;
    private const string EntradaBanco = "banco/nexus.db";
    private const string EntradaManifesto = "manifesto.json";
    private const string PrefixoDados = "dados/";

    // Pastas e arquivos que se recriam sozinhos ou são lixo: não entram no pacote.
    private static readonly string[] PastasIgnoradas =
        { "logs", "atualizacao", "webview2", "web", "cloudflared", "LicenseData", "LicenseProfile" };

    private readonly string _caminhoBanco;
    private readonly string _raiz;
    private readonly string _versaoApp;

    /// <param name="caminhoBanco">Arquivo nexus.db. A raiz dos dados é a pasta dele.</param>
    public PacoteCompleto(string caminhoBanco, string versaoApp = "")
    {
        _caminhoBanco = Path.GetFullPath(caminhoBanco);
        _raiz = Path.GetDirectoryName(_caminhoBanco) ?? ".";
        _versaoApp = versaoApp;
    }

    public static string NomeSugerido(DateTime momento) => $"nexus-backup-completo_{momento:yyyy-MM-dd_HHmm}{Extensao}";

    /// <summary>Resumo do que há num pacote, lido sem restaurar nada.</summary>
    public record Resumo(DateTime CriadoEm, string VersaoApp, int Arquivos, long Bytes);

    // ---------------------------------------------------------------- criar

    public Task<string> CriarAsync(string destino, string senha, DateTime? agora = null) =>
        Task.Run(() => Criar(destino, senha, agora));

    /// <summary>Cria o pacote. Escreve em um .tmp, confere se abre de volta e só então dá o nome final.</summary>
    public string Criar(string destino, string senha, DateTime? agora = null)
    {
        ValidarSenha(senha);
        if (string.IsNullOrWhiteSpace(destino)) throw new RegraDeNegocioException("Escolha onde salvar o backup.");
        if (!File.Exists(_caminhoBanco)) throw new RegraDeNegocioException("Ainda não existe banco de dados para copiar.");

        var momento = agora ?? DateTime.Now;
        var dirDestino = Path.GetDirectoryName(Path.GetFullPath(destino)) ?? ".";
        Directory.CreateDirectory(dirDestino);
        var zipTmp = Path.Combine(Path.GetTempPath(), "nexus-pacote-" + Guid.NewGuid().ToString("N") + ".zip");
        var bancoTmp = Path.Combine(Path.GetTempPath(), "nexus-banco-" + Guid.NewGuid().ToString("N") + ".db");
        var finalTmp = destino + ".tmp";
        try
        {
            CopiarBanco(_caminhoBanco, bancoTmp);
            var (ok, detalhe) = BackupService.Conferir(bancoTmp);
            if (!ok) throw new RegraDeNegocioException("A cópia do banco não passou na conferência: " + detalhe);

            var arquivos = ArquivosDeDados(Path.GetFullPath(destino)).ToList();
            using (var fs = new FileStream(zipTmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(bancoTmp, EntradaBanco, CompressionLevel.Fastest);
                foreach (var f in arquivos)
                {
                    var rel = Path.GetRelativePath(_raiz, f).Replace(Path.DirectorySeparatorChar, '/');
                    try { zip.CreateEntryFromFile(f, PrefixoDados + rel, CompressionLevel.Fastest); }
                    catch (IOException) { /* arquivo preso por outro programa: o resto do backup segue */ }
                }
                var manifesto = new Manifesto { CriadoEm = momento, VersaoApp = _versaoApp, Arquivos = arquivos.Count };
                var e = zip.CreateEntry(EntradaManifesto);
                using var w = e.Open();
                JsonSerializer.Serialize(w, manifesto);
            }

            using (var entrada = File.OpenRead(zipTmp))
            using (var saida = new FileStream(finalTmp, FileMode.Create, FileAccess.Write, FileShare.None))
                Criptografar(entrada, saida, senha);

            // Confere de verdade: abre de volta com a senha e lê o manifesto.
            LerResumo(finalTmp, senha);
            File.Move(finalTmp, destino, overwrite: true);
            return destino;
        }
        catch (Exception ex)
        {
            Apagar(finalTmp);
            if (ex is RegraDeNegocioException) throw;
            throw new RegraDeNegocioException("Não foi possível criar o backup: " + ex.Message);
        }
        finally
        {
            Apagar(zipTmp);
            Apagar(bancoTmp);
        }
    }

    // ---------------------------------------------------------------- ler / restaurar

    /// <summary>Abre o pacote (confere a senha) e devolve o resumo. Não altera nada.</summary>
    public static Resumo LerResumo(string arquivo, string senha)
    {
        var zipTmp = Path.Combine(Path.GetTempPath(), "nexus-leitura-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            Descriptografar(arquivo, zipTmp, senha);
            using var zip = ZipFile.OpenRead(zipTmp);
            var m = LerManifesto(zip);
            var bytes = zip.Entries.Sum(e => e.Length);
            return new Resumo(m.CriadoEm, m.VersaoApp, m.Arquivos, bytes);
        }
        finally { Apagar(zipTmp); }
    }

    public Task<string?> RestaurarAsync(string arquivo, string senha) => Task.Run(() => Restaurar(arquivo, senha));

    /// <summary>
    /// Restaura o pacote por cima da instalação atual. Antes, o banco e as configurações de agora são guardados em
    /// "antes-da-restauracao" (nada é apagado). Devolve a pasta dessa cópia (ou null se não havia nada). Reiniciar o programa depois.
    /// </summary>
    public string? Restaurar(string arquivo, string senha)
    {
        var zipTmp = Path.Combine(Path.GetTempPath(), "nexus-restaurar-" + Guid.NewGuid().ToString("N") + ".zip");
        var bancoNovo = _caminhoBanco + ".restaurando";
        try
        {
            Descriptografar(arquivo, zipTmp, senha);
            using var zip = ZipFile.OpenRead(zipTmp);
            LerManifesto(zip);

            var entradaBanco = zip.GetEntry(EntradaBanco)
                               ?? throw new RegraDeNegocioException("O backup não contém o banco de dados.");
            Directory.CreateDirectory(_raiz);
            entradaBanco.ExtractToFile(bancoNovo, overwrite: true);
            var (ok, detalhe) = BackupService.Conferir(bancoNovo);
            if (!ok) throw new RegraDeNegocioException("O banco dentro do backup está danificado: " + detalhe);

            // Valida os caminhos ANTES de mexer em qualquer coisa ("zip slip").
            var raizSegura = _raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var dados = new List<(ZipArchiveEntry Entrada, string Alvo)>();
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith(PrefixoDados, StringComparison.Ordinal) || e.Name.Length == 0) continue;
                var alvo = Path.GetFullPath(Path.Combine(_raiz, e.FullName[PrefixoDados.Length..]));
                if (!alvo.StartsWith(raizSegura, StringComparison.OrdinalIgnoreCase))
                    throw new RegraDeNegocioException("O backup tem um caminho inválido e foi recusado.");
                dados.Add((e, alvo));
            }

            // Guarda o que existe hoje.
            var seguranca = Path.Combine(_raiz, "antes-da-restauracao-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var guardou = false;
            SqliteConnection.ClearAllPools();
            if (File.Exists(_caminhoBanco))
            {
                Directory.CreateDirectory(seguranca);
                File.Move(_caminhoBanco, Path.Combine(seguranca, "nexus.db"));
                guardou = true;
            }
            Apagar(_caminhoBanco + "-wal");
            Apagar(_caminhoBanco + "-shm");
            try
            {
                File.Move(bancoNovo, _caminhoBanco);
                foreach (var (entrada, alvo) in dados)
                {
                    if (File.Exists(alvo))
                    {
                        var guardado = Path.Combine(seguranca, Path.GetRelativePath(_raiz, alvo));
                        Directory.CreateDirectory(Path.GetDirectoryName(guardado)!);
                        File.Copy(alvo, guardado, overwrite: true);
                        guardou = true;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
                    entrada.ExtractToFile(alvo, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                // Falhou no meio: volta o banco de antes.
                var guardadoDb = Path.Combine(seguranca, "nexus.db");
                if (File.Exists(guardadoDb))
                {
                    Apagar(_caminhoBanco);
                    File.Move(guardadoDb, _caminhoBanco);
                }
                throw new RegraDeNegocioException("Não foi possível restaurar (seu banco anterior foi mantido): " + ex.Message);
            }
            return guardou ? seguranca : null;
        }
        catch (Exception ex) when (ex is not RegraDeNegocioException)
        {
            throw new RegraDeNegocioException("Não foi possível restaurar o backup: " + ex.Message);
        }
        finally
        {
            Apagar(zipTmp);
            Apagar(bancoNovo);
        }
    }

    // ---------------------------------------------------------------- arquivos

    private IEnumerable<string> ArquivosDeDados(string destinoFinal)
    {
        foreach (var f in Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_raiz, f);
            var partes = rel.Split(Path.DirectorySeparatorChar);
            if (partes.Length > 1 && PastasIgnoradas.Contains(partes[0], StringComparer.OrdinalIgnoreCase)) continue;
            if (partes[0].StartsWith("antes-da-restauracao", StringComparison.OrdinalIgnoreCase)) continue;
            var nome = Path.GetFileName(f);
            if (nome.StartsWith("nexus.db", StringComparison.OrdinalIgnoreCase)) continue; // banco e cópias antigas
            if (nome.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || nome.EndsWith(".restaurando", StringComparison.OrdinalIgnoreCase)
                || nome.EndsWith(Extensao, StringComparison.OrdinalIgnoreCase)) continue;
            if (nome.Equals("atualizador.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(Path.GetFullPath(f), destinoFinal, StringComparison.OrdinalIgnoreCase)) continue;
            yield return f;
        }
    }

    private static void CopiarBanco(string origem, string destino)
    {
        var src = new SqliteConnectionStringBuilder { DataSource = origem, Pooling = false }.ToString();
        var dst = new SqliteConnectionStringBuilder { DataSource = destino, Pooling = false }.ToString();
        using var a = new SqliteConnection(src);
        using var b = new SqliteConnection(dst);
        a.Open();
        b.Open();
        a.BackupDatabase(b);
        using var cmd = b.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE";
        cmd.ExecuteNonQuery();
    }

    private static Manifesto LerManifesto(ZipArchive zip)
    {
        var e = zip.GetEntry(EntradaManifesto) ?? throw new RegraDeNegocioException("Esse arquivo não é um backup completo do Nexus LS.");
        using var s = e.Open();
        return JsonSerializer.Deserialize<Manifesto>(s) ?? throw new RegraDeNegocioException("O backup está ilegível.");
    }

    private static void ValidarSenha(string senha)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < MinimoSenha)
            throw new RegraDeNegocioException($"A senha do backup precisa ter pelo menos {MinimoSenha} caracteres.");
    }

    private static void Apagar(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); } catch (IOException) { }
    }

    private class Manifesto
    {
        public DateTime CriadoEm { get; set; }
        public string VersaoApp { get; set; } = "";
        public int Arquivos { get; set; }
    }

    // ---------------------------------------------------------------- criptografia (blocos AES-GCM)
    // Formato: "NXBK1" | sal(16) | prefixoNonce(8) | { tamanho(int32) | etiqueta(16) | cifrado }...
    // Nonce de cada bloco = prefixoNonce + índice(4). O AAD leva o índice e se é o último bloco,
    // então reordenar ou cortar o arquivo faz a leitura falhar.

    private static byte[] Chave(string senha, byte[] sal) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), sal, Iteracoes, HashAlgorithmName.SHA256, 32);

    private static void Criptografar(Stream entrada, Stream saida, string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var prefixo = RandomNumberGenerator.GetBytes(8);
        saida.Write(Magia);
        saida.Write(sal);
        saida.Write(prefixo);
        using var aes = new AesGcm(Chave(senha, sal), 16);
        var buffer = new byte[TamanhoBloco];
        var proximo = new byte[TamanhoBloco];
        var lidos = LerCheio(entrada, buffer);
        uint indice = 0;
        while (true)
        {
            var seguinte = lidos == TamanhoBloco ? LerCheio(entrada, proximo) : 0;
            var ultimo = seguinte == 0;
            var cifrado = new byte[lidos];
            var etiqueta = new byte[16];
            aes.Encrypt(Nonce(prefixo, indice), buffer.AsSpan(0, lidos), cifrado, etiqueta, Aad(indice, ultimo));
            saida.Write(BitConverter.GetBytes(lidos));
            saida.Write(etiqueta);
            saida.Write(cifrado);
            if (ultimo) break;
            (buffer, proximo) = (proximo, buffer);
            lidos = seguinte;
            indice++;
        }
    }

    private static void Descriptografar(string arquivo, string destinoZip, string senha)
    {
        if (!File.Exists(arquivo)) throw new RegraDeNegocioException("Arquivo de backup não encontrado.");
        try
        {
            using var entrada = File.OpenRead(arquivo);
            var cab = new byte[Magia.Length + 16 + 8];
            if (LerCheio(entrada, cab) != cab.Length || !cab.AsSpan(0, Magia.Length).SequenceEqual(Magia))
                throw new RegraDeNegocioException("Esse arquivo não é um backup completo do Nexus LS.");
            var sal = cab[Magia.Length..(Magia.Length + 16)];
            var prefixo = cab[(Magia.Length + 16)..];
            using var aes = new AesGcm(Chave(senha, sal), 16);
            using var saida = new FileStream(destinoZip, FileMode.Create, FileAccess.Write, FileShare.None);
            var tam = new byte[4];
            var etiqueta = new byte[16];
            uint indice = 0;
            while (true)
            {
                if (LerCheio(entrada, tam) != 4) throw new RegraDeNegocioException("O backup está incompleto ou foi cortado.");
                var n = BitConverter.ToInt32(tam);
                if (n < 0 || n > TamanhoBloco) throw new RegraDeNegocioException("O backup está danificado.");
                if (LerCheio(entrada, etiqueta) != 16) throw new RegraDeNegocioException("O backup está incompleto ou foi cortado.");
                var cifrado = new byte[n];
                if (LerCheio(entrada, cifrado) != n) throw new RegraDeNegocioException("O backup está incompleto ou foi cortado.");
                var ultimo = entrada.Position >= entrada.Length;
                var claro = new byte[n];
                try { aes.Decrypt(Nonce(prefixo, indice), cifrado, etiqueta, claro, Aad(indice, ultimo)); }
                catch (CryptographicException)
                {
                    throw new RegraDeNegocioException("Senha incorreta ou backup danificado.");
                }
                saida.Write(claro);
                if (ultimo) break;
                indice++;
            }
        }
        catch (Exception ex) when (ex is not RegraDeNegocioException)
        {
            Apagar(destinoZip);
            throw new RegraDeNegocioException("Não foi possível abrir o backup: " + ex.Message);
        }
    }

    private static byte[] Nonce(byte[] prefixo, uint indice)
    {
        var n = new byte[12];
        prefixo.CopyTo(n, 0);
        BitConverter.GetBytes(indice).CopyTo(n, 8);
        return n;
    }

    private static byte[] Aad(uint indice, bool ultimo)
    {
        var a = new byte[5];
        BitConverter.GetBytes(indice).CopyTo(a, 0);
        a[4] = (byte)(ultimo ? 1 : 0);
        return a;
    }

    private static int LerCheio(Stream s, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = s.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
