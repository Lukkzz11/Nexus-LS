using System.IO.Compression;
using Microsoft.Data.Sqlite;
using NexusLS.Core.Services;

namespace NexusLS.Core.Backup;

/// <summary>
/// Copia e restaura o banco SQLite com segurança, mesmo com o programa aberto.
/// Copiar o arquivo .db "no braço" NÃO serve: com o modo WAL parte dos dados fica no arquivo -wal e a cópia sai
/// incompleta. Aqui se usa a API de backup do próprio SQLite, que entrega uma cópia coerente.
/// </summary>
public class BackupService
{
    private readonly string _caminhoBanco;
    private readonly string _pastaAnexos;

    /// <summary>Sufixo do arquivo com os anexos de um backup: "nexus_..._manual.db" ganha "nexus_..._manual.db.anexos.zip".</summary>
    public const string SufixoAnexos = ".anexos.zip";

    /// <param name="pastaAnexos">Pasta dos anexos dos lançamentos. Nulo = a pasta "anexos" ao lado do banco.</param>
    public BackupService(string caminhoBanco, string? pastaAnexos = null)
    {
        _caminhoBanco = caminhoBanco;
        _pastaAnexos = pastaAnexos ?? AnexoService.PastaPadrao(caminhoBanco);
    }

    /// <summary>O arquivo (zip) dos anexos que acompanha um backup do banco.</summary>
    public static string CaminhoAnexosDoBackup(string arquivoBackup) => arquivoBackup + SufixoAnexos;

    /// <summary>
    /// Faz um backup na pasta. Passo a passo: copia para um arquivo .tmp, confere a integridade da CÓPIA
    /// e só então dá o nome final. Backup que falha na conferência é descartado e o erro aparece.
    /// </summary>
    public Task<InfoBackup> FazerAsync(string pasta, MotivoBackup motivo, int manter = 0, DateTime? agora = null) =>
        Task.Run(() => Fazer(pasta, motivo, manter, agora));

    public InfoBackup Fazer(string pasta, MotivoBackup motivo, int manter = 0, DateTime? agora = null)
    {
        var momento = agora ?? DateTime.Now;
        if (!File.Exists(_caminhoBanco))
            throw new RegraDeNegocioException("Ainda não existe banco de dados para copiar.");
        if (string.IsNullOrWhiteSpace(pasta))
            throw new RegraDeNegocioException("Escolha a pasta dos backups.");

        var destino = ProximoNomeLivre(pasta, momento, motivo);
        var tmp = destino + ".tmp";
        var zipTmp = CaminhoAnexosDoBackup(destino) + ".tmp";
        try
        {
            Directory.CreateDirectory(pasta);
            CopiarComApiDoSqlite(_caminhoBanco, tmp);
            var (ok, detalhe) = Conferir(tmp);
            if (!ok) throw new RegraDeNegocioException("A cópia não passou na conferência: " + detalhe);
            CompactarAnexos(zipTmp);
            File.Move(tmp, destino);
            if (File.Exists(zipTmp)) File.Move(zipTmp, CaminhoAnexosDoBackup(destino), overwrite: true);
        }
        catch (Exception ex)
        {
            ApagarSeExistir(tmp);
            ApagarSeExistir(zipTmp);
            if (ex is RegraDeNegocioException) throw;
            throw new RegraDeNegocioException("Não foi possível fazer o backup: " + ex.Message);
        }

        var info = new InfoBackup(destino, momento, motivo, new FileInfo(destino).Length);
        if (manter > 0) AplicarRetencao(pasta, manter);
        return info;
    }

    /// <summary>Os backups da pasta (só arquivos com o nome do Nexus LS), do mais novo para o mais antigo.</summary>
    public IReadOnlyList<InfoBackup> Listar(string pasta)
    {
        if (!Directory.Exists(pasta)) return Array.Empty<InfoBackup>();
        var lista = new List<InfoBackup>();
        foreach (var arquivo in Directory.EnumerateFiles(pasta, "nexus_*.db"))
            if (RegrasBackup.TentarLerNome(Path.GetFileName(arquivo), out var data, out var motivo))
                lista.Add(new InfoBackup(arquivo, data, motivo, new FileInfo(arquivo).Length));
        return lista.OrderByDescending(b => b.Data).ToList();
    }

