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

    public BackupService(string caminhoBanco) => _caminhoBanco = caminhoBanco;

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
        try
        {
            Directory.CreateDirectory(pasta);
            CopiarComApiDoSqlite(_caminhoBanco, tmp);
            var (ok, detalhe) = Conferir(tmp);
            if (!ok) throw new RegraDeNegocioException("A cópia não passou na conferência: " + detalhe);
            File.Move(tmp, destino);
        }
        catch (Exception ex)
        {
            ApagarSeExistir(tmp);
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
        foreach (var b in apagar) ApagarSeExistir(b.Caminho);
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
        try
        {
            SqliteConnection.ClearAllPools(); // solta o arquivo, caso o programa ainda tenha conexões abertas
            File.Copy(arquivoBackup, novo, overwrite: true);

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
            throw new RegraDeNegocioException("Não foi possível restaurar (seu banco atual foi mantido): " + ex.Message);
        }
        return seguranca;
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
