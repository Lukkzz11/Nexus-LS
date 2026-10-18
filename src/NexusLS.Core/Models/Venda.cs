namespace NexusLS.Core.Models;

public class Venda
{
    public int Id { get; set; }
    public DateTime DataHora { get; set; }

    /// <summary>Tabela de preço usada nesta venda (varejo ou atacado).</summary>
    public TabelaPreco TabelaPreco { get; set; } = TabelaPreco.Varejo;

    public long SubtotalCentavos { get; set; }
    public long DescontoCentavos { get; set; }
    public long TotalCentavos { get; set; }

    public FormaPagamento FormaPagamento { get; set; }

    /// <summary>Parcelas do cartão de crédito (1 a 4). Nas demais formas fica 1.</summary>
    public int Parcelas { get; set; } = 1;

    /// <summary>Só para dinheiro: quanto o cliente entregou.</summary>
    public long? ValorRecebidoCentavos { get; set; }
    public long TrocoCentavos { get; set; }

    /// <summary>Cliente da venda (obrigatório na venda a prazo, opcional nas demais).</summary>
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    /// <summary>Venda cancelada nunca é apagada; apenas marcada.</summary>
    public bool Cancelada { get; set; }
    public DateTime? DataCancelamento { get; set; }

    public List<ItemVenda> Itens { get; set; } = new();
}
