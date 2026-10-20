using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Venda no cartão que ainda não caiu na conta.</summary>
public record CartaoAliquidar(int VendaId, DateTime DataVenda, FormaPagamento Forma, int Parcelas, long TotalCentavos, DateTime PrevisaoDeCaida);

/// <summary>Venda no cartão cujo dinheiro já caiu.</summary>
public record CartaoLiquidado(int VendaId, DateTime DataVenda, long TotalCentavos, DateTime DataLiquidacao, string Conta, long TaxaCentavos);

public static class PrazosFinanceiros
{
    public const int DiasDebito = 1;
    public const int DiasCredito = 30;
    public const int DiasCheque = 30;

    /// <summary>Quando o motor espera ver o dinheiro: débito no dia seguinte, crédito em 30 dias.</summary>
    public static DateTime PrevisaoDoCartao(DateTime dataVenda, FormaPagamento forma) =>
        dataVenda.Date.AddDays(forma == FormaPagamento.CartaoDebito ? DiasDebito : DiasCredito);
}

/// <summary>
/// Liquidação do cartão: o dia em que o dinheiro de uma venda no cartão caiu na conta, com a taxa da operadora.
/// Antes disso a venda é "cartão a liquidar" (em compensação) e não faz parte do saldo.
/// </summary>
public class LiquidacaoCartaoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public LiquidacaoCartaoService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<List<CartaoAliquidar>> ListarAliquidarAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var liquidadas = db.LiquidacoesCartao.Where(l => !l.Cancelada).Select(l => l.VendaId);
        var vendas = await db.Vendas.AsNoTracking()
            .Where(v => !v.Cancelada && (v.FormaPagamento == FormaPagamento.CartaoDebito || v.FormaPagamento == FormaPagamento.CartaoCredito)
                        && !liquidadas.Contains(v.Id))
            .ToListAsync();
        return vendas.OrderBy(v => v.DataHora)
            .Select(v => new CartaoAliquidar(v.Id, v.DataHora, v.FormaPagamento, v.Parcelas, v.TotalCentavos,
                PrazosFinanceiros.PrevisaoDoCartao(v.DataHora, v.FormaPagamento))).ToList();
    }

    /// <summary>Vendas no cartão já liquidadas no período (para conferir ou estornar).</summary>
    public async Task<List<CartaoLiquidado>> ListarLiquidadosAsync(DateTime de, DateTime ate)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var liquidacoes = await db.LiquidacoesCartao.AsNoTracking()
            .Where(l => !l.Cancelada && l.Data >= de.Date && l.Data <= ate.Date).ToListAsync();
        var ids = liquidacoes.Select(l => l.VendaId).ToList();
        var vendas = await db.Vendas.AsNoTracking().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        var contas = await db.ContasFinanceiras.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome);
        return liquidacoes.OrderByDescending(l => l.Data).ThenByDescending(l => l.Id)
            .Select(l => new CartaoLiquidado(l.VendaId, vendas[l.VendaId].DataHora, vendas[l.VendaId].TotalCentavos, l.Data,
                contas.GetValueOrDefault(l.ContaId, "?"), l.TaxaCentavos)).ToList();
    }

    public async Task<LiquidacaoCartao> LiquidarAsync(int vendaId, DateTime data, int contaId, long taxaCentavos = 0)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var venda = await db.Vendas.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException("Venda não encontrada.");
        if (venda.Cancelada) throw new RegraDeNegocioException("Venda cancelada não tem dinheiro a receber.");
        if (venda.FormaPagamento is not (FormaPagamento.CartaoDebito or FormaPagamento.CartaoCredito))
            throw new RegraDeNegocioException("Só venda no cartão é liquidada aqui.");
        if (await db.LiquidacoesCartao.AnyAsync(l => l.VendaId == vendaId && !l.Cancelada))
            throw new RegraDeNegocioException("Essa venda já foi liquidada.");
        if (taxaCentavos < 0 || taxaCentavos > venda.TotalCentavos)
            throw new RegraDeNegocioException("A taxa não pode ser negativa nem maior que o valor da venda.");
        if (data.Date < venda.DataHora.Date) throw new RegraDeNegocioException("O dinheiro não pode cair antes do dia da venda.");
        if (data.Date > _agora().Date) throw new RegraDeNegocioException("A data da liquidação não pode ser no futuro.");
        var conta = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contaId)
            ?? throw new RegraDeNegocioException("Conta não encontrada.");
        if (!conta.Ativa) throw new RegraDeNegocioException("A conta escolhida está desativada.");

        var l = new LiquidacaoCartao
        {
            VendaId = vendaId, Data = data.Date, ContaId = contaId, TaxaCentavos = taxaCentavos,
            Operador = OperadorAtual.Nome, CriadaEm = _agora()
        };
        db.LiquidacoesCartao.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    public async Task EstornarAsync(int vendaId, string motivo)
    {
        motivo = (motivo ?? "").Trim();
        if (motivo.Length < 3) throw new RegraDeNegocioException("Informe o motivo (pelo menos 3 letras).");
        await using var db = await _fabrica.CreateDbContextAsync();
        var l = await db.LiquidacoesCartao.FirstOrDefaultAsync(x => x.VendaId == vendaId && !x.Cancelada)
            ?? throw new RegraDeNegocioException("Essa venda não tem liquidação para estornar.");
        l.Cancelada = true;
        l.MotivoCancelamento = motivo;
        AtividadeService.Registrar(db, TipoAtividade.LiquidacaoEstornada,
            $"Estornou a liquidação do cartão da venda #{vendaId}: {motivo}", $"Venda #{vendaId}", agora: _agora());
        await db.SaveChangesAsync();
    }
}

