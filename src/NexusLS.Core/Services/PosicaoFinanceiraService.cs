using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Lê o banco e monta a entrada do <see cref="MotorFinanceiro"/>: cada fato (venda, cheque, cartão, pagamento de cliente,
/// dívida, conta a pagar/receber, movimento do caixa, transferência) vira um lançamento com id único. É a ÚNICA porta de
/// entrada para saldos: toda tela que mostra saldo, a receber, a pagar ou resultado deve usar este serviço.
///
/// Onde cada dinheiro entra: dinheiro → gaveta; Pix → conta de banco padrão; cartão → "em compensação" até a liquidação
/// (aí vai para a conta escolhida, com a taxa como despesa); cheque → "em compensação" até compensar; venda a prazo → só
/// a receber (a dívida do cliente), e o pagamento dele entra pela forma usada. O troco inicial do turno NÃO entra: é
/// dinheiro que já estava na gaveta.
/// </summary>
public class PosicaoFinanceiraService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public PosicaoFinanceiraService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Como o lançamento do Financeiro entra no motor: compra de mercadoria e classificações especiais não são despesa/receita.</summary>
    internal static ClasseFin ClasseDoLancamento(Lancamento l)
    {
        if (l.Tipo == TipoLancamento.Receita)
            return l.Classificacao switch
            {
                ClassificacaoLancamento.Aporte => ClasseFin.Aporte,
                ClassificacaoLancamento.OutraEntrada => ClasseFin.OutraEntrada,
                _ => ClasseFin.ReceitaOutra
            };
        if (l.CompraId is not null) return ClasseFin.CompraEstoque;
        return l.Classificacao switch
        {
            ClassificacaoLancamento.RetiradaProprietario => ClasseFin.RetiradaProprietario,
            ClassificacaoLancamento.OutraSaida => ClasseFin.OutraSaida,
            _ => ClasseFin.Despesa
        };
    }

    public async Task<PosicaoFinanceira> CalcularAsync(DateTime de, DateTime ate)
    {
        await new ContaFinanceiraService(_fabrica, _agora).GarantirContaPadraoAsync();
        await using var db = await _fabrica.CreateDbContextAsync();

        var contas = await db.ContasFinanceiras.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        var caixaId = contas.Where(c => c.Ativa && c.Tipo == TipoContaFin.Caixa).Select(c => (int?)c.Id).FirstOrDefault();
        var bancoId = contas.Where(c => c.Ativa && c.Tipo == TipoContaFin.Banco)
            .OrderByDescending(c => c.Padrao).Select(c => (int?)c.Id).FirstOrDefault();

        int? ContaDaForma(FormaPagamento? forma) => forma is null or FormaPagamento.Dinheiro ? caixaId : bancoId;

        var vendas = await db.Vendas.AsNoTracking().Where(v => !v.Cancelada).ToListAsync();
        var liquidacoes = (await db.LiquidacoesCartao.AsNoTracking().Where(l => !l.Cancelada).ToListAsync())
            .ToDictionary(l => l.VendaId);
        var cheques = await db.ChequesRecebidos.AsNoTracking().ToListAsync();
        var chequesDeVenda = cheques.Where(c => c.VendaId is not null).Select(c => c.VendaId!.Value).ToHashSet();
        var chequesDePagamento = cheques.Where(c => c.PagamentoClienteId is not null).Select(c => c.PagamentoClienteId!.Value).ToHashSet();

        var lancs = new List<LancFin>();

        // ---- vendas do PDV ----
        foreach (var v in vendas)
        {
            var data = v.DataHora;
            switch (v.FormaPagamento)
            {
                case FormaPagamento.APrazo:
                    break; // sem dinheiro agora: vira título a receber (abaixo)

                case FormaPagamento.CartaoDebito or FormaPagamento.CartaoCredito:
                    if (liquidacoes.TryGetValue(v.Id, out var liq))
                    {
                        lancs.Add(new($"venda:{v.Id}", OrigemFin.Venda, ClasseFin.Venda, SentidoFin.Entrada, SituacaoFin.Realizado,
                            v.TotalCentavos, liq.Data, ContaId: liq.ContaId, Descricao: $"Venda #{v.Id} (cartão)"));
                        if (liq.TaxaCentavos > 0)
                            lancs.Add(new($"taxa-cartao:{v.Id}", OrigemFin.Cartao, ClasseFin.TarifaCartao, SentidoFin.Saida, SituacaoFin.Realizado,
                                liq.TaxaCentavos, liq.Data, ContaId: liq.ContaId, Descricao: $"Taxa do cartão da venda #{v.Id}"));
                    }
                    else
                    {
                        lancs.Add(new($"venda:{v.Id}", OrigemFin.Venda, ClasseFin.Venda, SentidoFin.Entrada, SituacaoFin.EmCompensacao,
                            v.TotalCentavos, data, DataPrevista: PrazosFinanceiros.PrevisaoDoCartao(data, v.FormaPagamento),
                            Descricao: $"Venda #{v.Id} (cartão a liquidar)"));
                    }
                    break;

                case FormaPagamento.Cheque:
                    if (chequesDeVenda.Contains(v.Id)) break; // o cheque da venda é quem representa o dinheiro
                    lancs.Add(new($"venda:{v.Id}", OrigemFin.Cheque, ClasseFin.Venda, SentidoFin.Entrada, SituacaoFin.EmCompensacao,
                        v.TotalCentavos, data, DataPrevista: data.Date.AddDays(PrazosFinanceiros.DiasCheque), Descricao: $"Venda #{v.Id} (cheque)"));
                    break;

                default:
                    lancs.Add(new($"venda:{v.Id}", OrigemFin.Venda, ClasseFin.Venda, SentidoFin.Entrada, SituacaoFin.Realizado,
                        v.TotalCentavos, data, ContaId: ContaDaForma(v.FormaPagamento), Descricao: $"Venda #{v.Id}"));
                    break;
            }
        }

        // ---- cheques recebidos ----
        foreach (var c in cheques)
        {
            var classe = c.VendaId is not null ? ClasseFin.Venda : c.PagamentoClienteId is not null ? ClasseFin.RecebimentoCliente : ClasseFin.ReceitaOutra;
            var descricao = $"Cheque{(string.IsNullOrWhiteSpace(c.Numero) ? "" : " " + c.Numero)}{(string.IsNullOrWhiteSpace(c.Emitente) ? "" : " de " + c.Emitente)}";
            switch (c.Situacao)
            {
                case SituacaoCheque.Pendente:
                    lancs.Add(new($"cheque:{c.Id}", OrigemFin.Cheque, classe, SentidoFin.Entrada, SituacaoFin.EmCompensacao,
                        c.ValorCentavos, c.CriadoEm, DataPrevista: c.BomPara, Descricao: descricao));
                    break;
                case SituacaoCheque.Compensado:
                    lancs.Add(new($"cheque:{c.Id}", OrigemFin.Cheque, classe, SentidoFin.Entrada, SituacaoFin.Realizado,
                        c.ValorCentavos, c.CompensadoEm ?? c.BomPara, ContaId: c.ContaId, Descricao: descricao));
                    break;
                case SituacaoCheque.Devolvido: // o dinheiro não veio: volta a ser algo a receber (vencido)
                    lancs.Add(new($"cheque:{c.Id}", OrigemFin.Cheque, classe, SentidoFin.Entrada, SituacaoFin.Previsto,
                        c.ValorCentavos, c.BomPara, Descricao: descricao + " (devolvido)"));
                    break;
            }
        }

        // ---- pagamentos de clientes (dívida a prazo) ----
        var pagamentos = await db.PagamentosCliente.AsNoTracking().ToListAsync();
        foreach (var p in pagamentos)
        {
            if (p.Forma == FormaPagamento.Cheque)
            {
                if (chequesDePagamento.Contains(p.Id)) continue;
                lancs.Add(new($"pagcli:{p.Id}", OrigemFin.Cheque, ClasseFin.RecebimentoCliente, SentidoFin.Entrada, SituacaoFin.EmCompensacao,
                    p.ValorCentavos, p.DataHora, DataPrevista: p.DataHora.Date.AddDays(PrazosFinanceiros.DiasCheque), Descricao: "Pagamento de cliente (cheque)"));
                continue;
            }
            lancs.Add(new($"pagcli:{p.Id}", OrigemFin.RecebimentoCliente, ClasseFin.RecebimentoCliente, SentidoFin.Entrada, SituacaoFin.Realizado,
                p.ValorCentavos, p.DataHora, ContaId: ContaDaForma(p.Forma), Descricao: "Pagamento de cliente"));
        }

        // ---- dívidas de clientes em aberto (a receber) ----
        var aPrazo = vendas.Where(v => v.FormaPagamento == FormaPagamento.APrazo && v.ClienteId is not null).GroupBy(v => v.ClienteId!.Value);
        foreach (var grupo in aPrazo)
        {
            var conta = ContaCorrente.Calcular(grupo, pagamentos.Where(p => p.ClienteId == grupo.Key));
            foreach (var t in conta.Titulos.Where(t => t.SaldoCentavos > 0))
                lancs.Add(new($"titulo:{t.VendaId}", OrigemFin.Titulo, ClasseFin.RecebimentoCliente, SentidoFin.Entrada, SituacaoFin.Previsto,
                    t.SaldoCentavos, t.Vencimento, Descricao: $"Venda a prazo #{t.VendaId}"));
        }

        // ---- contas a pagar e a receber (Financeiro) ----
        var lancamentos = await db.Lancamentos.AsNoTracking().Where(l => !l.Cancelado).ToListAsync();
        foreach (var l in lancamentos)
        {
            var entrada = l.Tipo == TipoLancamento.Receita;
            var classe = ClasseDoLancamento(l);
            var sentido = entrada ? SentidoFin.Entrada : SentidoFin.Saida;
            if (l.PagoEm is DateTime pago)
                lancs.Add(new($"lanc:{l.Id}", OrigemFin.Lancamento, classe, sentido, SituacaoFin.Realizado, l.ValorCentavos, pago,
                    ContaId: l.ContaId ?? ContaDaForma(l.FormaPagamento), Descricao: l.Descricao, Categoria: l.Categoria ?? (l.CompraId is not null ? "Fornecedores" : null)));
            else
                lancs.Add(new($"lanc:{l.Id}", OrigemFin.Lancamento, classe, sentido, SituacaoFin.Previsto, l.ValorCentavos, l.Vencimento,
                    Descricao: l.Descricao, Categoria: l.Categoria));
        }

        // ---- movimentos do caixa do PDV (sangria, reforço, despesa, entrada avulsa), sempre na gaveta ----
        foreach (var m in await db.MovimentosCaixa.AsNoTracking().ToListAsync())
        {
            var (classe, sentido) = m.Tipo switch
            {
                TipoMovimentoCaixa.Sangria => (ClasseFin.Retirada, SentidoFin.Saida),
                TipoMovimentoCaixa.Reforco => (ClasseFin.Reforco, SentidoFin.Entrada),
                TipoMovimentoCaixa.Despesa => (ClasseFin.Despesa, SentidoFin.Saida),
                _ => (ClasseFin.ReceitaOutra, SentidoFin.Entrada)
            };
            lancs.Add(new($"mov:{m.Id}", OrigemFin.Caixa, classe, sentido, SituacaoFin.Realizado, m.ValorCentavos, m.DataHora,
                ContaId: caixaId, Descricao: m.Descricao, Categoria: m.Tipo == TipoMovimentoCaixa.Despesa ? "Despesa do caixa" : null));
        }

        // ---- transferências entre contas ----
        foreach (var t in await db.Transferencias.AsNoTracking().Where(t => !t.Cancelada).ToListAsync())
            lancs.Add(new($"transf:{t.Id}", OrigemFin.Transferencia, ClasseFin.Transferencia, SentidoFin.Transferencia, SituacaoFin.Realizado,
                t.ValorCentavos, t.Data, ContaOrigemId: t.ContaOrigemId, ContaDestinoId: t.ContaDestinoId, Descricao: t.Descricao));

        // ---- receita e custo das vendas do período ----
        var inicio = de.Date;
        var fimExclusivo = ate.Date.AddDays(1);
        var doPeriodo = await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => !v.Cancelada && v.DataHora >= inicio && v.DataHora < fimExclusivo).ToListAsync();
        var idsProdutos = doPeriodo.SelectMany(v => v.Itens).Select(i => i.ProdutoId).Distinct().ToList();
        var custosAtuais = await db.Produtos.AsNoTracking().Where(p => idsProdutos.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PrecoCustoCentavos);
        var vendasFin = doPeriodo.Select(v => new VendaFin($"venda:{v.Id}", v.DataHora, v.TotalCentavos,
            v.Itens.Sum(i => (i.CustoUnitarioCentavos ?? custosAtuais.GetValueOrDefault(i.ProdutoId)) * i.Quantidade))).ToList();

        var contasFin = contas.Select(c => new ContaFin(c.Id, c.Nome, c.Tipo, c.SaldoInicialCentavos, c.DataSaldoInicial, c.Padrao)).ToList();
        return MotorFinanceiro.Calcular(new EntradaMotor(_agora(), contasFin, lancs, vendasFin, de, ate));
    }
}
