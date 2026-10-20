namespace NexusLS.Core.Models;

/// <summary>Os números ficam gravados no banco: nunca reaproveitar nem trocar um número existente.</summary>
public enum TipoLancamento
{
    Despesa = 1,   // conta a pagar (aluguel, energia, fornecedor...)
    Receita = 2    // conta a receber que não é venda do PDV (serviço, aluguel recebido...)
}

/// <summary>
/// Uma conta a pagar ou a receber. Nunca é apagada: cancela-se com motivo. "Pago" é só ter a data de pagamento;
/// estornar o pagamento volta o lançamento para "em aberto". As vendas do PDV NÃO viram lançamento: entram direto no resumo.
/// </summary>
public class Lancamento
{
    public int Id { get; set; }
    public TipoLancamento Tipo { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string? Categoria { get; set; }

    /// <summary>Fornecedor (despesa) ou quem vai pagar (receita). Texto livre.</summary>
    public string? Contraparte { get; set; }

    public long ValorCentavos { get; set; }

    /// <summary>Só a data (hora zerada).</summary>
    public DateTime Vencimento { get; set; }

    /// <summary>Quando foi pago/recebido. Nulo = em aberto.</summary>
    public DateTime? PagoEm { get; set; }
    public FormaPagamento? FormaPagamento { get; set; }

    /// <summary>Parcelamento: lançamentos do mesmo parcelamento compartilham o GrupoId.</summary>
    public Guid? GrupoId { get; set; }
    public int Parcela { get; set; } = 1;
    public int TotalParcelas { get; set; } = 1;

    public string? Observacao { get; set; }
    public DateTime CriadoEm { get; set; }
    public string? Operador { get; set; }

    public bool Cancelado { get; set; }
    public string? MotivoCancelamento { get; set; }
    public DateTime? CanceladoEm { get; set; }
}

public enum SituacaoLancamento { Aberto, Atrasado, Pago, Cancelado }