public record DadosCheque(long ValorCentavos, DateTime BomPara, string? Numero = null, string? Banco = null, string? Emitente = null, string? Observacao = null);

/// <summary>
/// Cheques recebidos. Pendente = em compensação (não é saldo). Compensar põe o dinheiro numa conta; devolver avisa que o
/// dinheiro não veio e que é preciso cobrar. Nada é apagado.
/// </summary>
public class ChequeService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public ChequeService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Usado pela venda e pelo pagamento de cliente: cria o cheque pendente na MESMA transação de quem chamou.</summary>
    internal static ChequeRecebido NovoPendente(long valorCentavos, DateTime quando, string? emitente, int? vendaId, int? pagamentoClienteId) =>
        new()
        {
            ValorCentavos = valorCentavos,
            BomPara = quando.Date.AddDays(PrazosFinanceiros.DiasCheque),
            Emitente = emitente,
            VendaId = vendaId,
            PagamentoClienteId = pagamentoClienteId,
            Operador = OperadorAtual.Nome,
            CriadoEm = quando
        };

    public async Task<List<ChequeRecebido>> ListarAsync(SituacaoCheque? situacao = null)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var consulta = db.ChequesRecebidos.AsNoTracking();
        if (situacao is SituacaoCheque s) consulta = consulta.Where(c => c.Situacao == s);
        return (await consulta.ToListAsync()).OrderBy(c => c.BomPara).ThenBy(c => c.Id).ToList();
    }

    /// <summary>Cheque recebido à parte (que não veio de uma venda do PDV). Quando compensar, conta como outra receita.</summary>
    public async Task<ChequeRecebido> RegistrarAsync(DadosCheque dados)
    {
        Validar(dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = new ChequeRecebido { CriadoEm = _agora(), Operador = OperadorAtual.Nome };
        Preencher(c, dados);
        db.ChequesRecebidos.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    /// <summary>Completa os dados de um cheque que ainda não foi compensado (número, banco, emitente, "bom para"). O valor não muda.</summary>
    public async Task EditarAsync(int id, DadosCheque dados)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await ObterAsync(db, id);
        if (c.Situacao != SituacaoCheque.Pendente) throw new RegraDeNegocioException("Só dá para editar cheque pendente.");
        Validar(dados with { ValorCentavos = c.ValorCentavos });
        Preencher(c, dados with { ValorCentavos = c.ValorCentavos });
        await db.SaveChangesAsync();
    }

    public async Task CompensarAsync(int id, int contaId, DateTime data)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await ObterAsync(db, id);
        if (c.Situacao != SituacaoCheque.Pendente) throw new RegraDeNegocioException("Esse cheque não está pendente.");
        if (data.Date > _agora().Date) throw new RegraDeNegocioException("A data da compensação não pode ser no futuro.");
        var conta = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == contaId)
            ?? throw new RegraDeNegocioException("Conta não encontrada.");
        if (!conta.Ativa) throw new RegraDeNegocioException("A conta escolhida está desativada.");

        c.Situacao = SituacaoCheque.Compensado;
        c.ContaId = contaId;
        c.CompensadoEm = data.Date;
        await db.SaveChangesAsync();
    }

    /// <summary>O banco devolveu: o dinheiro não veio. Cheque de pagamento de cliente não pode ser devolvido aqui (a dívida já foi abatida).</summary>
    public async Task DevolverAsync(int id, string motivo)
    {
        motivo = (motivo ?? "").Trim();
        if (motivo.Length < 3) throw new RegraDeNegocioException("Informe o motivo (pelo menos 3 letras).");
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await ObterAsync(db, id);
        if (c.Situacao != SituacaoCheque.Pendente) throw new RegraDeNegocioException("Só cheque pendente pode ser devolvido.");
        if (c.PagamentoClienteId is not null)
            throw new RegraDeNegocioException(
                "Este cheque pagou a dívida de um cliente, e o pagamento já foi abatido. Combine um novo pagamento com o cliente e use \"Cancelar\" neste cheque.");

        c.Situacao = SituacaoCheque.Devolvido;
        c.MotivoDevolucao = motivo;
        AtividadeService.Registrar(db, TipoAtividade.ChequeDevolvido,
            $"Cheque de {Dinheiro.Formatar(c.ValorCentavos)}{(c.Emitente is null ? "" : $" de {c.Emitente}")} devolvido: {motivo}",
            $"Cheque #{c.Id}", agora: _agora());
        await db.SaveChangesAsync();
    }

    public async Task CancelarAsync(int id, string motivo)
    {
        motivo = (motivo ?? "").Trim();
        if (motivo.Length < 3) throw new RegraDeNegocioException("Informe o motivo (pelo menos 3 letras).");
        await using var db = await _fabrica.CreateDbContextAsync();
        var c = await ObterAsync(db, id);
        if (c.Situacao == SituacaoCheque.Compensado) throw new RegraDeNegocioException("Cheque compensado não pode ser cancelado.");
        if (c.Situacao == SituacaoCheque.Cancelado) throw new RegraDeNegocioException("Esse cheque já está cancelado.");

        c.Situacao = SituacaoCheque.Cancelado;
        c.Observacao = string.IsNullOrWhiteSpace(c.Observacao) ? $"Cancelado: {motivo}" : $"{c.Observacao} | Cancelado: {motivo}";
        AtividadeService.Registrar(db, TipoAtividade.ChequeCancelado,
            $"Cancelou o cheque de {Dinheiro.Formatar(c.ValorCentavos)}: {motivo}", $"Cheque #{c.Id}", agora: _agora());
        await db.SaveChangesAsync();
    }

    private static void Validar(DadosCheque d)
    {
        if (d.ValorCentavos <= 0) throw new RegraDeNegocioException("O valor do cheque deve ser maior que zero.");
    }

    private static void Preencher(ChequeRecebido c, DadosCheque d)
    {
        c.ValorCentavos = d.ValorCentavos;
        c.BomPara = d.BomPara.Date;
        c.Numero = Limpar(d.Numero);
        c.Banco = Limpar(d.Banco);
        c.Emitente = Limpar(d.Emitente);
        c.Observacao = Limpar(d.Observacao);
    }

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<ChequeRecebido> ObterAsync(PdvDbContext db, int id) =>
        await db.ChequesRecebidos.FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new RegraDeNegocioException("Cheque não encontrado.");
}
