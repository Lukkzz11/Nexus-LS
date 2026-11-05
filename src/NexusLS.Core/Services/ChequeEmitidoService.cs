using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosChequeEmitido(int LancamentoId, int ContaId, DateTime BomPara, string? Numero = null, string? Banco = null, string? Favorecido = null);

/// <summary>Um cheque emitido com os dados da conta a pagar, prontos para a tela.</summary>
public record ChequeEmitidoComConta(ChequeEmitido Cheque, string Descricao, string ContaNome);

/// <summary>
/// Pagar uma conta a pagar com cheque emitido. Emitir NÃO paga a conta: ela fica em aberto, mas o cheque pendente a coloca
/// no motor como saída "em compensação" (não mexe no saldo). Compensar paga a conta (forma cheque, na conta do cheque, no dia
/// que compensou). Cancelar (com motivo) devolve a conta para "em aberto". Tudo grava junto com a trilha em Atividades.
/// </summary>
public class ChequeEmitidoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public ChequeEmitidoService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Contas a pagar em aberto, sem cheque pendente, que podem receber um cheque (mais antigas primeiro).</summary>
    public async Task<IReadOnlyList<Lancamento>> ContasParaChequeAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var comCheque = db.ChequesEmitidos.Where(c => c.Situacao == SituacaoChequeEmitido.Pendente).Select(c => c.LancamentoId);
        var lista = await db.Lancamentos.AsNoTracking()
            .Where(l => !l.Cancelado && l.PagoEm == null && l.Tipo == TipoLancamento.Despesa && !comCheque.Contains(l.Id)).ToListAsync();
        return lista.OrderBy(l => l.Vencimento).ThenBy(l => l.Id).ToList();
    }

    public async Task<IReadOnlyList<ChequeEmitidoComConta>> ListarAsync(SituacaoChequeEmitido? situacao = null)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var consulta = db.ChequesEmitidos.AsNoTracking();
        if (situacao is SituacaoChequeEmitido s) consulta = consulta.Where(c => c.Situacao == s);
        var cheques = (await consulta.ToListAsync()).OrderBy(c => c.BomPara).ThenBy(c => c.Id).ToList();
        var ids = cheques.Select(c => c.LancamentoId).Distinct().ToList();
        var lancs = await db.Lancamentos.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Descricao);
        var contas = await db.ContasFinanceiras.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome);
        return cheques.Select(c => new ChequeEmitidoComConta(c, lancs.GetValueOrDefault(c.LancamentoId, "?"), contas.GetValueOrDefault(c.ContaId, "?"))).ToList();
    }

    public async Task<ChequeEmitido> EmitirAsync(DadosChequeEmitido dados)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var l = await db.Lancamentos.FirstOrDefaultAsync(x => x.Id == dados.LancamentoId)
                ?? throw new RegraDeNegocioException("Conta a pagar não encontrada.");
        if (l.Cancelado) throw new RegraDeNegocioException("Conta cancelada não pode ser paga.");
        if (l.Tipo != TipoLancamento.Despesa) throw new RegraDeNegocioException("Só conta a pagar é paga com cheque emitido.");
        if (l.PagoEm is not null) throw new RegraDeNegocioException("Essa conta já está paga.");
        if (await db.ChequesEmitidos.AnyAsync(c => c.LancamentoId == l.Id && c.Situacao == SituacaoChequeEmitido.Pendente))
            throw new RegraDeNegocioException("Essa conta já tem um cheque pendente. Cancele o cheque antes de emitir outro.");
        var conta = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == dados.ContaId)
                    ?? throw new RegraDeNegocioException("Conta bancária não encontrada.");
        if (!conta.Ativa) throw new RegraDeNegocioException("A conta escolhida está desativada.");
        if (conta.Tipo != TipoContaFin.Banco) throw new RegraDeNegocioException("Cheque sai de uma conta de banco (não da gaveta nem do cofre).");

        var agora = _agora();
        var c = new ChequeEmitido
        {
            LancamentoId = l.Id,
            ValorCentavos = l.ValorCentavos,
            Numero = Limpar(dados.Numero),
            Banco = Limpar(dados.Banco),
            Favorecido = Limpar(dados.Favorecido) ?? Limpar(l.Contraparte),
            BomPara = dados.BomPara.Date,
            ContaId = conta.Id,
            Operador = Limpar(OperadorAtual.Nome),
            CriadoEm = agora
        };
        db.ChequesEmitidos.Add(c);
        await db.SaveChangesAsync(); // o Id do cheque entra na trilha
        AtividadeService.Registrar(db, TipoAtividade.ChequeEmitido,
            $"Emitiu cheque{(c.Numero is null ? "" : " " + c.Numero)} de {Dinheiro.Formatar(c.ValorCentavos)} para \"{l.Descricao}\" (bom para {c.BomPara:dd/MM/yyyy}, {conta.Nome})",
            $"Cheque emitido #{c.Id}", null, agora);
        await db.SaveChangesAsync();
        return c;
    }

    /// <summary>O banco pagou o cheque: a conta passa a paga (forma cheque) na conta do cheque, no dia informado.</summary>
    public async Task CompensarAsync(int id, DateTime data)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await ObterAsync(db, id);
        if (c.Situacao != SituacaoChequeEmitido.Pendente) throw new RegraDeNegocioException("Esse cheque não está pendente.");
        var agora = _agora();
        if (data.Date > agora.Date) throw new RegraDeNegocioException("A data da compensação não pode ser no futuro.");
        if (data.Date < c.CriadoEm.Date) throw new RegraDeNegocioException("O cheque não pode compensar antes do dia em que foi emitido.");
        var l = await db.Lancamentos.FirstAsync(x => x.Id == c.LancamentoId);
        if (l.Cancelado || l.PagoEm is not null) throw new RegraDeNegocioException("A conta deste cheque mudou: cancele o cheque.");

        c.Situacao = SituacaoChequeEmitido.Compensado;
        c.CompensadoEm = data.Date;
        l.PagoEm = data.Date;
        l.FormaPagamento = FormaPagamento.Cheque;
        l.ContaId = c.ContaId;
        AtividadeService.Registrar(db, TipoAtividade.ChequeEmitidoCompensado,
            $"Cheque emitido de {Dinheiro.Formatar(c.ValorCentavos)} compensou em {data:dd/MM/yyyy}: \"{l.Descricao}\" paga",
            $"Cheque emitido #{c.Id}", null, agora);
        await db.SaveChangesAsync();
    }

    /// <summary>Cheque sustado, perdido ou emitido por engano: a conta volta a ficar em aberto. Só cheque pendente.</summary>
    public async Task CancelarAsync(int id, string motivo)
    {
        var texto = (motivo ?? "").Trim();
        if (texto.Length < 3) throw new RegraDeNegocioException("Informe o motivo (pelo menos 3 letras).");
        if (texto.Length > 200) throw new RegraDeNegocioException("O motivo pode ter no máximo 200 caracteres.");
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await ObterAsync(db, id);
        if (c.Situacao == SituacaoChequeEmitido.Compensado)
            throw new RegraDeNegocioException("Cheque compensado não se cancela. Para desfazer o pagamento, corrija ou retire a conta no Financeiro.");
        if (c.Situacao == SituacaoChequeEmitido.Cancelado) throw new RegraDeNegocioException("Esse cheque já está cancelado.");

        c.Situacao = SituacaoChequeEmitido.Cancelado;
        c.MotivoCancelamento = texto;
        AtividadeService.Registrar(db, TipoAtividade.ChequeEmitidoCancelado,
            $"Cancelou o cheque emitido de {Dinheiro.Formatar(c.ValorCentavos)}: {texto}", $"Cheque emitido #{c.Id}", null, _agora());
        await db.SaveChangesAsync();
    }

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<ChequeEmitido> ObterAsync(PdvDbContext db, int id) =>
        await db.ChequesEmitidos.FirstOrDefaultAsync(c => c.Id == id) ?? throw new RegraDeNegocioException("Cheque não encontrado.");
}