    /// <summary>Apaga os backups comuns que passaram do limite. Só toca em arquivos com o nome padrão do Nexus LS.</summary>
    public int AplicarRetencao(string pasta, int manter)
    {
        var apagar = RegrasBackup.SelecionarParaApagar(Listar(pasta), manter);
        foreach (var b in apagar)
        {
            ApagarSeExistir(b.Caminho);
            ApagarSeExistir(CaminhoAnexosDoBackup(b.Caminho));
        }
        return apagar.Count;
    }

    /// <summary>
    /// Confere se o arquivo é um banco do Nexus LS íntegro (PRAGMA integrity_check e as tabelas principais).
    /// Devolve (true, "ok") ou (false, o motivo).
    /// </summary>
    public static (bool Ok, string Detalhe) Conferir(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return (false, "arquivo não encontrado");
            var cs = new SqliteConnectionStringBuilder { DataSource = arquivo, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var con = new SqliteConnection(cs);
            con.Open();

            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check";
                var resultado = cmd.ExecuteScalar()?.ToString();
                if (resultado != "ok") return (false, "o banco está danificado (" + resultado + ")");
            }

            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Produtos','Vendas','__EFMigrationsHistory')";
                if (Convert.ToInt32(cmd.ExecuteScalar()) != 3)
                    return (false, "não parece um banco do Nexus LS");
            }
            return (true, "ok");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Troca o banco atual pelo backup. Antes: confere o backup e guarda uma cópia do banco atual
    /// (motivo "pre-restauracao"), para a restauração poder ser desfeita. O programa precisa ser reiniciado depois.
    /// Devolve o backup de segurança (ou null se não havia banco atual).
    /// </summary>
    public Task<InfoBackup?> RestaurarAsync(string arquivoBackup, string pastaBackups) =>
        Task.Run(() => Restaurar(arquivoBackup, pastaBackups));

    public InfoBackup? Restaurar(string arquivoBackup, string pastaBackups)
    {
        var (ok, detalhe) = Conferir(arquivoBackup);
        if (!ok) throw new RegraDeNegocioException("Esse backup não pode ser restaurado: " + detalhe);

        InfoBackup? seguranca = File.Exists(_caminhoBanco)
            ? Fazer(pastaBackups, MotivoBackup.PreRestauracao)
            : null;

        var novo = _caminhoBanco + ".restaurando";
        var antigo = _caminhoBanco + ".antes";
        var zipAnexos = CaminhoAnexosDoBackup(arquivoBackup);
        var temAnexos = File.Exists(zipAnexos);
        var anexosNovos = _pastaAnexos.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".restaurando";
        try
        {
            SqliteConnection.ClearAllPools(); // solta o arquivo, caso o programa ainda tenha conexões abertas
            File.Copy(arquivoBackup, novo, overwrite: true);
            if (temAnexos) ExtrairAnexos(zipAnexos, anexosNovos); // antes de trocar o banco: se falhar, nada mudou

            var havia = File.Exists(_caminhoBanco);
            // Se o banco estiver em uso, este Move falha e NADA foi alterado.
            if (havia) File.Move(_caminhoBanco, antigo, overwrite: true);
            try
            {
                // Os arquivos -wal e -shm do banco antigo não podem sobrar: misturados ao banco novo, estragariam o arquivo.
                ApagarSeExistir(_caminhoBanco + "-wal");
                ApagarSeExistir(_caminhoBanco + "-shm");
                File.Move(novo, _caminhoBanco);
            }
            catch
            {
                if (havia && !File.Exists(_caminhoBanco)) File.Move(antigo, _caminhoBanco);
                throw;
            }
            ApagarSeExistir(antigo);
        }
        catch (Exception ex)
        {
            ApagarSeExistir(novo);
            ApagarPastaSeExistir(anexosNovos);
            throw new RegraDeNegocioException("Não foi possível restaurar (seu banco atual foi mantido): " + ex.Message);
        }

        // O banco já foi trocado. Backups antigos (sem zip de anexos) deixam a pasta de anexos como está.
        if (temAnexos)
        {
            var antes = anexosNovos[..^".restaurando".Length] + ".antes";
            try
            {
                ApagarPastaSeExistir(antes);
                var havia = Directory.Exists(_pastaAnexos);
                if (havia) Directory.Move(_pastaAnexos, antes);
                try { Directory.Move(anexosNovos, _pastaAnexos); }
                catch
                {
                    if (havia && !Directory.Exists(_pastaAnexos)) Directory.Move(antes, _pastaAnexos);
                    throw;
                }
                ApagarPastaSeExistir(antes);
            }
            catch (Exception ex)
            {
                ApagarPastaSeExistir(anexosNovos);
                throw new RegraDeNegocioException("O banco foi restaurado, mas não foi possível restaurar a pasta de anexos (ela foi mantida como estava): " + ex.Message);
            }
        }
        return seguranca;
    }

