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

    private static void Executar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }
}
