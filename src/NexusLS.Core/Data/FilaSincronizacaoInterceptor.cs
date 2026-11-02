using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexusLS.Core.Models;

namespace NexusLS.Core.Data;

/// <summary>
/// Quando qualquer parte do programa salva uma entidade sincronizável (produto, venda, cliente, lançamento...), anota-a na
/// fila dentro da mesma transação do SaveChanges. Assim nenhuma alteração escapa da fila, sem mexer em cada serviço.
/// Alterar uma parte (tamanho do produto, item da venda, evento do pedido) anota a entidade dona.
/// </summary>
public sealed class FilaSincronizacaoInterceptor : SaveChangesInterceptor
{
    private static readonly EntityState[] Alterados = { EntityState.Added, EntityState.Modified, EntityState.Deleted };

    /// <summary>Partes que pertencem a outra entidade: tipo da parte, tipo da dona, chave da dona e busca do Guid da dona.</summary>
    private sealed record Parte(Type Tipo, Type Dona, string NomeDona, Func<object, int> IdDaDona, Func<DbContext, List<int>, List<Guid>> GuidsDasDonas);

    private static readonly Parte[] Partes =
    {
        new(typeof(ProdutoTamanho), typeof(Produto), NomesEntidade.Produto, o => ((ProdutoTamanho)o).ProdutoId,
            (db, ids) => db.Set<Produto>().AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Guid).ToList()),
        new(typeof(ItemVenda), typeof(Venda), NomesEntidade.Venda, o => ((ItemVenda)o).VendaId,
            (db, ids) => db.Set<Venda>().AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => v.Guid).ToList()),
        new(typeof(ItemCompra), typeof(Compra), NomesEntidade.Compra, o => ((ItemCompra)o).CompraId,
            (db, ids) => db.Set<Compra>().AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.Guid).ToList()),
        new(typeof(ItemPedido), typeof(Pedido), NomesEntidade.Pedido, o => ((ItemPedido)o).PedidoId,
            (db, ids) => db.Set<Pedido>().AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Guid).ToList()),
        new(typeof(OrdemProducaoGrade), typeof(OrdemProducao), NomesEntidade.OrdemProducao, o => ((OrdemProducaoGrade)o).OrdemProducaoId,
            (db, ids) => db.Set<OrdemProducao>().AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Guid).ToList()),
        new(typeof(EventoPedido), typeof(Pedido), NomesEntidade.Pedido, o => ((EventoPedido)o).PedidoId,
            (db, ids) => db.Set<Pedido>().AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Guid).ToList()),
    };

    private readonly Func<string?> _usuarioUid;
    private readonly Func<DateTime> _agora;

    public FilaSincronizacaoInterceptor(Func<string?>? usuarioUid = null, Func<DateTime>? agora = null)
    {
        _usuarioUid = usuarioUid ?? (() => null);
        _agora = agora ?? (() => DateTime.Now);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } db) Enfileirar(db);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is { } db) Enfileirar(db);
        return ValueTask.FromResult(result);
    }

    private void Enfileirar(DbContext db)
    {
        var mudou = new HashSet<(string Entidade, Guid Guid)>();
        var entradas = db.ChangeTracker.Entries().Where(e => Alterados.Contains(e.State)).ToList();

        // 1) A própria entidade sincronizável.
        foreach (var e in entradas)
            if (e.Entity is IEntidadeSincronizavel s && s.Guid != Guid.Empty)
                mudou.Add((NomesEntidade.De(e.Entity.GetType()), s.Guid));

        // 2) Partes: anota a dona (a que já está no contexto, ou procura o Guid no banco).
        foreach (var parte in Partes)
        {
            var idsDasDonas = entradas.Where(e => parte.Tipo.IsInstanceOfType(e.Entity))
                .Select(e => parte.IdDaDona(e.Entity)).Where(id => id > 0).Distinct().ToList();
            if (idsDasDonas.Count == 0) continue;

            var faltam = new List<int>();
            foreach (var id in idsDasDonas)
            {
                var rastreada = db.ChangeTracker.Entries()
                    .FirstOrDefault(e => e.Entity.GetType() == parte.Dona && (int)e.Property("Id").CurrentValue! == id);
                if (rastreada?.Entity is IEntidadeSincronizavel dona) mudou.Add((parte.NomeDona, dona.Guid));
                else faltam.Add(id);
            }
            if (faltam.Count > 0)
                foreach (var g in parte.GuidsDasDonas(db, faltam)) mudou.Add((parte.NomeDona, g));
        }

        mudou.RemoveWhere(m => m.Guid == Guid.Empty);
        if (mudou.Count == 0) return;

        var agora = _agora();
        var uid = _usuarioUid();
        var guids = mudou.Select(m => m.Guid).Distinct().ToList();
        var existentes = db.Set<ItemFilaSincronizacao>().Where(f => guids.Contains(f.EntidadeGuid)).ToList()
            .ToDictionary(f => (f.Entidade, f.EntidadeGuid));

        foreach (var (entidade, guid) in mudou)
        {
            if (existentes.TryGetValue((entidade, guid), out var item))
            {
                item.Versao++;
                item.Estado = EstadoFila.Pendente;
                item.Tentativas = 0;
                item.ProximaTentativa = null;
                item.UltimoErro = null;
                item.UsuarioUid = uid ?? item.UsuarioUid;
                item.AtualizadoEm = agora;
            }
            else
            {
                db.Set<ItemFilaSincronizacao>().Add(new ItemFilaSincronizacao
                {
                    Entidade = entidade, EntidadeGuid = guid, UsuarioUid = uid, CriadoEm = agora, AtualizadoEm = agora
                });
            }
        }
    }
}
