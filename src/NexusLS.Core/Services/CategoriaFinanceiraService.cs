using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Categorias configuráveis dos lançamentos. Na primeira vez que a lista é pedida, cria as categorias padrão (as mesmas que
/// o programa sempre ofereceu); depois o dono acrescenta, desativa e reativa. Nada é apagado.
/// </summary>
public class CategoriaFinanceiraService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public CategoriaFinanceiraService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Cria as categorias padrão se ainda não existe nenhuma. Seguro repetir.</summary>
    public async Task GarantirPadraoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        if (await db.CategoriasFinanceiras.AnyAsync()) return;
        var agora = _agora();
        db.CategoriasFinanceiras.AddRange(
            RegrasFinanceiras.CategoriasDespesa.Select(n => Nova(TipoLancamento.Despesa, n, agora))
                .Concat(RegrasFinanceiras.CategoriasReceita.Select(n => Nova(TipoLancamento.Receita, n, agora))));
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<CategoriaFinanceira>> ListarAsync(TipoLancamento? tipo = null, bool incluirInativas = false)
    {
        await GarantirPadraoAsync();
        await using var db = await _fabrica.CreateDbContextAsync();
        var consulta = db.CategoriasFinanceiras.AsNoTracking().AsQueryable();
        if (tipo is { } t) consulta = consulta.Where(c => c.Tipo == t);
        if (!incluirInativas) consulta = consulta.Where(c => c.Ativa);
        return (await consulta.ToListAsync()).OrderBy(c => c.Tipo).ThenBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Nomes das categorias ativas de um tipo, prontos para a lista de escolha.</summary>
    public async Task<IReadOnlyList<string>> NomesAtivosAsync(TipoLancamento tipo) =>
        (await ListarAsync(tipo)).Select(c => c.Nome).ToList();

    public async Task<CategoriaFinanceira> CriarAsync(TipoLancamento tipo, string nome)
    {
        var limpo = (nome ?? "").Trim();
        if (limpo.Length == 0) throw new RegraDeNegocioException("Informe o nome da categoria.");
        if (limpo.Length > 60) throw new RegraDeNegocioException("O nome da categoria pode ter no máximo 60 caracteres.");
        await GarantirPadraoAsync();
        await using var db = await _fabrica.CreateDbContextAsync();
        var existentes = await db.CategoriasFinanceiras.Where(c => c.Tipo == tipo).ToListAsync();
        var igual = existentes.FirstOrDefault(c => string.Equals(c.Nome, limpo, StringComparison.CurrentCultureIgnoreCase));
        if (igual is not null)
        {
            if (igual.Ativa) throw new RegraDeNegocioException("Já existe uma categoria com esse nome.");
            igual.Ativa = true; // o nome voltou: reativa em vez de duplicar
            await db.SaveChangesAsync();
            return igual;
        }
        var nova = Nova(tipo, limpo, _agora());
        db.CategoriasFinanceiras.Add(nova);
        await db.SaveChangesAsync();
        return nova;
    }

    public async Task DefinirAtivaAsync(int id, bool ativa)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await db.CategoriasFinanceiras.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new RegraDeNegocioException("Categoria não encontrada.");
        c.Ativa = ativa;
        await db.SaveChangesAsync();
    }

    private static CategoriaFinanceira Nova(TipoLancamento tipo, string nome, DateTime agora) =>
        new() { Tipo = tipo, Nome = nome, Ativa = true, CriadaEm = agora };
}
