namespace PdvSimples.Core.Models;

public class ItemVenda
{
    public int Id { get; set; }
    public int VendaId { get; set; }
    public int ProdutoId { get; set; }

    /// <summary>Nome e preço são copiados na hora da venda: mudar o produto depois não altera o histórico.</summary>
    public string NomeProduto { get; set; } = string.Empty;
    public long PrecoUnitarioCentavos { get; set; }
    public int Quantidade { get; set; }
    public long TotalCentavos { get; set; }
}
