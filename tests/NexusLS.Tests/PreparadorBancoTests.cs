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

    [Fact]
    public void Banco_saudavel_nao_mexe_em_nada_e_nao_cria_sobra()
    {
        Directory.CreateDirectory(_pasta);
        var caminho = Path.Combine(_pasta, "teste.db");
        PreparadorBanco.Aplicar(caminho);

        var posto = PreparadorBanco.AplicarComRecuperacao(caminho);

        Assert.Null(posto);
        Assert.Empty(Directory.EnumerateFiles(_pasta, "*.suspeito-*"));
    }

    [Fact]
    public void Diario_invalido_e_renomeado_e_nunca_apagado()
    {
        // O que sobra de um fechamento abrupto: os arquivos -wal/-shm saem do caminho, mas continuam no disco.
        Directory.CreateDirectory(_pasta);
        var caminho = Path.Combine(_pasta, "teste.db");
        File.WriteAllText(caminho + "-wal", "conteudo do diario");
        File.WriteAllText(caminho + "-shm", "conteudo da memoria");

        var postos = PreparadorBanco.PostarDeLadoDiarios(caminho);

        Assert.Equal(2, postos.Count);
        Assert.False(File.Exists(caminho + "-wal"));
        Assert.False(File.Exists(caminho + "-shm"));
        foreach (var posto in postos)
        {
            Assert.True(File.Exists(posto));
            Assert.Contains(".suspeito-", posto);
        }
    }

    [Fact]
    public void Arquivo_que_nao_e_banco_continua_falhando_e_fica_intacto()
    {
        Directory.CreateDirectory(_pasta);
        var caminho = Path.Combine(_pasta, "teste.db");
        File.WriteAllText(caminho, "isto não é um banco de dados");

        Assert.Throws<SqliteException>(() => PreparadorBanco.AplicarComRecuperacao(caminho));

        SqliteConnection.ClearAllPools(); // o pool segura o arquivo: solta antes de conferir o conteúdo
        Assert.Equal("isto não é um banco de dados", File.ReadAllText(caminho));
        Assert.Empty(Directory.EnumerateFiles(_pasta, "*.suspeito-*"));
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
