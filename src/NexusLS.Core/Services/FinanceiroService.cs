using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosLancamento(
    TipoLancamento Tipo,
    string Descricao,
    long ValorCentavos,
    DateTime Vencimento,
    string? Categoria = null,
    string? Contraparte = null,
    string? Observacao = null,
    int Parcelas = 1,
    bool JaPago = false,
    FormaPagamento? Forma = null);

public record FiltroLancamentos(
    DateTime De, DateTime Ate,
    TipoLancamento? Tipo = null,
    SituacaoLancamento? Situacao = null,
    string? Texto = null);

public record ResumoCategoriaFinanceira(string Categoria, long TotalCentavos);

/// <summary>Os números do período: vendas do PDV + lançamentos. Tudo em centavos.</summary>
public record ResumoFinanceiro(
    DateTime De, DateTime Ate,
    long VendasCentavos, long CustoMercadoriasCentavos,
    long ReceitasRecebidasCentavos, long DespesasPagasCentavos,
    long AReceberCentavos, long APagarCentavos,
    int QuantidadeAtrasados, long AtrasadosCentavos,
    IReadOnlyList<ResumoCategoriaFinanceira> DespesasPorCategoria)
{
    public long LucroBrutoCentavos => VendasCentavos - CustoMercadoriasCentavos;

    /// <summary>Lucro bruto das vendas + outras receitas recebidas − despesas pagas no período.</summary>
    public long ResultadoCentavos => LucroBrutoCentavos + ReceitasRecebidasCentavos - DespesasPagasCentavos;
}

public static class RegrasFinanceiras
{
    public static readonly IReadOnlyList<string> CategoriasDespesa = new[]
    {
        "Aluguel", "Energia", "Água", "Internet e telefone", "Salários", "Fornecedores", "Impostos e taxas",
        "Marketing", "Manutenção", "Frete", "Material de uso", "Outros"
    };

    public static readonly IReadOnlyList<string> CategoriasReceita = new[] { "Serviços", "Aluguel recebido", "Outras receitas" };

    public static SituacaoLancamento Situacao(Lancamento l, DateTime hoje) =>
        l.Cancelado ? SituacaoLancamento.Cancelado
        : l.PagoEm is not null ? SituacaoLancamento.Pago
        : l.Vencimento.Date < hoje.Date ? SituacaoLancamento.Atrasado
        : SituacaoLancamento.Aberto;

    /// <summary>Divide o valor em parcelas de centavos inteiros; a última leva a sobra (R$ 100,00 em 3 = 33,33 + 33,33 + 33,34).</summary>
    public static long[] DividirEmParcelas(long totalCentavos, int parcelas)
    {
        var baseParcela = totalCentavos / parcelas;
        var valores = Enumerable.Repeat(baseParcela, parcelas).ToArray();
        valores[^1] += totalCentavos - baseParcela * parcelas;
        return valores;
    }
}

