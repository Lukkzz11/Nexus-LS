using Microsoft.Data.Sqlite;

namespace NexusLS.Core.Data;

/// <summary>
/// Ajustes de conexão do SQLite que precisam valer ANTES de o programa usar o banco (idealmente antes das migrations).
/// O modo WAL deixa ler e gravar ao mesmo tempo; o busy_timeout evita "banco bloqueado" quando outra conexão está gravando.
/// </summary>
public static class PreparadorBanco
{
    /// <summary>
    /// Aplica os PRAGMAs numa conexão própria. É idempotente (pode rodar a cada abertura) e cria o arquivo
    /// se ele ainda não existir. Não engole erro: se falhar, o chamador decide se segue ou avisa o usuário.
    /// </summary>
    public static void Aplicar(string caminho)
    {
        var pasta = Path.GetDirectoryName(Path.GetFullPath(caminho));
        if (!string.IsNullOrWhiteSpace(pasta)) Directory.CreateDirectory(pasta);

        using var conexao = new SqliteConnection(CaminhoBanco.StringConexao(caminho));
        conexao.Open();

        Executar(conexao, "PRAGMA journal_mode=WAL;");
        Executar(conexao, "PRAGMA synchronous=NORMAL;");
        Executar(conexao, "PRAGMA busy_timeout=30000;");
        Executar(conexao, "PRAGMA foreign_keys=ON;");
        Executar(conexao, "PRAGMA wal_autocheckpoint=1000;");
    }

    /// <summary>Modo de journal em uso no arquivo (esperado: "wal"). Usado nos testes.</summary>
    public static string JournalMode(string caminho)
    {
        using var conexao = new SqliteConnection(CaminhoBanco.StringConexao(caminho));
        conexao.Open();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "PRAGMA journal_mode;";
        return (comando.ExecuteScalar() as string)?.ToLowerInvariant() ?? string.Empty;
    }

    /// <summary>
    /// Abre como <see cref="Aplicar"/>, mas tolera o "database disk image is malformed" que aparece depois de um
    /// fechamento abrupto: nesse caso os diários (<c>-wal</c> e <c>-shm</c>) são POSTOS DE LADO — renomeados, nunca
    /// apagados — e a abertura é repetida. O arquivo principal não é tocado. Devolve o que foi posto de lado
    /// (nulo quando não foi preciso mexer em nada).
    /// </summary>
    public static string? AplicarComRecuperacao(string caminho)
    {
        try
        {
            Aplicar(caminho);
            return null;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26) // CORRUPT / NOTADB
        {
            var postos = PostarDeLadoDiarios(caminho);
            if (postos.Count == 0) throw; // não era diário sobrando: quem chamou decide o que fazer
            Aplicar(caminho);
            return string.Join("; ", postos);
        }
    }

    /// <summary>
    /// Renomeia <c>banco-wal</c>/<c>banco-shm</c> para <c>...suspeito-AAAAMMDD-HHMMSS</c>. O nome antigo sai do
    /// caminho para o SQLite não tentar usá-lo, mas o arquivo continua no disco para quem precisar investigar.
    /// </summary>
    public static List<string> PostarDeLadoDiarios(string caminho)
    {
        var postos = new List<string>();
        var marcador = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var sufixo in new[] { "-wal", "-shm" })
        {
            var arquivo = caminho + sufixo;
            if (!File.Exists(arquivo)) continue;
            var destino = arquivo + ".suspeito-" + marcador;
            try
            {
                File.Move(arquivo, destino, overwrite: true);
                postos.Add(destino);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return postos;
    }

    private static void Executar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }
}
