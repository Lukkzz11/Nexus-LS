using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexusLS.Core.Data;

/// <summary>
/// Usada só pelas ferramentas do EF (dotnet ef migrations add ...), que precisam criar o contexto
/// sem rodar o aplicativo.
/// </summary>
public class PdvDbContextFactory : IDesignTimeDbContextFactory<PdvDbContext>
{
    public PdvDbContext CreateDbContext(string[] args)
    {
        var opcoes = new DbContextOptionsBuilder<PdvDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "pdv-design.db")}")
            .Options;
        return new PdvDbContext(opcoes);
    }
}
