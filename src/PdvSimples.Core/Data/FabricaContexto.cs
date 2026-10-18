using Microsoft.EntityFrameworkCore;

namespace PdvSimples.Core.Data;

/// <summary>
/// Cria um DbContext novo a cada pedido. Num app de janela, o certo é abrir um contexto curto
/// para cada operação (buscar, salvar), em vez de manter um aberto o programa inteiro.
/// </summary>
public class FabricaContexto : IDbContextFactory<PdvDbContext>
{
    private readonly DbContextOptions<PdvDbContext> _opcoes;

    public FabricaContexto(DbContextOptions<PdvDbContext> opcoes) => _opcoes = opcoes;

    public PdvDbContext CreateDbContext() => new(_opcoes);
}
