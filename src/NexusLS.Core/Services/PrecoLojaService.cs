using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Sincronizacao.Nuvem;

namespace NexusLS.Core.Services;

public sealed record ResultadoPrecosLoja(int Aplicados, int Removidos, int Ignorados);

/// <summary>
/// Preços por loja recebidos da nuvem. Ficam na tabela local PrecosLoja: o preço do cadastro (que sincroniza com o ERP Life)
/// não é alterado. O PDV usa o preço daqui quando existe (<see cref="Carrinho.PrecoDaLoja"/> e a venda).
/// </summary>
public sealed class PrecoLojaService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agoraUtc;
    private volatile IReadOnlyDictionary<Guid, PrecoDaLoja> _cache = new Dictionary<Guid, PrecoDaLoja>();

    public PrecoLojaService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agoraUtc = null)
    {
        _fabrica = fabrica;
        _agoraUtc = agoraUtc ?? (() => DateTime.UtcNow);
    }

    /// <summary>Resposta imediata (sem banco) para o carrinho do PDV: o último mapa carregado.</summary>
    public PrecoDaLoja? PrecoDe(Guid produtoGuid) => _cache.TryGetValue(produtoGuid, out var p) ? p : null;

    public async Task RecarregarAsync(CancellationToken ct = default)
    {
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        var todos = await db.PrecosLoja.AsNoTracking().ToListAsync(ct);
        _cache = todos.ToDictionary(x => x.ProdutoGuid, x => new PrecoDaLoja(x.PrecoVarejoCentavos, x.PrecoAtacadoCentavos));
    }

    public async Task<List<PrecoLoja>> ListarAsync(CancellationToken ct = default)
    {
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        return await db.PrecosLoja.AsNoTracking().OrderBy(x => x.ProdutoGuid).ToListAsync(ct);
    }

    /// <summary>
    /// Aplica o que veio da nuvem: grava/atualiza o preço de cada produto e REMOVE o preço local dos produtos de
    /// <paramref name="consultados"/> que não têm mais preço na nuvem (volta ao preço do cadastro). Preço inválido (zero ou
    /// negativo) ou id que não é Guid é ignorado. Tudo numa transação: se falhar, nada muda.
    /// </summary>
    public async Task<ResultadoPrecosLoja> SubstituirAsync(IEnumerable<PrecoLojaNuvem> daNuvem, IEnumerable<Guid> consultados, CancellationToken ct = default)
    {
        var validos = new Dictionary<Guid, PrecoLojaNuvem>();
        var ignorados = 0;
        foreach (var n in daNuvem)
        {
            if (!Guid.TryParse(n.ProdutoGuid, out var guid) || n.PrecoVarejoCentavos <= 0 || n.PrecoAtacadoCentavos is < 0) { ignorados++; continue; }
            validos[guid] = n;
        }

        await using var db = await _fabrica.CreateDbContextAsync(ct);
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var existentes = await db.PrecosLoja.ToDictionaryAsync(x => x.ProdutoGuid, ct);
        var agora = _agoraUtc();
        var aplicados = 0;
        foreach (var (guid, n) in validos)
        {
            var atacado = n.PrecoAtacadoCentavos ?? 0;
            if (existentes.TryGetValue(guid, out var atual))
            {
                if (atual.PrecoVarejoCentavos == n.PrecoVarejoCentavos && atual.PrecoAtacadoCentavos == atacado) continue; // já é este
                atual.PrecoVarejoCentavos = n.PrecoVarejoCentavos;
                atual.PrecoAtacadoCentavos = atacado;
                atual.DefinidoEmUtc = n.AtualizadoEmUtc;
                atual.RecebidoEmUtc = agora;
            }
            else
            {
                db.PrecosLoja.Add(new PrecoLoja
                {
                    ProdutoGuid = guid, PrecoVarejoCentavos = n.PrecoVarejoCentavos, PrecoAtacadoCentavos = atacado,
                    DefinidoEmUtc = n.AtualizadoEmUtc, RecebidoEmUtc = agora
                });
            }
            aplicados++;
        }
        var removidos = 0;
        foreach (var guid in consultados.Distinct())
            if (!validos.ContainsKey(guid) && existentes.TryGetValue(guid, out var sobra)) { db.PrecosLoja.Remove(sobra); removidos++; }

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        await RecarregarAsync(ct);
        return new ResultadoPrecosLoja(aplicados, removidos, ignorados);
    }
}
