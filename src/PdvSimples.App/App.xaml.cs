using System.Windows;
using Microsoft.EntityFrameworkCore;
using PdvSimples.Core.Data;

namespace PdvSimples.App;

public partial class App : Application
{
    /// <summary>Opções do banco compartilhadas pelo app (aponta para %LOCALAPPDATA%\PdvSimples\pdv.db).</summary>
    public static DbContextOptions<PdvDbContext> OpcoesBanco { get; } =
        new DbContextOptionsBuilder<PdvDbContext>()
            .UseSqlite($"Data Source={CaminhoBanco.Padrao()}")
            .Options;

    /// <summary>
    /// OnStartup roda uma vez quando o programa abre. Migrate() cria o arquivo do banco
    /// se não existir e aplica as migrations pendentes (as "versões" da estrutura das tabelas).
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var db = new PdvDbContext(OpcoesBanco);
        db.Database.Migrate();
    }
}
