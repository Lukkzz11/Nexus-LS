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
    public DbSet<PagamentoCliente> PagamentosCliente => Set<PagamentoCliente>();
    public DbSet<AjusteCreditoCliente> AjustesCreditoCliente => Set<AjusteCreditoCliente>();
    public DbSet<TurnoCaixa> TurnosCaixa => Set<TurnoCaixa>();
    public DbSet<MovimentoCaixa> MovimentosCaixa => Set<MovimentoCaixa>();
    public DbSet<FechamentoCaixa> FechamentosCaixa => Set<FechamentoCaixa>();
    public DbSet<SincronizacaoProduto> SincronizacaoProdutos => Set<SincronizacaoProduto>();
    public DbSet<SincronizacaoLog> SincronizacaoLogs => Set<SincronizacaoLog>();
    public DbSet<ItemFilaSincronizacao> FilaSincronizacao => Set<ItemFilaSincronizacao>();
    public DbSet<ImportacaoErpItem> ImportacaoErp => Set<ImportacaoErpItem>();
    public DbSet<ImportacaoErpCursor> ImportacaoErpCursores => Set<ImportacaoErpCursor>();
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<CategoriaFinanceira> CategoriasFinanceiras => Set<CategoriaFinanceira>();
    public DbSet<AjusteTituloPrazo> AjustesTitulosPrazo => Set<AjusteTituloPrazo>();
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<Compromisso> Compromissos => Set<Compromisso>();
    public DbSet<MateriaPrima> MateriasPrimas => Set<MateriaPrima>();
    public DbSet<OrdemProducao> OrdensProducao => Set<OrdemProducao>();
    public DbSet<OrdemProducaoGrade> GradesOrdemProducao => Set<OrdemProducaoGrade>();
    public DbSet<PerdaProducao> PerdasProducao => Set<PerdaProducao>();
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();
    public DbSet<EventoPedido> EventosPedido => Set<EventoPedido>();
    public DbSet<Compra> Compras => Set<Compra>();
    public DbSet<ItemCompra> ItensCompra => Set<ItemCompra>();
    public DbSet<Atividade> Atividades => Set<Atividade>();
    public DbSet<ContaFinanceira> ContasFinanceiras => Set<ContaFinanceira>();
    public DbSet<TransferenciaConta> Transferencias => Set<TransferenciaConta>();
    public DbSet<LiquidacaoCartao> LiquidacoesCartao => Set<LiquidacaoCartao>();
    public DbSet<ChequeRecebido> ChequesRecebidos => Set<ChequeRecebido>();
    public DbSet<ChequeEmitido> ChequesEmitidos => Set<ChequeEmitido>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContaFinanceira>(e =>
        {
            e.Property(c => c.Nome).IsRequired().HasMaxLength(80);
        });

        modelBuilder.Entity<TransferenciaConta>(e =>
        {
            e.Property(t => t.Descricao).HasMaxLength(200);
            e.Property(t => t.Operador).HasMaxLength(80);
            e.Property(t => t.MotivoCancelamento).HasMaxLength(200);
            e.HasOne<ContaFinanceira>().WithMany().HasForeignKey(t => t.ContaOrigemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContaFinanceira>().WithMany().HasForeignKey(t => t.ContaDestinoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.Data);
        });

        modelBuilder.Entity<LiquidacaoCartao>(e =>
        {
            e.Property(l => l.Operador).HasMaxLength(80);
            e.Property(l => l.MotivoCancelamento).HasMaxLength(200);
            e.HasOne<Venda>().WithMany().HasForeignKey(l => l.VendaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContaFinanceira>().WithMany().HasForeignKey(l => l.ContaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => l.VendaId);
        });

        modelBuilder.Entity<ChequeRecebido>(e =>
        {
            e.Property(c => c.Numero).HasMaxLength(30);
            e.Property(c => c.Banco).HasMaxLength(60);
            e.Property(c => c.Emitente).HasMaxLength(120);
            e.Property(c => c.Observacao).HasMaxLength(300);
            e.Property(c => c.MotivoDevolucao).HasMaxLength(200);
            e.Property(c => c.Operador).HasMaxLength(80);
            e.HasIndex(c => c.BomPara);
            e.HasIndex(c => c.VendaId);
        });

        modelBuilder.Entity<ChequeEmitido>(e =>
        {
            e.Property(c => c.Numero).HasMaxLength(30);
            e.Property(c => c.Banco).HasMaxLength(60);
            e.Property(c => c.Favorecido).HasMaxLength(120);
            e.Property(c => c.MotivoCancelamento).HasMaxLength(200);
            e.Property(c => c.Operador).HasMaxLength(80);
            e.HasOne<Lancamento>().WithMany().HasForeignKey(c => c.LancamentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContaFinanceira>().WithMany().HasForeignKey(c => c.ContaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.LancamentoId);
            e.HasIndex(c => c.BomPara);
        });

        modelBuilder.Entity<Atividade>(e =>
        {
            e.Property(a => a.Operador).HasMaxLength(80);
            e.Property(a => a.Descricao).IsRequired().HasMaxLength(500);
            e.Property(a => a.Referencia).HasMaxLength(120);
            e.HasIndex(a => a.DataHora);
        });

        modelBuilder.Entity<Produto>(e =>
        {
            e.Property(p => p.Nome).IsRequired().HasMaxLength(200);
            e.Property(p => p.CodigoBarras).HasMaxLength(50);
            e.Property(p => p.Referencia).HasMaxLength(60);
            e.Property(p => p.CodigoInterno).HasMaxLength(20);
            e.Property(p => p.Categoria).HasMaxLength(60);
            e.Property(p => p.Cores).HasMaxLength(200);
            e.HasIndex(p => p.CodigoInterno).IsUnique().HasFilter("\"CodigoInterno\" IS NOT NULL");
            // Código de barras não pode repetir, mas vários produtos podem ficar sem código (nulo).
            e.HasIndex(p => p.CodigoBarras).IsUnique().HasFilter("\"CodigoBarras\" IS NOT NULL");
            e.HasIndex(p => p.Referencia);
            e.HasIndex(p => p.Guid).IsUnique();
            e.Property(p => p.Guid).ValueGeneratedNever();
            e.HasMany(p => p.Tamanhos).WithOne().HasForeignKey(t => t.ProdutoId);
            e.Ignore(p => p.EstoqueBaixo); // propriedade calculada, não vira coluna
        });

        modelBuilder.Entity<SincronizacaoProduto>(e =>
        {
            e.Property(x => x.HashEnviado).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.ProdutoGuid).IsUnique(); // um registro de estado por produto
        });

        modelBuilder.Entity<SincronizacaoLog>(e =>
        {
            e.Property(x => x.Operador).HasMaxLength(80);
            e.Property(x => x.Mensagem).HasMaxLength(1000);
            e.HasIndex(x => x.IniciadaEm);
        });

        modelBuilder.Entity<ImportacaoErpItem>(e =>
        {
            e.HasIndex(x => new { x.Colecao, x.ErpId }).IsUnique(); // um documento do ERP = um registro aqui
            e.HasIndex(x => x.EntidadeGuid);
        });
        modelBuilder.Entity<ImportacaoErpCursor>(e => e.HasKey(x => x.Chave));

        modelBuilder.Entity<ItemFilaSincronizacao>(e =>
        {
            e.HasIndex(x => new { x.Entidade, x.EntidadeGuid }).IsUnique(); // uma linha por entidade: a fila nunca duplica
            e.Property(x => x.Entidade).HasMaxLength(40);
            e.HasIndex(x => new { x.Estado, x.ProximaTentativa });
            e.Property(x => x.UltimoErro).HasMaxLength(500);
            e.Property(x => x.UsuarioUid).HasMaxLength(128);
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
            e.HasIndex(v => v.ChaveUnica).IsUnique().HasFilter("\"ChaveUnica\" IS NOT NULL");
            e.Property(v => v.Operador).HasMaxLength(80);
            e.Property(v => v.MotivoCancelamento).HasMaxLength(300);
            e.Property(v => v.CanceladoPor).HasMaxLength(80);
        });

        modelBuilder.Entity<ItemVenda>(e =>
        {
            e.Property(i => i.BaixouEstoque).HasDefaultValue(true);
            e.Property(i => i.NomeProduto).IsRequired().HasMaxLength(200);
            e.Property(i => i.Tamanho).IsRequired().HasMaxLength(30);
        });

        modelBuilder.Entity<Cliente>(e =>
        {
            e.Property(c => c.Nome).IsRequired().HasMaxLength(200);
            e.Property(c => c.RazaoSocial).HasMaxLength(200);
            e.Property(c => c.Documento).HasMaxLength(20);
            e.Property(c => c.Telefone).HasMaxLength(30);
            e.Property(c => c.Email).HasMaxLength(150);
            e.Property(c => c.Observacao).HasMaxLength(500);
            e.Property(c => c.InscricaoEstadual).HasMaxLength(30);
            e.Property(c => c.Cep).HasMaxLength(12);
            e.Property(c => c.Endereco).HasMaxLength(200);
            e.Property(c => c.Numero).HasMaxLength(20);
            e.Property(c => c.Complemento).HasMaxLength(100);
            e.Property(c => c.Bairro).HasMaxLength(100);
            e.Property(c => c.Cidade).HasMaxLength(100);
            e.Property(c => c.Estado).HasMaxLength(2);
            e.HasIndex(c => c.Nome);
        });

        modelBuilder.Entity<AjusteCreditoCliente>(e =>
        {
            e.Property(a => a.Motivo).IsRequired().HasMaxLength(200);
            e.Property(a => a.Operador).HasMaxLength(80);
            e.HasOne(a => a.Cliente).WithMany().HasForeignKey(a => a.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.ClienteId);
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

        modelBuilder.Entity<PagamentoCliente>(e =>
        {
            e.Property(p => p.Observacao).HasMaxLength(200);
            e.Property(p => p.Operador).HasMaxLength(80);
            e.HasOne<Cliente>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.ClienteId);
        });

        modelBuilder.Entity<Pedido>(e =>
        {
            e.Property(p => p.Numero).IsRequired().HasMaxLength(20);
            e.Property(p => p.ClienteNome).IsRequired().HasMaxLength(150);
            e.Property(p => p.ClienteTelefone).HasMaxLength(30);
            e.Property(p => p.Observacao).HasMaxLength(300);
            e.Property(p => p.MotivoCancelamento).HasMaxLength(200);
            e.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Historico).WithOne().HasForeignKey(h => h.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.Status);
            e.HasIndex(p => p.VendaId);
            e.HasIndex(p => p.CriadoEm);
        });

        modelBuilder.Entity<ItemPedido>(e =>
        {
            e.Property(i => i.Nome).IsRequired().HasMaxLength(200);
            e.Property(i => i.Tamanho).IsRequired().HasMaxLength(30);
            e.Ignore(i => i.TotalCentavos);
        });

        modelBuilder.Entity<EventoPedido>(e =>
        {
            e.Property(h => h.Operador).HasMaxLength(80);
            e.Property(h => h.Observacao).HasMaxLength(300);
        });

        modelBuilder.Entity<Funcionario>(e =>
        {
            e.Property(f => f.Nome).IsRequired().HasMaxLength(80);
            e.Property(f => f.Cargo).HasMaxLength(60);
            e.Property(f => f.Telefone).HasMaxLength(30);
            e.Property(f => f.PinHash).IsRequired().HasMaxLength(120);
            e.Property(f => f.RecuperacaoHash).HasMaxLength(120);
            e.Ignore(f => f.PermissoesEfetivas);
            e.Ignore(f => f.PerfilTexto);
            e.HasIndex(f => f.Nome);
        });

        modelBuilder.Entity<Fornecedor>(e =>
        {
            e.Property(f => f.Nome).IsRequired().HasMaxLength(150);
            e.Property(f => f.Documento).HasMaxLength(30);
            e.Property(f => f.Telefone).HasMaxLength(30);
            e.Property(f => f.Email).HasMaxLength(120);
            e.Property(f => f.Observacao).HasMaxLength(300);
            e.HasIndex(f => f.Nome);
        });

        modelBuilder.Entity<Compromisso>(e =>
        {
            e.Property(c => c.Titulo).IsRequired().HasMaxLength(150);
            e.Property(c => c.Descricao).HasMaxLength(1000);
            e.HasIndex(c => c.Inicio);
        });

        modelBuilder.Entity<MateriaPrima>(e =>
        {
            e.Property(m => m.Nome).IsRequired().HasMaxLength(120);
            e.Property(m => m.Tipo).HasMaxLength(60);
            e.Property(m => m.Unidade).IsRequired().HasMaxLength(20);
            e.Property(m => m.Fornecedor).HasMaxLength(120);
            e.Property(m => m.Localizacao).HasMaxLength(80);
            e.Ignore(m => m.AbaixoDoMinimo);
            e.HasIndex(m => m.Nome);
        });

        modelBuilder.Entity<OrdemProducao>(e =>
        {
            e.Property(o => o.Codigo).IsRequired().HasMaxLength(20);
            e.Property(o => o.Modelo).IsRequired().HasMaxLength(150);
            e.Property(o => o.Referencia).HasMaxLength(60);
            e.Property(o => o.Categoria).HasMaxLength(60);
            e.Property(o => o.TecidoUtilizado).HasMaxLength(120);
            e.Property(o => o.Responsavel).HasMaxLength(80);
            e.Property(o => o.Observacoes).HasMaxLength(500);
            e.Ignore(o => o.TotalPlanejado);
            e.Ignore(o => o.CustoEstimadoTotalCentavos);
            e.HasMany(o => o.Grade).WithOne().HasForeignKey(g => g.OrdemProducaoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(o => o.Codigo).IsUnique();
        });

        modelBuilder.Entity<OrdemProducaoGrade>(e =>
        {
            e.Property(g => g.Tamanho).IsRequired().HasMaxLength(30);
        });

        modelBuilder.Entity<PerdaProducao>(e =>
        {
            e.Property(p => p.OrdemCodigo).HasMaxLength(20);
            e.Property(p => p.MaterialNome).IsRequired().HasMaxLength(120);
            e.Property(p => p.Unidade).IsRequired().HasMaxLength(20);
            e.Property(p => p.Observacao).HasMaxLength(300);
            e.Property(p => p.RegistradoPor).HasMaxLength(80);
            e.HasIndex(p => p.DataHora);
        });

        modelBuilder.Entity<Compra>(e =>
        {
            e.Property(c => c.NumeroNota).HasMaxLength(40);
            e.Property(c => c.Observacao).HasMaxLength(300);
            e.Property(c => c.Operador).HasMaxLength(80);
            e.Property(c => c.MotivoCancelamento).HasMaxLength(200);
            e.HasOne(c => c.Fornecedor).WithMany().HasForeignKey(c => c.FornecedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(c => c.Itens).WithOne().HasForeignKey(i => i.CompraId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.DataHora);
        });

        modelBuilder.Entity<ItemCompra>(e =>
        {
            e.Property(i => i.NomeProduto).IsRequired().HasMaxLength(200);
            e.Property(i => i.Tamanho).IsRequired().HasMaxLength(30);
            e.Ignore(i => i.TotalCentavos);
            e.HasOne<Produto>().WithMany().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Lancamento>(e =>
        {
            e.Property(l => l.Descricao).IsRequired().HasMaxLength(200);
            e.Property(l => l.Categoria).HasMaxLength(60);
            e.Property(l => l.Contraparte).HasMaxLength(120);
            e.Property(l => l.Observacao).HasMaxLength(300);
            e.Property(l => l.Operador).HasMaxLength(80);
            e.Property(l => l.MotivoCancelamento).HasMaxLength(200);
            e.Property(l => l.CanceladoPor).HasMaxLength(80);
            e.HasIndex(l => l.CorrigeLancamentoId);
            e.HasIndex(l => l.Vencimento);
            e.HasIndex(l => l.PagoEm);
            e.HasIndex(l => l.GrupoId);
            e.HasIndex(l => l.CompraId);
        });

        modelBuilder.Entity<AjusteTituloPrazo>(e =>
        {
            e.Property(a => a.Motivo).IsRequired().HasMaxLength(200);
            e.Property(a => a.Operador).HasMaxLength(80);
            e.HasOne<Venda>().WithMany().HasForeignKey(a => a.VendaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.VendaId);
        });

        modelBuilder.Entity<CategoriaFinanceira>(e =>
        {
            e.Property(c => c.Nome).IsRequired().HasMaxLength(60);
            e.HasIndex(c => new { c.Tipo, c.Nome }).IsUnique();
        });

        modelBuilder.Entity<TurnoCaixa>(e =>
        {
            e.Property(t => t.Operador).HasMaxLength(80);
            e.Ignore(t => t.Aberto);
            e.HasIndex(t => t.DataAbertura);
        });

        modelBuilder.Entity<MovimentoCaixa>(e =>
        {
            e.Property(m => m.Descricao).HasMaxLength(200);
            e.HasOne<TurnoCaixa>().WithMany().HasForeignKey(m => m.TurnoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FechamentoCaixa>(e =>
        {
            e.Property(f => f.Operador).HasMaxLength(80);
            e.Property(f => f.Observacao).HasMaxLength(300);
            e.HasOne<TurnoCaixa>().WithMany().HasForeignKey(f => f.TurnoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(f => new { f.TurnoId, f.Versao }).IsUnique();
            e.Ignore(f => f.DiferencaDinheiroCentavos);
            e.Ignore(f => f.DiferencaCartaoCentavos);
            e.Ignore(f => f.DiferencaPixCentavos);
            e.Ignore(f => f.DiferencaTotalCentavos);
        });

        // Toda entidade sincronizável tem um Guid único (o id do documento remoto). Produto já tinha o índice acima.
        foreach (var tipo in modelBuilder.Model.GetEntityTypes().Select(t => t.ClrType)
                     .Where(t => typeof(IEntidadeSincronizavel).IsAssignableFrom(t) && t != typeof(Produto)).ToList())
            modelBuilder.Entity(tipo).HasIndex(nameof(IEntidadeSincronizavel.Guid)).IsUnique();
    }

    // ---- AtualizadoEm: mantido em UM lugar só, para nenhum serviço esquecer de atualizar ----

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CarimbarProdutosAlterados();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CarimbarProdutosAlterados();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Antes de gravar: todo produto alterado, e todo produto cujo tamanho/estoque foi alterado, recebe o horário
    /// de agora em <see cref="Produto.AtualizadoEm"/>. Cobre cadastro, edição, ajuste de estoque, venda e cancelamento.
    /// </summary>
    private void CarimbarProdutosAlterados()
    {
        var agora = DateTime.UtcNow;

        // Demais entidades sincronizáveis (herdam EntidadeSincronizavel): carimba quando algo além do carimbo mudou.
        // O Pedido fica de fora: o PedidoService já mantém o AtualizadoEm dele.
        foreach (var entrada in ChangeTracker.Entries<EntidadeSincronizavel>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            if (entrada.State == EntityState.Added
                || entrada.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(EntidadeSincronizavel.AtualizadoEm)))
                entrada.Entity.AtualizadoEm = agora;
        }

        foreach (var entrada in ChangeTracker.Entries<Produto>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            // Só carimba se alguma coisa além do próprio carimbo mudou (ou se é novo).
            if (entrada.State == EntityState.Added
                || entrada.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(Produto.AtualizadoEm)))
                entrada.Entity.AtualizadoEm = agora;
        }

        var idsComTamanhoAlterado = ChangeTracker.Entries<ProdutoTamanho>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.ProdutoId)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        foreach (var id in idsComTamanhoAlterado)
        {
            var rastreado = ChangeTracker.Entries<Produto>().FirstOrDefault(e => e.Entity.Id == id);
            if (rastreado is not null)
            {
                rastreado.Entity.AtualizadoEm = agora;
                continue;
            }
            // Produto que não está carregado neste contexto: atualiza só a coluna do carimbo, sem ler o produto.
            var esboco = new Produto { Id = id, AtualizadoEm = agora };
            Attach(esboco);
            Entry(esboco).Property(p => p.AtualizadoEm).IsModified = true;
        }
    }
}
