namespace NexusLS.Core.Models;

public class Venda
{
    public int Id { get; set; }
    public DateTime DataHora { get; set; }

    public long SubtotalCentavos { get; set; }
    public long DescontoCentavos { get; set; }
    public long TotalCentavos { get; set; }

    public FormaPagamento FormaPagamento { get; set; }

    /// <summary>Só para dinheiro: quanto o cliente entregou.</summary>
    public long? ValorRecebidoCentavos { get; set; }
    public long TrocoCentavos { get; set; }

    /// <summary>Venda cancelada nunca é apagada; apenas marcada.</summary>
    public bool Cancelada { get; set; }
    public DateTime? DataCancelamento { get; set; }

    public List<ItemVenda> Itens { get; set; } = new();
}
