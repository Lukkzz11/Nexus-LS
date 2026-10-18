using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PdvSimples.Core.Data;

namespace PdvSimples.Tests.Infra;

/// <summary>
/// Cria um SQLite em memória por teste. A conexão precisa ficar aberta, senão o banco some.
/// </summary>
public sealed class BancoDeTeste : IDisposable
{
    private readonly SqliteConnection _conexao;

    public BancoDeTeste()
    {
        _conexao = new SqliteConnection("DataSource=:memory:");
        _conexao.Open();
        using var db = CriarContexto();
        db.Database.Migrate();
    }

    public PdvDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<PdvDbContext>().UseSqlite(_conexao).Options);

    public void Dispose() => _conexao.Dispose();
}
