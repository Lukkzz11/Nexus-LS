using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosCompromisso(string Titulo, DateTime Inicio, DateTime? Fim = null, bool DiaInteiro = false, string? Descricao = null);

/// <summary>Agenda da loja: criar, editar, concluir e remover compromissos e ver o dia ou o mês.</summary>
public class AgendaService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public AgendaService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    private static void Validar(DadosCompromisso d)
    {
        if (string.IsNullOrWhiteSpace(d.Titulo))
            throw new RegraDeNegocioException("Informe o título do compromisso.");
        if (d.Titulo.Trim().Length > 150)
            throw new RegraDeNegocioException("O título pode ter no máximo 150 caracteres.");
        if (d.Fim is { } fim && fim < d.Inicio)
            throw new RegraDeNegocioException("O fim não pode ser antes do início.");
    }

    private static void Aplicar(Compromisso c, DadosCompromisso d)
    {
        c.Titulo = d.Titulo.Trim();
        c.Descricao = string.IsNullOrWhiteSpace(d.Descricao) ? null : d.Descricao.Trim();
        c.DiaInteiro = d.DiaInteiro;
        c.Inicio = d.DiaInteiro ? d.Inicio.Date : d.Inicio;
        c.Fim = d.DiaInteiro ? null : d.Fim;
    }

    public async Task<Compromisso> CriarAsync(DadosCompromisso dados)
    {
        Validar(dados);
        var c = new Compromisso { CriadoEm = _agora() };
        Aplicar(c, dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        db.Compromissos.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public async Task EditarAsync(int id, DadosCompromisso dados)
    {
        Validar(dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await db.Compromissos.FindAsync(id) ?? throw new RegraDeNegocioException("Compromisso não encontrado.");
        Aplicar(c, dados);
        await db.SaveChangesAsync();
    }

    public async Task DefinirGoogleEventoAsync(int id, string? eventoId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await db.Compromissos.FirstOrDefaultAsync(x => x.Id == id) ?? throw new RegraDeNegocioException("Compromisso não encontrado.");
        c.GoogleEventoId = eventoId;
        await db.SaveChangesAsync();
    }

    public async Task DefinirConcluidoAsync(int id, bool concluido)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await db.Compromissos.FindAsync(id) ?? throw new RegraDeNegocioException("Compromisso não encontrado.");
        c.Concluido = concluido;
        await db.SaveChangesAsync();
    }

    public async Task RemoverAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await db.Compromissos.FindAsync(id) ?? throw new RegraDeNegocioException("Compromisso não encontrado.");
        db.Compromissos.Remove(c);
        await db.SaveChangesAsync();
    }

    /// <summary>Os compromissos de um dia: os de dia inteiro primeiro, depois por hora.</summary>
    public async Task<List<Compromisso>> DoDiaAsync(DateTime dia)
    {
        var de = dia.Date;
        await using var db = await _fabrica.CreateDbContextAsync();
        return (await db.Compromissos.AsNoTracking().Where(c => c.Inicio >= de && c.Inicio < de.AddDays(1)).ToListAsync())
            .OrderByDescending(c => c.DiaInteiro).ThenBy(c => c.Inicio).ThenBy(c => c.Id).ToList();
    }

    /// <summary>Os dias do mês que têm compromisso ainda em aberto (para marcar no calendário).</summary>
    public async Task<HashSet<DateTime>> DiasComCompromissoAsync(int ano, int mes)
    {
        var de = new DateTime(ano, mes, 1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var datas = await db.Compromissos.AsNoTracking()
            .Where(c => c.Inicio >= de && c.Inicio < de.AddMonths(1) && !c.Concluido).Select(c => c.Inicio).ToListAsync();
        return datas.Select(d => d.Date).ToHashSet();
    }

    /// <summary>Os próximos compromissos em aberto, a partir de agora (para o Início).</summary>
    public async Task<List<Compromisso>> ProximosAsync(int quantidade = 5)
    {
        var agora = _agora().Date;
        await using var db = await _fabrica.CreateDbContextAsync();
        return (await db.Compromissos.AsNoTracking().Where(c => !c.Concluido && c.Inicio >= agora).ToListAsync())
            .OrderBy(c => c.Inicio).Take(quantidade).ToList();
    }
}
