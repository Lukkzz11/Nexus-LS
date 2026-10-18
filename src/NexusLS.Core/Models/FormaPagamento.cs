namespace NexusLS.Core.Models;

/// <summary>Os números ficam gravados no banco: nunca reaproveitar nem trocar um número existente.</summary>
public enum FormaPagamento
{
    Dinheiro = 1,
    Pix = 2,
    CartaoDebito = 3,   // vendas antigas "Cartão" viraram débito
    CartaoCredito = 4,
    Cheque = 5,
    APrazo = 6
}
