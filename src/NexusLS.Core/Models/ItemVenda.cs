namespace NexusLS.Core.Models;

public class ItemVenda
{
    public int Id { get; set; }
    public int VendaId { get; set; }
    public int ProdutoId { get; set; }

    /// <summary>Nome e preço são copiados na hora da venda: mudar o produto depois não altera o histórico.</summary>
    public string NomeProduto { get; set; } = string.Empty;

    /// <summary>Tamanho vendido (de onde saiu o estoque).</summary>
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;

    public long PrecoUnitarioCentavos { get; set; }
    public int Quantidade { get; set; }

    /// <summary>Custo unitário do produto no momento da venda (para o lucro bruto). Nulo em vendas antigas, de antes deste registro.</summary>
    public long? CustoUnitarioCentavos { get; set; }

    /// <summary>Esta venda baixou o estoque deste item? (Falso em produto "vender sem baixa".) Só quem baixou devolve ao cancelar.</summary>
    public bool BaixouEstoque { get; set; } = true;
    public long TotalCentavos { get; set; }
}
