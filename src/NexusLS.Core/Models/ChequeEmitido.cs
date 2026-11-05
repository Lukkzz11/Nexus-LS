namespace NexusLS.Core.Models;

public enum SituacaoChequeEmitido { Pendente = 1, Compensado = 2, Cancelado = 3 }

/// <summary>
/// Cheque que a loja EMITIU para pagar uma conta a pagar (<see cref="Lancamento"/>). Enquanto estiver pendente a conta
/// continua em aberto, mas o motor financeiro a trata como saída "em compensação" (o dinheiro ainda não saiu da conta
/// bancária). Ao compensar, a conta é paga (forma cheque, na conta do cheque, no dia em que compensou). Cancelar
/// (com motivo) devolve a conta para "em aberto". Nada é apagado.
/// </summary>
public class ChequeEmitido : EntidadeSincronizavel
{
    public int Id { get; set; }
    public int LancamentoId { get; set; }

    /// <summary>Valor do cheque = valor da conta no momento da emissão.</summary>
    public long ValorCentavos { get; set; }
    public string? Numero { get; set; }
    public string? Banco { get; set; }

    /// <summary>Para quem o cheque foi emitido (por padrão, a contraparte da conta).</summary>
    public string? Favorecido { get; set; }

    /// <summary>A data em que o favorecido pode depositar (a previsão de saída do dinheiro).</summary>
    public DateTime BomPara { get; set; }

    /// <summary>A conta bancária de onde o cheque vai sair.</summary>
    public int ContaId { get; set; }
    public SituacaoChequeEmitido Situacao { get; set; } = SituacaoChequeEmitido.Pendente;

    public DateTime? CompensadoEm { get; set; }
    public string? MotivoCancelamento { get; set; }
    public string? Operador { get; set; }
    public DateTime CriadoEm { get; set; }
}
