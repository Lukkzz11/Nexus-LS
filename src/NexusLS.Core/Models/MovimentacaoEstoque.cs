namespace NexusLS.Core.Models;

/// <summary>
/// Registro de toda mudança no estoque (ajuste manual, venda ou cancelamento).
/// Serve de "extrato" do produto: nunca é alterado nem apagado.
/// </summary>
public class MovimentacaoEstoque
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public DateTime DataHora { get; set; }
    public TipoMovimentacao Tipo { get; set; }

    /// <summary>Variação com sinal: +10 entrada, -2 perda/venda.</summary>
    public int Quantidade { get; set; }

    /// <summary>Tamanho afetado. Vazio (nulo) nos registros antigos, anteriores à grade de tamanhos.</summary>
    public string? Tamanho { get; set; }

    public string? Motivo { get; set; }

    /// <summary>Preenchido quando a movimentação veio de uma venda (ou do cancelamento dela).</summary>
    public int? VendaId { get; set; }
}
