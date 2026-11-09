using Microsoft.Data.Sqlite;
using NexusLS.Core.Data;

namespace NexusLS.Tests;

public class PreparadorBancoTests : IDisposable
{
    private readonly string _pasta =
        Path.Combine(Path.GetTempPath(), "NexusLS-Preparador-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Aplicar_liga_wal_e_a_string_de_conexao_aplica_os_pragmas()
    {
        Directory.CreateDirectory(_pasta);
        var caminho = Path.Combine(_pasta, "teste.db");

        PreparadorBanco.Aplicar(caminho);
        Assert.True(File.Exists(caminho));
        Assert.Equal("wal", PreparadorBanco.JournalMode(caminho));

        using var conexao = new SqliteConnection(CaminhoBanco.StringConexao(caminho));
        conexao.Open();
        Assert.Equal(1L, LerInteiro(conexao, "PRAGMA foreign_keys;"));
        Assert.Equal(30000L, LerInteiro(conexao, "PRAGMA busy_timeout;"));

        PreparadorBanco.Aplicar(caminho); // idempotente: pode rodar a cada abertura
        Assert.Equal("wal", PreparadorBanco.JournalMode(caminho));
    }

    private static long LerInteiro(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return Convert.ToInt64(comando.ExecuteScalar());
    }

    public void Dispose()
    {
        // O pool pode manter o arquivo aberto no Windows: solta antes de apagar a pasta temporária.
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
        }
        catch (IOException) { /* pasta temporária: o teste não depende da limpeza */ }
        catch (UnauthorizedAccessException) { }
    }
}