    /// <summary>Zip com a pasta de anexos (caminhos relativos, com "/"). Não cria nada se a pasta não existe ou está vazia.</summary>
    private void CompactarAnexos(string zip)
    {
        if (!Directory.Exists(_pastaAnexos)) return;
        var raiz = Path.GetFullPath(_pastaAnexos).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var arquivos = Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)).ToList();
        if (arquivos.Count == 0) return;
        ApagarSeExistir(zip);
        using var fs = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None);
        using var arq = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var f in arquivos)
        {
            var nome = Path.GetRelativePath(raiz, f).Replace(Path.DirectorySeparatorChar, '/');
            arq.CreateEntryFromFile(f, nome, CompressionLevel.Fastest);
        }
    }

    /// <summary>Extrai o zip na pasta indicada (recriada). Recusa entrada que escape da pasta ("zip slip").</summary>
    private static void ExtrairAnexos(string zip, string destino)
    {
        ApagarPastaSeExistir(destino);
        Directory.CreateDirectory(destino);
        var raiz = Path.GetFullPath(destino).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var arq = ZipFile.OpenRead(zip);
        foreach (var e in arq.Entries)
        {
            if (e.FullName.EndsWith('/') || e.Name.Length == 0) continue;
            var alvo = Path.GetFullPath(Path.Combine(raiz, e.FullName));
            if (!alvo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                throw new RegraDeNegocioException("O arquivo de anexos do backup tem um caminho inválido.");
            Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
            e.ExtractToFile(alvo, overwrite: true);
        }
    }

    private static void ApagarPastaSeExistir(string pasta)
    {
        try { if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); }
        catch (IOException) { /* pasta presa: não derruba a operação */ }
    }

    private static void CopiarComApiDoSqlite(string origem, string destino)
    {
        ApagarSeExistir(destino);
        var csOrigem = new SqliteConnectionStringBuilder { DataSource = origem, Pooling = false }.ToString();
        var csDestino = new SqliteConnectionStringBuilder { DataSource = destino, Pooling = false }.ToString();
        using var src = new SqliteConnection(csOrigem);
        using var dst = new SqliteConnection(csDestino);
        src.Open();
        dst.Open();
        src.BackupDatabase(dst);
        // A cópia sai em um único arquivo (sem -wal/-shm).
        using var cmd = dst.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE";
        cmd.ExecuteNonQuery();
    }

    private static string ProximoNomeLivre(string pasta, DateTime momento, MotivoBackup motivo)
    {
        var nome = RegrasBackup.NomeArquivo(momento, motivo);
        var caminho = Path.Combine(pasta, nome);
        for (var i = 2; File.Exists(caminho); i++)
            caminho = Path.Combine(pasta, nome.Replace(".db", $"_{i}.db"));
        return caminho;
    }

    private static void ApagarSeExistir(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); }
        catch (IOException) { /* arquivo temporário preso: não derruba a operação */ }
    }
}