/// <summary>
/// Contas a pagar e a receber, mais o resumo do período (DRE simples). Nada é apagado: cancela-se com motivo,
/// e só o que ainda não foi pago pode ser editado ou cancelado (estorne o pagamento antes).
/// </summary>
public class FinanceiroService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public FinanceiroService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<IReadOnlyList<Lancamento>> CriarAsync(DadosLancamento dados, string? operador = null)
    {
        var criados = Montar(dados, _agora(), operador);
        await using var db = await _fabrica.CreateDbContextAsync();
        db.Lancamentos.AddRange(criados);
        await db.SaveChangesAsync();
        return criados;
    }

    /// <summary>
    /// Valida e monta os lançamentos (um por parcela) SEM gravar. Outros serviços (ex.: Compras) usam isto para
    /// gravar as contas a pagar na mesma transação do que estão fazendo.
    /// </summary>
    public static List<Lancamento> Montar(DadosLancamento dados, DateTime agora, string? operador = null, int? compraId = null)
    {
        Validar(dados.Descricao, dados.ValorCentavos);
        if (dados.Parcelas is < 1 or > 60) throw new RegraDeNegocioException("O número de parcelas deve ficar entre 1 e 60.");
        if (dados.JaPago && dados.Parcelas > 1)
            throw new RegraDeNegocioException("Um lançamento parcelado não pode ser marcado como já pago: pague as parcelas uma a uma.");
        if (dados.ValorCentavos < dados.Parcelas)
            throw new RegraDeNegocioException("O valor é pequeno demais para essa quantidade de parcelas.");
        if (dados.Forma == FormaPagamento.APrazo)
            throw new RegraDeNegocioException("Escolha como foi pago (dinheiro, Pix, cartão...).");

        var valores = RegrasFinanceiras.DividirEmParcelas(dados.ValorCentavos, dados.Parcelas);
        var grupo = dados.Parcelas > 1 ? Guid.NewGuid() : (Guid?)null;
        var criados = new List<Lancamento>();
        for (var i = 0; i < dados.Parcelas; i++)
        {
            criados.Add(new Lancamento
            {
                Tipo = dados.Tipo,
                Descricao = dados.Parcelas > 1 ? $"{dados.Descricao.Trim()} ({i + 1}/{dados.Parcelas})" : dados.Descricao.Trim(),
                Categoria = Limpar(dados.Categoria),
                Contraparte = Limpar(dados.Contraparte),
                Observacao = Limpar(dados.Observacao),
                ValorCentavos = valores[i],
                Vencimento = dados.Vencimento.Date.AddMonths(i),
                PagoEm = dados.JaPago ? agora : null,
                FormaPagamento = dados.JaPago ? dados.Forma ?? Models.FormaPagamento.Dinheiro : null,
                GrupoId = grupo,
                CompraId = compraId,
                Parcela = i + 1,
                TotalParcelas = dados.Parcelas,
                CriadoEm = agora,
                Operador = Limpar(operador) ?? Limpar(OperadorAtual.Nome)
            });
        }
        return criados;
    }

    public async Task EditarAsync(int id, string descricao, long valorCentavos, DateTime vencimento,
        string? categoria, string? contraparte, string? observacao)
    {
        Validar(descricao, valorCentavos);
        await using var db = await _fabrica.CreateDbContextAsync();
        var l = await ObterAsync(db, id);
        if (l.Cancelado) throw new RegraDeNegocioException("Lançamento cancelado não pode ser editado.");
        if (l.PagoEm is not null) throw new RegraDeNegocioException("Lançamento já pago não pode ser editado. Estorne o pagamento primeiro.");

        l.Descricao = descricao.Trim();
        l.ValorCentavos = valorCentavos;
        l.Vencimento = vencimento.Date;
        l.Categoria = Limpar(categoria);
        l.Contraparte = Limpar(contraparte);
        l.Observacao = Limpar(observacao);
        await db.SaveChangesAsync();
    }

    public async Task PagarAsync(int id, FormaPagamento forma, DateTime? quando = null)
    {
        if (forma == FormaPagamento.APrazo) throw new RegraDeNegocioException("Escolha como foi pago (dinheiro, Pix, cartão...).");
        await using var db = await _fabrica.CreateDbContextAsync();
        var l = await ObterAsync(db, id);
        if (l.Cancelado) throw new RegraDeNegocioException("Lançamento cancelado não pode ser pago.");
        if (l.PagoEm is not null) throw new RegraDeNegocioException("Esse lançamento já está pago.");

        var data = quando ?? _agora();
        if (data.Date > _agora().Date) throw new RegraDeNegocioException("A data do pagamento não pode ser no futuro.");
        l.PagoEm = data;
        l.FormaPagamento = forma;
        await db.SaveChangesAsync();
    }

    public async Task EstornarPagamentoAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var l = await ObterAsync(db, id);
        if (l.PagoEm is null) throw new RegraDeNegocioException("Esse lançamento não está pago.");
        l.PagoEm = null;
        l.FormaPagamento = null;
        AtividadeService.Registrar(db, TipoAtividade.PagamentoEstornado,
            $"Estornou o pagamento de \"{l.Descricao}\" ({Dinheiro.Formatar(l.ValorCentavos)})", $"Lançamento #{l.Id}");
        await db.SaveChangesAsync();
    }

    public async Task CancelarAsync(int id, string motivo)
    {
        if ((motivo ?? "").Trim().Length < 3) throw new RegraDeNegocioException("Informe o motivo do cancelamento (pelo menos 3 letras).");
        await using var db = await _fabrica.CreateDbContextAsync();
        var l = await ObterAsync(db, id);
        if (l.Cancelado) throw new RegraDeNegocioException("Esse lançamento já está cancelado.");
        if (l.PagoEm is not null) throw new RegraDeNegocioException("Lançamento pago não pode ser cancelado. Estorne o pagamento primeiro.");
        l.Cancelado = true;
        l.MotivoCancelamento = motivo.Trim();
        l.CanceladoEm = _agora();
        AtividadeService.Registrar(db, TipoAtividade.LancamentoCancelado,
            $"Cancelou \"{l.Descricao}\" ({Dinheiro.Formatar(l.ValorCentavos)}): {motivo.Trim()}", $"Lançamento #{l.Id}", null, l.CanceladoEm);
        await db.SaveChangesAsync();
    }

    /// <summary>Lista pelo vencimento dentro do período, do mais antigo para o mais novo.</summary>
    public async Task<IReadOnlyList<Lancamento>> ListarAsync(FiltroLancamentos filtro)
    {
        var inicio = filtro.De.Date;
        var fim = filtro.Ate.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var consulta = db.Lancamentos.AsNoTracking().Where(l => l.Vencimento >= inicio && l.Vencimento < fim);
        if (filtro.Tipo is { } tipo) consulta = consulta.Where(l => l.Tipo == tipo);
        var lista = await consulta.ToListAsync();

        var hoje = _agora();
        if (filtro.Situacao is { } sit) lista = lista.Where(l => RegrasFinanceiras.Situacao(l, hoje) == sit).ToList();
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var t = filtro.Texto.Trim();
            lista = lista.Where(l =>
                l.Descricao.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
                (l.Contraparte?.Contains(t, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (l.Categoria?.Contains(t, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();
        }
        return lista.OrderBy(l => l.Vencimento).ThenBy(l => l.Id).ToList();
    }

    public async Task<ResumoFinanceiro> ResumoAsync(DateTime de, DateTime ate)
    {
        var inicio = de.Date;
        var fim = ate.Date.AddDays(1);
        var hoje = _agora();

        var vendas = await new RelatorioService(_fabrica).VendasPorPeriodoAsync(de, ate);

        await using var db = await _fabrica.CreateDbContextAsync();
        var todos = await db.Lancamentos.AsNoTracking().Where(l => !l.Cancelado).ToListAsync();

        var pagosNoPeriodo = todos.Where(l => l.PagoEm >= inicio && l.PagoEm < fim).ToList();
        var abertos = todos.Where(l => l.PagoEm is null).ToList();
        var atrasados = abertos.Where(l => l.Vencimento.Date < hoje.Date).ToList();

        // "A pagar / a receber": o que está em aberto e vence até o fim do período (inclui o atrasado de antes).
        var abertosAteOFim = abertos.Where(l => l.Vencimento < fim).ToList();

        var porCategoria = pagosNoPeriodo.Where(l => l.Tipo == TipoLancamento.Despesa)
            .GroupBy(l => l.Categoria ?? "Sem categoria")
            .Select(g => new ResumoCategoriaFinanceira(g.Key, g.Sum(l => l.ValorCentavos)))
            .OrderByDescending(c => c.TotalCentavos).ToList();

        return new ResumoFinanceiro(
            de.Date, ate.Date,
            vendas.TotalCentavos, vendas.CustoTotalCentavos,
            pagosNoPeriodo.Where(l => l.Tipo == TipoLancamento.Receita).Sum(l => l.ValorCentavos),
            pagosNoPeriodo.Where(l => l.Tipo == TipoLancamento.Despesa).Sum(l => l.ValorCentavos),
            abertosAteOFim.Where(l => l.Tipo == TipoLancamento.Receita).Sum(l => l.ValorCentavos),
            abertosAteOFim.Where(l => l.Tipo == TipoLancamento.Despesa).Sum(l => l.ValorCentavos),
            atrasados.Count, atrasados.Sum(l => l.ValorCentavos),
            porCategoria);
    }

    private static async Task<Lancamento> ObterAsync(PdvDbContext db, int id) =>
        await db.Lancamentos.FirstOrDefaultAsync(l => l.Id == id)
        ?? throw new RegraDeNegocioException("Lançamento não encontrado.");

    private static void Validar(string descricao, long valorCentavos)
    {
        if (string.IsNullOrWhiteSpace(descricao)) throw new RegraDeNegocioException("Informe a descrição.");
        if (descricao.Trim().Length > 200) throw new RegraDeNegocioException("A descrição pode ter no máximo 200 caracteres.");
        if (valorCentavos <= 0) throw new RegraDeNegocioException("O valor deve ser maior que zero.");
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
