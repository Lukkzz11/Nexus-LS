using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Quanto foi removido numa passada de retenção (só controle de sincronização/log).</summary>
public record ResultadoRetencao(int FilaApagada, int LogsApagados);

/// <summary>
/// Limpeza periódica do que NÃO é dado comercial: itens já confirmados da fila de sincronização e logs antigos.
/// Nada de venda, compra, lançamento, cliente, produto ou caixa é apagado aqui — essas regras continuam valendo
/// (cancelamento com motivo, fechamento imutável etc.).
/// </summary>
public class RetencaoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public RetencaoService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>
    /// Apaga somente itens da fila em <see cref="EstadoFila.Enviado"/> cujo <c>AtualizadoEm</c> é anterior ao corte.
    /// Pendente, Enviando e Falhou NUNCA são apagados: são dados ainda não confirmados pelo servidor.
    /// Dos logs de sincronização ficam os <paramref name="maxLogs"/> mais recentes (por <c>IniciadaEm</c>).
    /// Tudo numa transação única.
    /// </summary>
    public async Task<ResultadoRetencao> AplicarAsync(int diasFila = 90, int maxLogs = 1000, DateTime? agora = null)
    {
        if (diasFila < 0) throw new ArgumentOutOfRangeException(nameof(diasFila), "Os dias de retenção não podem ser negativos.");
        if (maxLogs < 0) throw new ArgumentOutOfRangeException(nameof(maxLogs), "A quantidade de logs a manter não pode ser negativa.");

        var corte = (agora ?? DateTime.Now).AddDays(-diasFila);

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        var filaApagada = await db.FilaSincronizacao
            .Where(i => i.Estado == EstadoFila.Enviado && i.AtualizadoEm < corte)
            .ExecuteDeleteAsync();

        // Descobre os ids que ficam (os maxLogs mais recentes) e apaga o resto pelo id, sem depender de empates de data.
        var idsMantidos = await db.SincronizacaoLogs.AsNoTracking()
            .OrderByDescending(l => l.IniciadaEm).ThenByDescending(l => l.Id)
            .Take(maxLogs)
            .Select(l => l.Id)
            .ToListAsync();

        var logsApagados = await db.SincronizacaoLogs
            .Where(l => !idsMantidos.Contains(l.Id))
            .ExecuteDeleteAsync();

        await transacao.CommitAsync();
        return new ResultadoRetencao(filaApagada, logsApagados);
    }
}
