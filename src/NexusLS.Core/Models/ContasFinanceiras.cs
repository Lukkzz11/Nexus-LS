using NexusLS.Core.Financeiro;

namespace NexusLS.Core.Models;

/// <summary>
/// Onde o dinheiro fica: a gaveta do caixa, o cofre ou uma conta de banco. O saldo NÃO é guardado: é sempre calculado
/// pelo motor financeiro (saldo inicial + o que entrou − o que saiu). Nunca é apagada: desativa-se.
/// </summary>
public class ContaFinanceira : EntidadeSincronizavel
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public TipoContaFin Tipo { get; set; }

    /// <summary>Quanto havia na conta na <see cref="DataSaldoInicial"/>. O que aconteceu antes dessa data não conta.</summary>
    public long SaldoInicialCentavos { get; set; }
    public DateTime? DataSaldoInicial { get; set; }

    /// <summary>A conta de banco onde caem Pix e cartão quando nada diz o contrário.</summary>
    public bool Padrao { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTime CriadaEm { get; set; }
}

/// <summary>Dinheiro que mudou de uma conta para outra. Não é receita nem despesa. Nunca é apagada: cancela-se com motivo.</summary>
public class TransferenciaConta : EntidadeSincronizavel
{
    public int Id { get; set; }
    public int ContaOrigemId { get; set; }
    public int ContaDestinoId { get; set; }
    public long ValorCentavos { get; set; }

    /// <summary>Só a data (hora zerada).</summary>
    public DateTime Data { get; set; }
    public string? Descricao { get; set; }
    public string? Operador { get; set; }
    public DateTime CriadaEm { get; set; }

    public bool Cancelada { get; set; }
    public string? MotivoCancelamento { get; set; }
    public DateTime? CanceladaEm { get; set; }
}

/// <summary>
/// O dia em que o dinheiro de uma venda no cartão caiu na conta (e a taxa descontada). Enquanto não existir, a venda
/// fica "em compensação" (cartão a liquidar). Cancelar a liquidação (com motivo) devolve a venda para "a liquidar".
/// </summary>
public class LiquidacaoCartao : EntidadeSincronizavel
{
    public int Id { get; set; }
    public int VendaId { get; set; }
    public DateTime Data { get; set; }
    public int ContaId { get; set; }

    /// <summary>Taxa da operadora, já descontada do valor que caiu. Vira despesa (tarifa de cartão).</summary>
    public long TaxaCentavos { get; set; }
    public string? Operador { get; set; }
    public DateTime CriadaEm { get; set; }

    public bool Cancelada { get; set; }
    public string? MotivoCancelamento { get; set; }
}

public enum SituacaoCheque { Pendente = 1, Compensado = 2, Devolvido = 3, Cancelado = 4 }

/// <summary>
/// Cheque que a loja recebeu. Pendente fica "em compensação" (não é saldo). Compensado entra na conta; devolvido não entra
/// (a cobrança volta para quem pagou). Venda ou pagamento de cliente no cheque cria um cheque pendente sozinho.
/// </summary>
public class ChequeRecebido : EntidadeSincronizavel
{
    public int Id { get; set; }
    public long ValorCentavos { get; set; }
    public string? Numero { get; set; }
    public string? Banco { get; set; }
    public string? Emitente { get; set; }

    /// <summary>A data em que o cheque pode ser depositado ("bom para").</summary>
    public DateTime BomPara { get; set; }
    public SituacaoCheque Situacao { get; set; } = SituacaoCheque.Pendente;

    /// <summary>De onde veio: uma venda no cheque, um pagamento de dívida de cliente ou nenhum (recebido à parte).</summary>
    public int? VendaId { get; set; }
    public int? PagamentoClienteId { get; set; }

    public DateTime? CompensadoEm { get; set; }
    public int? ContaId { get; set; }

    public string? Observacao { get; set; }
    public string? MotivoDevolucao { get; set; }
    public string? Operador { get; set; }
    public DateTime CriadoEm { get; set; }
}
