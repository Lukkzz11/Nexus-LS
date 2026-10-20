namespace NexusLS.Core.Financeiro;

public enum TipoContaFin { Caixa = 1, Banco = 2 }

/// <summary>Entrada, saída, ou transferência entre duas contas (que nunca é receita nem despesa).</summary>
public enum SentidoFin { Entrada = 1, Saida = 2, Transferencia = 3 }

/// <summary>
/// Realizado: já aconteceu e mexe no saldo. Previsto: ainda vai acontecer (conta a pagar/receber, venda a prazo).
/// EmCompensacao: cheque ou cartão que ainda não caiu na conta. Só o Realizado entra no saldo disponível.
/// </summary>
public enum SituacaoFin { Realizado = 1, Previsto = 2, EmCompensacao = 3 }

/// <summary>De onde veio o lançamento. Serve para o id ser único e para separar cheques de cartões.</summary>
public enum OrigemFin { Venda = 1, RecebimentoCliente = 2, Lancamento = 3, Caixa = 4, Transferencia = 5, Cheque = 6, Cartao = 7, Titulo = 8 }

/// <summary>O que o lançamento é para o resultado do período.</summary>
public enum ClasseFin
{
    Venda = 1,
    RecebimentoCliente = 2,   // dinheiro de dívida antiga: aumenta o saldo mas NÃO é receita de novo
    ReceitaOutra = 3,
    Despesa = 4,
    CompraEstoque = 5,        // saída de dinheiro, mas não é despesa: o custo só entra quando a peça é vendida
    TarifaCartao = 6,         // despesa operacional
    Retirada = 7,             // sangria: tirou da gaveta, não é despesa
    Reforco = 8,              // colocou na gaveta, não é receita
    Devolucao = 9,
    Transferencia = 10
}

/// <summary>Uma conta onde o dinheiro fica: a gaveta do caixa, o cofre ou uma conta de banco.</summary>
public record ContaFin(int Id, string Nome, TipoContaFin Tipo, long SaldoInicialCentavos = 0,
    DateTime? DataSaldoInicial = null, bool Padrao = false);

/// <summary>
/// Um fato financeiro já convertido para o motor. O <see cref="Id"/> é único e determinístico ("venda:12", "lanc:7"):
/// o mesmo id nunca é contado duas vezes.
/// </summary>
public record LancFin(
    string Id,
    OrigemFin Origem,
    ClasseFin Classe,
    SentidoFin Sentido,
    SituacaoFin Situacao,
    long ValorCentavos,
    DateTime Data,
    int? ContaId = null,
    int? ContaOrigemId = null,
    int? ContaDestinoId = null,
    DateTime? DataPrevista = null,
    string? Descricao = null,
    string? Categoria = null);

/// <summary>Venda do período, para a receita bruta e o custo das mercadorias (CMV).</summary>
public record VendaFin(string Id, DateTime Data, long TotalCentavos, long CustoCentavos);

public record EntradaMotor(
    DateTime Hoje,
    IReadOnlyList<ContaFin> Contas,
    IReadOnlyList<LancFin> Lancamentos,
    IReadOnlyList<VendaFin> Vendas,
    DateTime PeriodoInicio,
    DateTime PeriodoFim);

public record SaldoConta(int Id, string Nome, TipoContaFin Tipo, long SaldoInicialCentavos,
    long EntradasCentavos, long SaidasCentavos, long DisponivelCentavos);

public record AvisoFin(string Codigo, string Mensagem);

/// <summary>Valores a receber ou a pagar, separados pela situação.</summary>
public record PendenciasFin(long EmDiaCentavos, long VencidoCentavos, long EmCompensacaoCentavos, int Quantidade)
{
    public long TotalCentavos => EmDiaCentavos + VencidoCentavos + EmCompensacaoCentavos;
}

public record EmCompensacaoFin(long ChequesCentavos, long CartoesCentavos)
{
    public long TotalCentavos => ChequesCentavos + CartoesCentavos;
}

public record ProjecaoFin(long TotalCentavos, IReadOnlyDictionary<int, long> PorDias);

public record PeriodoFin(
    DateTime Inicio, DateTime Fim,
    long EntradasRealizadasCentavos, long SaidasRealizadasCentavos,
    long ReceitaBrutaCentavos, long DevolucoesCentavos, long CustoMercadoriasCentavos,
    long OutrasReceitasCentavos, long DespesasOperacionaisCentavos,
    IReadOnlyDictionary<string, long> DespesasPorCategoria)
{
    public long FluxoCaixaCentavos => EntradasRealizadasCentavos - SaidasRealizadasCentavos;
    public long ReceitaLiquidaCentavos => ReceitaBrutaCentavos - DevolucoesCentavos;
    public long LucroBrutoCentavos => ReceitaLiquidaCentavos - CustoMercadoriasCentavos;
    public long ResultadoCentavos => LucroBrutoCentavos + OutrasReceitasCentavos - DespesasOperacionaisCentavos;
}

public record PosicaoFinanceira(
    DateTime Hoje,
    IReadOnlyList<SaldoConta> Contas,
    long DinheiroFisicoCentavos,
    long BancosCentavos,
    PendenciasFin AReceber,
    PendenciasFin APagar,
    EmCompensacaoFin EmCompensacao,
    ProjecaoFin Projecao,
    PeriodoFin Periodo,
    IReadOnlyList<AvisoFin> Avisos)
{
    /// <summary>O que existe de verdade agora (gaveta + cofre + bancos). Cheque e cartão a liquidar ficam de fora.</summary>
    public long SaldoDisponivelCentavos => DinheiroFisicoCentavos + BancosCentavos;
}
