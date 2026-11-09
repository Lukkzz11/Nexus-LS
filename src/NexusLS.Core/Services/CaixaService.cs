using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Tudo que o fechamento mostra: o que se vendeu, o que entrou e saiu da gaveta e quanto deveria ter em cada meio.</summary>
public record ResumoTurno(
    TurnoCaixa Turno,
    int VendasValidas,
    int VendasCanceladas,
    long VendasDinheiroCentavos,
    long VendasCartaoCentavos,
    long VendasPixCentavos,
    long VendasChequeCentavos,
    long VendasAPrazoCentavos,
    long SangriasCentavos,
    long ReforcosCentavos,
    long DespesasCentavos,
    long EntradasAvulsasCentavos,
    IReadOnlyList<MovimentoCaixa> Movimentos,
    long RecebimentosDinheiroCentavos = 0,
    long RecebimentosCartaoCentavos = 0,
    long RecebimentosPixCentavos = 0)
{
    /// <summary>O dinheiro que tem que estar na gaveta: troco inicial + vendas em dinheiro + reforços/entradas − sangrias/despesas.</summary>
    public long EsperadoDinheiroCentavos =>
        Turno.TrocoInicialCentavos + VendasDinheiroCentavos + RecebimentosDinheiroCentavos + ReforcosCentavos + EntradasAvulsasCentavos
        - SangriasCentavos - DespesasCentavos;

    public long EsperadoCartaoCentavos => VendasCartaoCentavos + RecebimentosCartaoCentavos;
    public long EsperadoPixCentavos => VendasPixCentavos + RecebimentosPixCentavos;
}

/// <summary>Valores contados pelo operador no fechamento.</summary>
public record ConferenciaCaixa(long DinheiroCentavos, long CartaoCentavos, long PixCentavos, string? Observacao = null);

