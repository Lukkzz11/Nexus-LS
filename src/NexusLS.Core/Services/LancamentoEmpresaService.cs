using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>O que o usuário preenche ao lançar uma entrada ou saída da empresa.</summary>
public sealed record DadosLancamentoEmpresa(DateTime Data, TipoMovimentoEmpresa Tipo, string Categoria, string Descricao,
    long ValorCentavos, FormaPagamento? Forma = null, string? Observacao = null, string? Operador = null);

/// <summary>Filtros da lista. Datas inclusas nas duas pontas; qualquer filtro nulo é ignorado.</summary>
public sealed record FiltroLancamentoEmpresa(DateTime? De = null, DateTime? Ate = null, TipoMovimentoEmpresa? Tipo = null,
    string? Categoria = null, bool? ComAnexo = null, string? Texto = null);

/// <summary>
/// Entradas e saídas da empresa (com anexo opcional). Registro só local. Não confundir com o <see cref="FinanceiroService"/>
/// (contas a pagar e a receber).
/// </summary>
public class LancamentoEmpresaService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly AnexoService? _anexos;

    /// <param name="anexos">Necessário só para apagar os arquivos ao excluir um lançamento com anexos.</param>
    public LancamentoEmpresaService(IDbContextFactory<PdvDbContext> fabrica, AnexoService? anexos = null)
    {
        _fabrica = fabrica;
        _anexos = anexos;
    }

    public async Task<LancamentoEmpresa> CriarAsync(DadosLancamentoEmpresa dados, DateTime? agora = null)
    {
        var d = Validar(dados, agora ?? DateTime.Now);
        var momento = agora ?? DateTime.Now;
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var l = new LancamentoEmpresa { CriadoEm = momento };
        Aplicar(l, d, momento);
        db.LancamentosEmpresa.Add(l);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return l;
    }

    public async Task<LancamentoEmpresa> EditarAsync(int id, DadosLancamentoEmpresa dados, DateTime? agora = null)
    {
        var momento = agora ?? DateTime.Now;
        var d = Validar(dados, momento);
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var l = await db.LancamentosEmpresa.Include(x => x.Anexos).FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false)
            ?? throw new RegraDeNegocioException("Lançamento não encontrado.");
        Aplicar(l, d, momento);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return l;
    }

    /// <summary>
    /// Exclui o lançamento. Se ele tem anexos, só exclui com <paramref name="removerAnexos"/> = true (o usuário confirmou) e,
    /// então, apaga também os arquivos. Sem a confirmação nada é apagado.
    /// </summary>
    public async Task ExcluirAsync(int id, bool removerAnexos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var l = await db.LancamentosEmpresa.Include(x => x.Anexos).FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false)
            ?? throw new RegraDeNegocioException("Lançamento não encontrado.");
        if (l.Anexos.Count > 0 && !removerAnexos)
            throw new RegraDeNegocioException($"Este lançamento tem {l.Anexos.Count} anexo(s). Confirme para apagar o lançamento e os arquivos.");
        if (l.Anexos.Count > 0 && _anexos is null)
            throw new RegraDeNegocioException("Não é possível apagar os arquivos anexos agora.");

        var anexos = l.Anexos.ToList();
        db.AnexosLancamento.RemoveRange(anexos);
        db.LancamentosEmpresa.Remove(l);
        await db.SaveChangesAsync().ConfigureAwait(false);

        foreach (var a in anexos)
        {
            try
            {
                var caminho = _anexos!.CaminhoCompleto(a.CaminhoRelativo);
                if (File.Exists(caminho)) File.Delete(caminho);
            }
            catch (Exception ex) when (ex is IOException or RegraDeNegocioException or UnauthorizedAccessException) { /* sobra um arquivo solto: não desfaz a exclusão */ }
        }
    }

    public async Task<LancamentoEmpresa?> ObterAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        return await db.LancamentosEmpresa.AsNoTracking().Include(x => x.Anexos).FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);
    }

    /// <summary>Mais recentes primeiro, com os anexos carregados.</summary>
    public async Task<List<LancamentoEmpresa>> ListarAsync(FiltroLancamentoEmpresa? filtro = null)
    {
        filtro ??= new FiltroLancamentoEmpresa();
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        IQueryable<LancamentoEmpresa> q = db.LancamentosEmpresa.AsNoTracking().Include(x => x.Anexos);
        if (filtro.De is DateTime de) { var inicio = de.Date; q = q.Where(l => l.Data >= inicio); }
        if (filtro.Ate is DateTime ate) { var fim = ate.Date; q = q.Where(l => l.Data <= fim); }
        if (filtro.Tipo is TipoMovimentoEmpresa tipo) q = q.Where(l => l.Tipo == tipo);
        if (filtro.ComAnexo is bool com) q = com ? q.Where(l => l.Anexos.Any()) : q.Where(l => !l.Anexos.Any());
        var lista = await q.OrderByDescending(l => l.Data).ThenByDescending(l => l.Id).ToListAsync().ConfigureAwait(false);

        // Categoria e texto sem diferenciar maiúsculas e acentos: feito em memória (poucos registros).
        if (!string.IsNullOrWhiteSpace(filtro.Categoria))
        {
            var c = ResumoVendas.Normalizar(filtro.Categoria);
            lista = lista.Where(l => ResumoVendas.Normalizar(l.Categoria) == c).ToList();
        }
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var t = ResumoVendas.Normalizar(filtro.Texto);
            lista = lista.Where(l => ResumoVendas.Normalizar(l.Descricao).Contains(t) ||
                                     ResumoVendas.Normalizar(l.Categoria).Contains(t) ||
                                     ResumoVendas.Normalizar(l.Observacao).Contains(t)).ToList();
        }
        return lista;
    }

    /// <summary>Categorias já usadas (ordem alfabética), para sugerir no campo.</summary>
    public async Task<List<string>> CategoriasAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var lista = await db.LancamentosEmpresa.AsNoTracking().Select(l => l.Categoria).Distinct().ToListAsync().ConfigureAwait(false);
        return lista.OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Soma de entradas, saídas e saldo de uma lista.</summary>
    public static (long Entradas, long Saidas, long Saldo) Totais(IEnumerable<LancamentoEmpresa> lancamentos)
    {
        var lista = lancamentos as IReadOnlyCollection<LancamentoEmpresa> ?? lancamentos.ToList();
        var e = lista.Where(l => l.Tipo == TipoMovimentoEmpresa.Entrada).Sum(l => l.ValorCentavos);
        var s = lista.Where(l => l.Tipo == TipoMovimentoEmpresa.Saida).Sum(l => l.ValorCentavos);
        return (e, s, e - s);
    }

    private static DadosLancamentoEmpresa Validar(DadosLancamentoEmpresa d, DateTime agora)
    {
        if (!Enum.IsDefined(d.Tipo)) throw new RegraDeNegocioException("Escolha se é entrada ou saída.");
        var categoria = (d.Categoria ?? "").Trim();
        if (categoria.Length == 0) throw new RegraDeNegocioException("Informe a categoria.");
        if (categoria.Length > 60) throw new RegraDeNegocioException("A categoria pode ter no máximo 60 letras.");
        var descricao = (d.Descricao ?? "").Trim();
        if (descricao.Length < 3) throw new RegraDeNegocioException("Descreva o lançamento (mínimo 3 letras).");
        if (descricao.Length > 200) throw new RegraDeNegocioException("A descrição pode ter no máximo 200 letras.");
        if (d.ValorCentavos <= 0) throw new RegraDeNegocioException("O valor precisa ser maior que zero.");
        if (d.ValorCentavos > 100_000_000_00L) throw new RegraDeNegocioException("Valor alto demais: confira.");
        if (d.Data.Date < new DateTime(2000, 1, 1)) throw new RegraDeNegocioException("Data inválida.");
        if (d.Data.Date > agora.Date.AddDays(1)) throw new RegraDeNegocioException("A data não pode estar no futuro.");
        if (d.Forma is FormaPagamento f && (!Enum.IsDefined(f) || f == FormaPagamento.ValeTroca))
            throw new RegraDeNegocioException("Forma de pagamento inválida para um lançamento.");
        var obs = string.IsNullOrWhiteSpace(d.Observacao) ? null : d.Observacao.Trim();
        if (obs is { Length: > 500 }) throw new RegraDeNegocioException("A observação pode ter no máximo 500 letras.");
        var op = string.IsNullOrWhiteSpace(d.Operador) ? null : d.Operador.Trim();
        if (op is { Length: > 80 }) op = op[..80];
        return d with { Categoria = categoria, Descricao = descricao, Observacao = obs, Operador = op };
    }

    private static void Aplicar(LancamentoEmpresa l, DadosLancamentoEmpresa d, DateTime agora)
    {
        l.Data = d.Data.Date;
        l.Tipo = d.Tipo;
        l.Categoria = d.Categoria;
        l.Descricao = d.Descricao;
        l.ValorCentavos = d.ValorCentavos;
        l.Forma = d.Forma;
        l.Observacao = d.Observacao;
        l.Operador ??= d.Operador ?? OperadorAtual.Nome;
        l.AlteradoEm = agora;
    }
}
