using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexusLS.Core.Models;

namespace NexusLS.Core.Data;

/// <summary>
/// Quando qualquer parte do programa salva um produto (cadastro, edição, ajuste de estoque, venda, compra...), anota o
/// produto na fila de sincronização dentro da mesma transação do SaveChanges. Assim nenhuma alteração escapa da fila,
/// sem precisar mexer em cada serviço.
/// </summary>
public sealed class FilaSincronizacaoInterceptor : SaveChangesInterceptor
{
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
        var alterados = new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted };
        var guids = new HashSet<Guid>();
        var idsSemProduto = new HashSet<int>();

        foreach (var e in db.ChangeTracker.Entries<Produto>().Where(e => alterados.Contains(e.State)))
            guids.Add(e.Entity.Guid);

        foreach (var e in db.ChangeTracker.Entries<ProdutoTamanho>().Where(e => alterados.Contains(e.State)))
        {
            if (e.Entity.ProdutoId <= 0) continue; // produto novo: já entrou pelo laço acima
            var rastreado = db.ChangeTracker.Entries<Produto>().FirstOrDefault(p => p.Entity.Id == e.Entity.ProdutoId);
            if (rastreado is not null) guids.Add(rastreado.Entity.Guid);
            else idsSemProduto.Add(e.Entity.ProdutoId);
        }

        if (idsSemProduto.Count > 0)
            foreach (var g in db.Set<Produto>().AsNoTracking().Where(p => idsSemProduto.Contains(p.Id)).Select(p => p.Guid).ToList())
                guids.Add(g);

        guids.Remove(Guid.Empty);
        if (guids.Count == 0) return;

        var agora = _agora();
        var uid = _usuarioUid();
        var lista = guids.ToList();
        var existentes = db.Set<ItemFilaSincronizacao>().Where(f => lista.Contains(f.ProdutoGuid))
            .ToDictionary(f => f.ProdutoGuid);

        foreach (var guid in lista)
        {
            if (existentes.TryGetValue(guid, out var item))
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
                    ProdutoGuid = guid, UsuarioUid = uid, CriadoEm = agora, AtualizadoEm = agora
                });
            }
        }
    }
}
