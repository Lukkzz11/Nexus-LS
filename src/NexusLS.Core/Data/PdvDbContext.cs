using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;

namespace NexusLS.Core.Data;

/// <summary>
/// O DbContext é a "ponte" entre as classes C# e o banco: cada DbSet vira uma tabela,
/// e o SaveChanges grava tudo o que mudou de uma vez (numa transação).
/// </summary>
public class PdvDbContext : DbContext
{
    public PdvDbContext(DbContextOptions<PdvDbContext> options) : base(options) { }

    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ProdutoTamanho> TamanhosProduto => Set<ProdutoTamanho>();
    public DbSet<MovimentacaoEstoque> Movimentacoes => Set<MovimentacaoEstoque>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<ItemVenda> ItensVenda => Set<ItemVenda>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ConfiguracaoLoja> ConfiguracoesLoja => Set<ConfiguracaoLoja>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Produto>(e =>
        {
            e.Property(p => p.Nome).IsRequired().HasMaxLength(200);
            e.Property(p => p.CodigoBarras).HasMaxLength(50);
            e.Property(p => p.Referencia).HasMaxLength(60);
            // Código de barras não pode repetir, mas vários produtos podem ficar sem código (nulo).
            e.HasIndex(p => p.CodigoBarras).IsUnique().HasFilter("\"CodigoBarras\" IS NOT NULL");
            e.HasIndex(p => p.Referencia);
            e.HasMany(p => p.Tamanhos).WithOne().HasForeignKey(t => t.ProdutoId);
            e.Ignore(p => p.EstoqueBaixo); // propriedade calculada, não vira coluna
        });

        modelBuilder.Entity<ProdutoTamanho>(e =>
        {
            e.Property(t => t.Tamanho).IsRequired().HasMaxLength(30);
            e.HasIndex(t => new { t.ProdutoId, t.Tamanho }).IsUnique(); // um produto não repete tamanho
            e.Ignore(t => t.EhSemTamanho);
        });

        modelBuilder.Entity<MovimentacaoEstoque>(e =>
        {
            e.Property(m => m.Motivo).HasMaxLength(300);
            e.Property(m => m.Tamanho).HasMaxLength(30);
            e.HasOne(m => m.Produto).WithMany().HasForeignKey(m => m.ProdutoId);
        });

        modelBuilder.Entity<Venda>(e =>
        {
            e.HasMany(v => v.Itens).WithOne().HasForeignKey(i => i.VendaId);
            e.HasOne(v => v.Cliente).WithMany().HasForeignKey(v => v.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => v.DataHora);
            e.Property(v => v.Operador).HasMaxLength(80);
            e.Property(v => v.MotivoCancelamento).HasMaxLength(300);
            e.Property(v => v.CanceladoPor).HasMaxLength(80);
        });

        modelBuilder.Entity<ItemVenda>(e =>
        {
            e.Property(i => i.NomeProduto).IsRequired().HasMaxLength(200);
            e.Property(i => i.Tamanho).IsRequired().HasMaxLength(30);
        });

        modelBuilder.Entity<Cliente>(e =>
        {
            e.Property(c => c.Nome).IsRequired().HasMaxLength(200);
            e.Property(c => c.Documento).HasMaxLength(20);
            e.Property(c => c.Telefone).HasMaxLength(30);
            e.Property(c => c.Observacao).HasMaxLength(500);
            e.HasIndex(c => c.Nome);
        });

        modelBuilder.Entity<ConfiguracaoLoja>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever(); // sempre Id = 1
            e.Property(c => c.NomeLoja).IsRequired().HasMaxLength(120);
            e.Property(c => c.Documento).HasMaxLength(30);
            e.Property(c => c.Endereco).HasMaxLength(250);
            e.Property(c => c.Telefone).HasMaxLength(30);
            e.Property(c => c.MensagemCupom).HasMaxLength(250);
            e.Property(c => c.ImpressoraCupom).HasMaxLength(200);
            e.Property(c => c.NomeOperador).IsRequired().HasMaxLength(80);
        });
    }
}
