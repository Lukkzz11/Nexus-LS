using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Sincronizacao;

/// <summary>
/// Lê os produtos publicáveis do banco e guarda o controle da sincronização (o que já foi enviado e o log).
/// Só mexe nas tabelas de controle; nunca altera produto, estoque, venda ou qualquer dado comercial.
/// </summary>
public class SincronizacaoRepositorio
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public SincronizacaoRepositorio(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Todos os produtos (ativos e desativados) já no formato de publicação, num retrato único do banco.</summary>
    public async Task<IReadOnlyList<ProdutoPublicavel>> ObterPublicaveisAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Tamanhos).OrderBy(p => p.Id).ToListAsync();
        return produtos.Select(RegrasSincronizacao.Montar).ToList();
    }

    /// <summary>O que já foi enviado: id global do produto -> hash e se está publicado como ativo.</summary>
    public async Task<IReadOnlyDictionary<Guid, EstadoEnviado>> ObterEstadoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var linhas = await db.SincronizacaoProdutos.AsNoTracking().ToListAsync();
        return linhas.ToDictionary(l => l.ProdutoGuid, l => new EstadoEnviado(l.HashEnviado, l.AtivoNoRemoto));
    }

    /// <summary>O plano completo (sem enviar nada): o que é novo, alterado, desativado, inválido e inalterado.</summary>
    public async Task<PlanoSincronizacao> PlanejarAsync() =>
        RegrasSincronizacao.Planejar(await ObterPublicaveisAsync(), await ObterEstadoAsync());

    /// <summary>
    /// Anota que estes itens foram CONFIRMADOS pelo servidor (chame só depois da confirmação). Se já havia registro
    /// do produto, ele é atualizado; nunca cria duas linhas para o mesmo produto, então repetir é seguro.
    /// </summary>
    public async Task RegistrarEnviadosAsync(IEnumerable<ItemPlano> itens)
    {
        var lista = itens.ToList();
        if (lista.Count == 0) return;

        await using var db = await _fabrica.CreateDbContextAsync();
        var guids = lista.Select(i => i.Produto.Guid).ToList();
        var existentes = await db.SincronizacaoProdutos.Where(s => guids.Contains(s.ProdutoGuid))
            .ToDictionaryAsync(s => s.ProdutoGuid);

        var agora = _agora();
        foreach (var item in lista)
        {
            if (!existentes.TryGetValue(item.Produto.Guid, out var linha))
            {
                linha = new SincronizacaoProduto { ProdutoGuid = item.Produto.Guid };
                db.SincronizacaoProdutos.Add(linha);
            }
            linha.HashEnviado = item.Hash;
            linha.AtivoNoRemoto = item.Produto.Ativo;
            linha.EnviadoEm = agora;
        }
        await db.SaveChangesAsync(); // uma transação só: ou anota todos, ou nenhum
    }

    public async Task<SincronizacaoLog> GravarLogAsync(SincronizacaoLog log)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        db.SincronizacaoLogs.Add(log);
        await db.SaveChangesAsync();
        return log;
    }

    /// <summary>Os últimos registros de sincronização, do mais recente para o mais antigo.</summary>
    public async Task<IReadOnlyList<SincronizacaoLog>> UltimosLogsAsync(int quantidade = 20)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.SincronizacaoLogs.AsNoTracking()
            .OrderByDescending(l => l.IniciadaEm).ThenByDescending(l => l.Id)
            .Take(quantidade).ToListAsync();
    }
}
