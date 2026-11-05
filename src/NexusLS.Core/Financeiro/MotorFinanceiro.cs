using NexusLS.Core.Services;

namespace NexusLS.Core.Financeiro;

/// <summary>
/// Fórmulas oficiais dos saldos (puro: sem banco, sem tela). Toda tela que mostra saldo, a receber, a pagar ou resultado
/// deve usar este motor, para os números serem iguais em todo lugar. Dinheiro em centavos inteiros.
///
/// Regras:
///  - Só o "Realizado" mexe no saldo disponível. Cheque pendente, cartão a liquidar, venda a prazo e conta em aberto não entram.
///  - Transferência entre contas mexe nos dois saldos e nunca é receita nem despesa.
///  - Venda a prazo é receita na data da venda; o recebimento depois só aumenta o saldo (não é receita de novo).
///  - Compra de mercadoria é saída de dinheiro, mas não é despesa: o custo entra no resultado quando a peça é vendida.
///  - Retirada (sangria) e reforço de caixa não são despesa nem receita.
///  - Aporte, outra entrada, retirada do proprietário e outra saída movem o saldo mas ficam FORA do resultado.
///  - Lançamento sem conta não entra em nenhum saldo; vira aviso, para ser corrigido.
/// </summary>
public static class MotorFinanceiro
{
    private static readonly int[] DiasDaProjecao = { 7, 30, 60, 90 };

