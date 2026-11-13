using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;

namespace NexusLS.Core.Pagamentos;

/// <summary>Registro de uma cobrança Pix no SQLite. Fica só neste computador (não sincroniza): guarda a cópia local do que o gateway informou.</summary>
public class CobrancaPixRegistro
{
    public int Id { get; set; }
    public string ChaveVenda { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string IdExterno { get; set; } = "";
    public long ValorCentavos { get; set; }
    public string CopiaECola { get; set; } = "";
    public string? QrCodeBase64 { get; set; }
    public StatusPix Status { get; set; }
    public DateTime CriadaEm { get; set; }
    public DateTime AtualizadaEm { get; set; }
    public DateTime? ExpiraEm { get; set; }
    /// <summary>Venda que esta cobrança pagou (preenchido quando a venda é finalizada).</summary>
    public int? VendaId { get; set; }
}

public class RepositorioPixSqlite : IRepositorioPix
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public RepositorioPixSqlite(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<CobrancaPix?> PorChaveVendaAsync(string chaveVenda, CancellationToken ct = default)
    {
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        var r = await db.CobrancasPix.AsNoTracking().FirstOrDefaultAsync(c => c.ChaveVenda == chaveVenda, ct);
        return r is null ? null : Para(r);
    }

    public async Task<CobrancaPix?> PorIdExternoAsync(string gateway, string idExterno, CancellationToken ct = default)
    {
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        var r = await db.CobrancasPix.AsNoTracking().FirstOrDefaultAsync(c => c.Gateway == gateway && c.IdExterno == idExterno, ct);
        return r is null ? null : Para(r);
    }

    public async Task SalvarAsync(CobrancaPix cobranca, CancellationToken ct = default)
    {
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        var r = await db.CobrancasPix.FirstOrDefaultAsync(c => c.ChaveVenda == cobranca.ChaveVenda, ct);
        var agora = _agora();
        if (r is null)
        {
            r = new CobrancaPixRegistro { ChaveVenda = cobranca.ChaveVenda, CriadaEm = agora };
            db.CobrancasPix.Add(r);
        }
        r.Gateway = cobranca.Gateway;
        r.IdExterno = cobranca.IdExterno;
        r.ValorCentavos = cobranca.ValorCentavos;
        r.CopiaECola = cobranca.CopiaECola;
        r.QrCodeBase64 = cobranca.QrCodeBase64;
        r.Status = cobranca.Status;
        r.ExpiraEm = cobranca.ExpiraEm;
        r.AtualizadaEm = agora;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Liga a cobrança aprovada à venda finalizada (para o histórico e a conciliação).</summary>
    public async Task VincularVendaAsync(string chaveVenda, int vendaId, CancellationToken ct = default)
    {
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        var r = await db.CobrancasPix.FirstOrDefaultAsync(c => c.ChaveVenda == chaveVenda, ct);
        if (r is null) return;
        r.VendaId = vendaId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Histórico para a tela de configurações, da mais recente para a mais antiga.</summary>
    public async Task<IReadOnlyList<CobrancaPixRegistro>> HistoricoAsync(int limite = 100, CancellationToken ct = default)
    {
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        return await db.CobrancasPix.AsNoTracking().OrderByDescending(c => c.CriadaEm).Take(limite).ToListAsync(ct);
    }

    private static CobrancaPix Para(CobrancaPixRegistro r) =>
        new(r.Gateway, r.IdExterno, r.ChaveVenda, r.ValorCentavos, r.CopiaECola, r.QrCodeBase64, r.Status, r.ExpiraEm);
}
