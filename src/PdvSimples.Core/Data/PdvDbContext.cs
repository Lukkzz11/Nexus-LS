using Microsoft.EntityFrameworkCore;
using PdvSimples.Core.Models;

namespace PdvSimples.Core.Data;

/// <summary>
/// O DbContext é a "ponte" entre as classes C# e o banco: cada DbSet vira uma tabela,
/// e o SaveChanges grava tudo o que mudou de uma vez (numa transação).
/// </summary>
public class PdvDbContext : DbContext
{
    public PdvDbContext(DbContextOptions<PdvDbContext> options) : base(options) { }

    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<MovimentacaoEstoque> Movimentacoes => Set<MovimentacaoEstoque>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<ItemVenda> ItensVenda => Set<ItemVenda>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Produto>(e =>
        {
            e.Property(p => p.Nome).IsRequired().HasMaxLength(200);
            e.Property(p => p.CodigoBarras).HasMaxLength(50);
            // Código de barras não pode repetir, mas vários produtos podem ficar sem código (nulo).
            e.HasIndex(p => p.CodigoBarras).IsUnique().HasFilter("\"CodigoBarras\" IS NOT NULL");
            e.Ignore(p => p.EstoqueBaixo); // propriedade calculada, não vira coluna
        });

        modelBuilder.Entity<MovimentacaoEstoque>(e =>
        {
            e.Property(m => m.Motivo).HasMaxLength(300);
            e.HasOne(m => m.Produto).WithMany().HasForeignKey(m => m.ProdutoId);
        });

        modelBuilder.Entity<Venda>(e =>
        {
            e.HasMany(v => v.Itens).WithOne().HasForeignKey(i => i.VendaId);
            e.HasIndex(v => v.DataHora);
        });

        modelBuilder.Entity<ItemVenda>(e =>
        {
            e.Property(i => i.NomeProduto).IsRequired().HasMaxLength(200);
        });
    }
}