    public static PosicaoFinanceira Calcular(EntradaMotor entrada)
    {
        var hoje = entrada.Hoje.Date;
        var avisos = new List<AvisoFin>();

        // 1. Cada id vale uma vez só.
        var unicos = new Dictionary<string, LancFin>();
        var repetidos = 0;
        foreach (var l in entrada.Lancamentos)
        {
            if (l.ValorCentavos <= 0) continue;
            if (!unicos.TryAdd(l.Id, l)) repetidos++;
        }
        if (repetidos > 0)
            avisos.Add(new("duplicado", $"{repetidos} lançamento(s) repetido(s) foram ignorados."));
        var lancamentos = unicos.Values.ToList();

        // 2. Saldos por conta: só o realizado, e só depois da data do saldo inicial da conta.
        var saldos = entrada.Contas.ToDictionary(c => c.Id, c => new Acumulador(c));
        var semConta = 0;
        long semContaValor = 0;
        foreach (var l in lancamentos.Where(l => l.Situacao == SituacaoFin.Realizado))
        {
            if (l.Sentido == SentidoFin.Transferencia)
            {
                if (l.ContaOrigemId is not int de || l.ContaDestinoId is not int para
                    || !saldos.TryGetValue(de, out var origem) || !saldos.TryGetValue(para, out var destino))
                {
                    semConta++;
                    semContaValor += l.ValorCentavos;
                    continue;
                }
                if (origem.Vale(l.Data)) origem.Saidas += l.ValorCentavos;
                if (destino.Vale(l.Data)) destino.Entradas += l.ValorCentavos;
                continue;
            }

            if (l.ContaId is not int id || !saldos.TryGetValue(id, out var conta))
            {
                semConta++;
                semContaValor += l.ValorCentavos;
                continue;
            }
            if (!conta.Vale(l.Data)) continue;
            if (l.Sentido == SentidoFin.Entrada) conta.Entradas += l.ValorCentavos;
            else conta.Saidas += l.ValorCentavos;
        }

        var porConta = saldos.Values.Select(a => a.Resultado()).ToList();
        var dinheiroFisico = porConta.Where(c => c.Tipo == TipoContaFin.Caixa).Sum(c => c.DisponivelCentavos);
        var bancos = porConta.Where(c => c.Tipo == TipoContaFin.Banco).Sum(c => c.DisponivelCentavos);
        if (semConta > 0)
            avisos.Add(new("sem_conta",
                $"{semConta} lançamento(s) somam {Dinheiro.Formatar(semContaValor)} e não indicam a conta (ou não há conta de banco cadastrada): " +
                "ficam fora dos saldos até serem associados a uma conta."));

        // 3. A receber e a pagar. "Vencido" = previsto com data antes de hoje; em compensação tem a sua própria linha.
        var aReceber = Pendencias(lancamentos, SentidoFin.Entrada, hoje);
        var aPagar = Pendencias(lancamentos, SentidoFin.Saida, hoje);

        var compensando = lancamentos.Where(l => l.Situacao == SituacaoFin.EmCompensacao && l.Sentido == SentidoFin.Entrada).ToList();
        var emCompensacao = new EmCompensacaoFin(
            compensando.Where(l => l.Origem == OrigemFin.Cheque).Sum(l => l.ValorCentavos),
            compensando.Where(l => l.Origem != OrigemFin.Cheque).Sum(l => l.ValorCentavos),
            lancamentos.Where(l => l.Situacao == SituacaoFin.EmCompensacao && l.Sentido == SentidoFin.Saida).Sum(l => l.ValorCentavos));

        // 4. Projeção = disponível + o que vai entrar − o que vai sair, até cada data limite.
        var disponivel = dinheiroFisico + bancos;
        var projecao = new ProjecaoFin(
            ProjetarAte(disponivel, lancamentos, hoje, null),
            DiasDaProjecao.ToDictionary(d => d, d => ProjetarAte(disponivel, lancamentos, hoje, hoje.AddDays(d))));

        // 5. Período: entradas/saídas realizadas, receita, custo e resultado.
        var inicio = entrada.PeriodoInicio.Date;
        var fim = entrada.PeriodoFim.Date;
        bool NoPeriodo(DateTime d) => d.Date >= inicio && d.Date <= fim;

        var realizados = lancamentos
            .Where(l => l.Situacao == SituacaoFin.Realizado && l.Sentido != SentidoFin.Transferencia && NoPeriodo(l.Data)).ToList();
        var vendas = entrada.Vendas.Where(v => NoPeriodo(v.Data)).ToList();
        var despesas = realizados.Where(l => l.Sentido == SentidoFin.Saida
            && l.Classe is ClasseFin.Despesa or ClasseFin.TarifaCartao).ToList();

        var periodo = new PeriodoFin(
            inicio, fim,
            realizados.Where(l => l.Sentido == SentidoFin.Entrada).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Sentido == SentidoFin.Saida).Sum(l => l.ValorCentavos),
            vendas.Sum(v => v.TotalCentavos),
            realizados.Where(l => l.Classe == ClasseFin.Devolucao).Sum(l => l.ValorCentavos),
            vendas.Sum(v => v.CustoCentavos),
            realizados.Where(l => l.Sentido == SentidoFin.Entrada && l.Classe == ClasseFin.ReceitaOutra).Sum(l => l.ValorCentavos),
            despesas.Sum(l => l.ValorCentavos),
            despesas.GroupBy(l => l.Classe == ClasseFin.TarifaCartao ? "Taxas de cartão" : l.Categoria ?? "Sem categoria")
                .ToDictionary(g => g.Key, g => g.Sum(l => l.ValorCentavos)),
            realizados.Where(l => l.Classe == ClasseFin.Aporte).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Classe == ClasseFin.OutraEntrada).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Classe == ClasseFin.RetiradaProprietario).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Classe == ClasseFin.OutraSaida).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Classe == ClasseFin.RecebimentoCliente && l.Sentido == SentidoFin.Entrada).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Classe == ClasseFin.CompraEstoque).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Classe == ClasseFin.Retirada).Sum(l => l.ValorCentavos),
            realizados.Where(l => l.Classe == ClasseFin.Reforco).Sum(l => l.ValorCentavos),
            vendas.Sum(v => v.AjusteTituloCentavos));

        return new PosicaoFinanceira(hoje, porConta, dinheiroFisico, bancos, aReceber, aPagar, emCompensacao, projecao, periodo, avisos);
    }

    private static PendenciasFin Pendencias(List<LancFin> lancamentos, SentidoFin sentido, DateTime hoje)
    {
        var previstos = lancamentos.Where(l => l.Situacao == SituacaoFin.Previsto && l.Sentido == sentido).ToList();
        var compensando = lancamentos.Where(l => l.Situacao == SituacaoFin.EmCompensacao && l.Sentido == sentido).ToList();
        return new PendenciasFin(
            previstos.Where(l => l.Data.Date >= hoje).Sum(l => l.ValorCentavos),
            previstos.Where(l => l.Data.Date < hoje).Sum(l => l.ValorCentavos),
            compensando.Sum(l => l.ValorCentavos),
            previstos.Count + compensando.Count);
    }

    /// <summary>O que está atrasado conta como "hoje"; em compensação conta na data prevista (ou hoje, se não houver).</summary>
    private static long ProjetarAte(long disponivel, List<LancFin> lancamentos, DateTime hoje, DateTime? limite)
    {
        long Soma(SentidoFin sentido) => lancamentos
            .Where(l => l.Sentido == sentido && l.Situacao is SituacaoFin.Previsto or SituacaoFin.EmCompensacao)
            .Where(l =>
            {
                var quando = (l.Situacao == SituacaoFin.EmCompensacao ? l.DataPrevista ?? hoje : l.Data).Date;
                if (quando < hoje) quando = hoje;
                return limite is null || quando <= limite.Value;
            })
            .Sum(l => l.ValorCentavos);

        return disponivel + Soma(SentidoFin.Entrada) - Soma(SentidoFin.Saida);
    }

    private sealed class Acumulador
    {
        private readonly ContaFin _conta;
        public long Entradas;
        public long Saidas;

        public Acumulador(ContaFin conta) => _conta = conta;

        /// <summary>Movimento anterior ao saldo inicial já está dentro desse saldo: não conta de novo.</summary>
        public bool Vale(DateTime data) => _conta.DataSaldoInicial is not DateTime inicio || data.Date >= inicio.Date;

        public SaldoConta Resultado() => new(_conta.Id, _conta.Nome, _conta.Tipo, _conta.SaldoInicialCentavos,
            Entradas, Saidas, _conta.SaldoInicialCentavos + Entradas - Saidas);
    }
}