/// <summary>
/// Abertura e fechamento de caixa. As vendas pertencem ao turno pelo horário (da abertura até o fechamento),
/// então dois turnos nunca se cruzam: só se abre um novo depois de fechar o anterior.
/// </summary>
public class CaixaService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    /// <summary>
    /// Serializa abertura, fechamento e movimentos neste processo. O app já tem mutex de instância única,
    /// então não existem duas cópias gravando no mesmo banco: a trava basta para o duplo clique/duas abas.
    /// </summary>
    private readonly SemaphoreSlim _trava = new(1, 1);

    public CaixaService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<TurnoCaixa?> ObterAbertoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.TurnosCaixa.AsNoTracking().Where(t => t.DataFechamento == null).FirstOrDefaultAsync();
    }

    public async Task<TurnoCaixa> AbrirAsync(long trocoInicialCentavos)
    {
        if (trocoInicialCentavos < 0)
            throw new RegraDeNegocioException("O troco inicial não pode ser negativo.");

        await _trava.WaitAsync();
        try
        {
            await using var db = await _fabrica.CreateDbContextAsync();
            // A checagem fica DENTRO da trava: dois cliques ao mesmo tempo não podem abrir dois turnos.
            if (await db.TurnosCaixa.AnyAsync(t => t.DataFechamento == null))
                throw new RegraDeNegocioException("Já existe um caixa aberto. Feche-o antes de abrir outro.");

            var turno = new TurnoCaixa
            {
                DataAbertura = _agora(),
                TrocoInicialCentavos = trocoInicialCentavos,
                Operador = await NomeDoOperadorAsync(db)
            };
            db.TurnosCaixa.Add(turno);
            await db.SaveChangesAsync();
            return turno;
        }
        finally
        {
            _trava.Release();
        }
    }

    public async Task<MovimentoCaixa> RegistrarMovimentoAsync(TipoMovimentoCaixa tipo, long valorCentavos, string? descricao)
    {
        if (valorCentavos <= 0)
            throw new RegraDeNegocioException("Informe um valor maior que zero.");
        descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        if (tipo is TipoMovimentoCaixa.Despesa or TipoMovimentoCaixa.EntradaAvulsa && (descricao?.Length ?? 0) < 3)
            throw new RegraDeNegocioException("Descreva o movimento (pelo menos 3 letras).");

        await _trava.WaitAsync();
        try
        {
            await using var db = await _fabrica.CreateDbContextAsync();
            var turno = await db.TurnosCaixa.FirstOrDefaultAsync(t => t.DataFechamento == null)
                        ?? throw new RegraDeNegocioException("Abra o caixa antes de registrar movimentos.");

            if (tipo is TipoMovimentoCaixa.Sangria or TipoMovimentoCaixa.Despesa)
            {
                var resumo = await CalcularAsync(db, turno);
                if (valorCentavos > resumo.EsperadoDinheiroCentavos)
                    throw new RegraDeNegocioException(
                        $"Não há tanto dinheiro no caixa (esperado: {Dinheiro.Formatar(resumo.EsperadoDinheiroCentavos)}).");
            }

            var movimento = new MovimentoCaixa
            {
                TurnoId = turno.Id,
                DataHora = _agora(),
                Tipo = tipo,
                ValorCentavos = valorCentavos,
                Descricao = descricao
            };
            db.MovimentosCaixa.Add(movimento);
            await db.SaveChangesAsync();
            return movimento;
        }
        finally
        {
            _trava.Release();
        }
    }

    public async Task<ResumoTurno> ResumoAsync(int turnoId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var turno = await db.TurnosCaixa.AsNoTracking().FirstOrDefaultAsync(t => t.Id == turnoId)
                    ?? throw new RegraDeNegocioException("Caixa não encontrado.");
        return await CalcularAsync(db, turno);
    }

    /// <summary>
    /// Fecha o turno aberto (versão 1). Passando o id de um turno já fechado, refaz a conferência como nova versão
    /// (com os valores de agora), mantendo as anteriores no histórico. Se algum valor contado difere do esperado,
    /// a observação é obrigatória.
    /// </summary>
    public async Task<FechamentoCaixa> FecharAsync(int turnoId, ConferenciaCaixa conferencia)
    {
        if (conferencia.DinheiroCentavos < 0 || conferencia.CartaoCentavos < 0 || conferencia.PixCentavos < 0)
            throw new RegraDeNegocioException("Os valores conferidos não podem ser negativos.");

        await _trava.WaitAsync();
        try
        {
            await using var db = await _fabrica.CreateDbContextAsync();
            var turno = await db.TurnosCaixa.FirstOrDefaultAsync(t => t.Id == turnoId)
                        ?? throw new RegraDeNegocioException("Caixa não encontrado.");
            var resumo = await CalcularAsync(db, turno);
            var observacao = string.IsNullOrWhiteSpace(conferencia.Observacao) ? null : conferencia.Observacao.Trim();

            var diferente = conferencia.DinheiroCentavos != resumo.EsperadoDinheiroCentavos
                            || conferencia.CartaoCentavos != resumo.EsperadoCartaoCentavos
                            || conferencia.PixCentavos != resumo.EsperadoPixCentavos;
            var refazendo = !turno.Aberto;
            if ((diferente || refazendo) && (observacao?.Length ?? 0) < 3)
                throw new RegraDeNegocioException(refazendo
                    ? "Explique por que a conferência está sendo refeita (pelo menos 3 letras)."
                    : "Há diferença no caixa: informe uma observação (pelo menos 3 letras).");

            var agora = _agora();
            // A versão é calculada já dentro da trava. Ainda assim, outra cópia do app (fora deste processo)
            // pode ter gravado a mesma versão entre a leitura e o SaveChanges — por isso o retry único abaixo.
            var ultimaVersao = await db.FechamentosCaixa.Where(f => f.TurnoId == turnoId)
                .Select(f => (int?)f.Versao).MaxAsync() ?? 0;
            var fechamento = new FechamentoCaixa
            {
                TurnoId = turnoId,
                Versao = ultimaVersao + 1,
                DataHora = agora,
                Operador = await NomeDoOperadorAsync(db),
                EsperadoDinheiroCentavos = resumo.EsperadoDinheiroCentavos,
                ConferidoDinheiroCentavos = conferencia.DinheiroCentavos,
                EsperadoCartaoCentavos = resumo.EsperadoCartaoCentavos,
                ConferidoCartaoCentavos = conferencia.CartaoCentavos,
                EsperadoPixCentavos = resumo.EsperadoPixCentavos,
                ConferidoPixCentavos = conferencia.PixCentavos,
                Observacao = observacao
            };
            db.FechamentosCaixa.Add(fechamento);
            if (turno.Aberto) turno.DataFechamento = agora;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (EhConflitoDeVersao(ex))
            {
                // Alguém gravou a mesma (TurnoId, Versao) no meio do caminho: recalcula e tenta UMA vez.
                // Qualquer outro erro continua subindo (não é mascarado).
                fechamento.Versao = (await db.FechamentosCaixa.Where(f => f.TurnoId == turnoId)
                    .Select(f => (int?)f.Versao).MaxAsync() ?? 0) + 1;
                await db.SaveChangesAsync();
            }

            return fechamento;
        }
        finally
        {
            _trava.Release();
        }
    }

    /// <summary>Só o índice único (TurnoId, Versao) merece nova tentativa; o resto dos erros sobe normalmente.</summary>
    private static bool EhConflitoDeVersao(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 } sqlite
        && sqlite.Message.Contains("FechamentosCaixa.TurnoId", StringComparison.OrdinalIgnoreCase)
        && sqlite.Message.Contains("FechamentosCaixa.Versao", StringComparison.OrdinalIgnoreCase);

    /// <summary>Todas as conferências feitas, da mais recente para a mais antiga.</summary>
    public async Task<IReadOnlyList<FechamentoCaixa>> HistoricoAsync(int limite = 50)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.FechamentosCaixa.AsNoTracking()
            .OrderByDescending(f => f.DataHora).ThenByDescending(f => f.Versao)
            .Take(limite)
            .ToListAsync();
    }

    /// <summary>O turno já fechado que contém este horário, ou nulo (usado para avisar ao cancelar venda de caixa fechado).</summary>
    public async Task<TurnoCaixa?> TurnoFechadoDoHorarioAsync(DateTime dataHora)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.TurnosCaixa.AsNoTracking()
            .Where(t => t.DataFechamento != null && t.DataAbertura <= dataHora && t.DataFechamento >= dataHora)
            .FirstOrDefaultAsync();
    }

    private static async Task<ResumoTurno> CalcularAsync(PdvDbContext db, TurnoCaixa turno)
    {
        var fim = turno.DataFechamento ?? DateTime.MaxValue;
        // O filtro de data vai para o SQL; só as colunas usadas são lidas.
        var vendas = await db.Vendas.AsNoTracking()
            .Where(v => v.DataHora >= turno.DataAbertura && v.DataHora <= fim)
            .Select(v => new { v.Cancelada, v.FormaPagamento, v.TotalCentavos })
            .ToListAsync();
        var validas = vendas.Where(v => !v.Cancelada).ToList();
        long Soma(params FormaPagamento[] formas) => validas.Where(v => formas.Contains(v.FormaPagamento)).Sum(v => v.TotalCentavos);

        var movimentos = (await db.MovimentosCaixa.AsNoTracking().Where(m => m.TurnoId == turno.Id).ToListAsync())
            .OrderBy(m => m.DataHora).ThenBy(m => m.Id).ToList();
        long Mov(TipoMovimentoCaixa t) => movimentos.Where(m => m.Tipo == t).Sum(m => m.ValorCentavos);

        // Dívidas de clientes pagas durante o turno também entram na gaveta, na maquininha ou no Pix.
        var recebimentos = await db.PagamentosCliente.AsNoTracking()
            .Where(p => p.DataHora >= turno.DataAbertura && p.DataHora <= fim)
            .Select(p => new { p.Forma, p.ValorCentavos })
            .ToListAsync();
        long Rec(params FormaPagamento[] formas) => recebimentos.Where(p => formas.Contains(p.Forma)).Sum(p => p.ValorCentavos);

        return new ResumoTurno(
            turno, validas.Count, vendas.Count - validas.Count,
            Soma(FormaPagamento.Dinheiro),
            Soma(FormaPagamento.CartaoDebito, FormaPagamento.CartaoCredito),
            Soma(FormaPagamento.Pix),
            Soma(FormaPagamento.Cheque),
            Soma(FormaPagamento.APrazo),
            Mov(TipoMovimentoCaixa.Sangria), Mov(TipoMovimentoCaixa.Reforco),
            Mov(TipoMovimentoCaixa.Despesa), Mov(TipoMovimentoCaixa.EntradaAvulsa),
            movimentos,
            Rec(FormaPagamento.Dinheiro),
            Rec(FormaPagamento.CartaoDebito, FormaPagamento.CartaoCredito),
            Rec(FormaPagamento.Pix));
    }

    private static async Task<string> NomeDoOperadorAsync(PdvDbContext db)
    {
        if (!string.IsNullOrWhiteSpace(OperadorAtual.Nome)) return OperadorAtual.Nome!; // funcionário que fez o login
        var nome = await db.ConfiguracoesLoja.AsNoTracking().Where(c => c.Id == 1).Select(c => c.NomeOperador).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(nome) ? "Caixa" : nome;
    }
}
