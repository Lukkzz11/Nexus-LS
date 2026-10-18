using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;

namespace NexusLS.Tests.Infra;

/// <summary>
/// Cria um SQLite em memória por teste. A conexão precisa ficar aberta, senão o banco some.
/// </summary>
public sealed class BancoDeTeste : IDisposable
{
    private readonly SqliteConnection _conexao;

    /// <param name="aplicarMigracoes">false = banco vazio, para testar a migração de dados antigos passo a passo.</param>
    public BancoDeTeste(bool aplicarMigracoes = true)
    {
        _conexao = new SqliteConnection("DataSource=:memory:");
        _conexao.Open();
        if (!aplicarMigracoes) return;
        using var db = CriarContexto();
        db.Database.Migrate();
    }

    private DbContextOptions<PdvDbContext> Opcoes =>
        new DbContextOptionsBuilder<PdvDbContext>().UseSqlite(_conexao).Options;

    /// <summary>Fábrica que entrega contextos novos sobre a mesma conexão (usada pelos serviços).</summary>
    public FabricaContexto Fabrica => new(Opcoes);

    public PdvDbContext CriarContexto() => new(Opcoes);

    public void Dispose() => _conexao.Dispose();
}
