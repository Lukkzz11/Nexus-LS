namespace NexusLS.Core.Models;

/// <summary>
/// Um ajuste no título a prazo de uma venda (valor devido e/ou vencimento). A VENDA nunca é alterada (itens, custo, receita e
/// fechamento dependem dela): o valor devido do título passa a ser o do ajuste mais recente, e a dívida do cliente continua
/// sendo recalculada por FIFO a partir dele. Nunca é apagado nem editado: um novo ajuste substitui o anterior e o histórico fica.
/// Se a venda for cancelada, o ajuste deixa de valer junto com ela.
/// </summary>
public class AjusteTituloPrazo : EntidadeSincronizavel
{
    public int Id { get; set; }
    public int VendaId { get; set; }

    /// <summary>Valor devido e vencimento ANTES do ajuste (para mostrar de → para).</summary>
    public long ValorAnteriorCentavos { get; set; }
    public DateTime VencimentoAnterior { get; set; }

    /// <summary>Valor devido e vencimento DEPOIS do ajuste (o vigente, enquanto não houver outro mais novo).</summary>
    public long ValorNovoCentavos { get; set; }
    public DateTime VencimentoNovo { get; set; }

    public string Motivo { get; set; } = string.Empty;
    public string? Operador { get; set; }
    public DateTime CriadoEm { get; set; }
}
