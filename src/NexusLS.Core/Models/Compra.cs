namespace NexusLS.Core.Models;

public class Fornecedor : EntidadeSincronizavel
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    /// <summary>CNPJ ou CPF (opcional, guardado como foi digitado).</summary>
    public string? Documento { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Observacao { get; set; }

    /// <summary>Fornecedor desativado some da busca, mas continua nas compras antigas.</summary>
    public bool Ativo { get; set; } = true;
    public DateTime DataCadastro { get; set; }
}

/// <summary>
/// Uma compra de mercadoria. Ao registrar, o estoque de cada item aumenta (com movimentação de entrada), o custo do
/// produto pode ser atualizado e as contas a pagar podem ser geradas. Nunca é apagada: cancela-se com motivo.
/// </summary>
public class Compra : EntidadeSincronizavel
{
    public int Id { get; set; }
    public int? FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }

    public DateTime DataHora { get; set; }

    /// <summary>Número da nota fiscal ou do pedido do fornecedor (opcional).</summary>
    public string? NumeroNota { get; set; }

    public long TotalCentavos { get; set; }
    public string? Observacao { get; set; }
    public string? Operador { get; set; }

    public bool Cancelada { get; set; }
    public string? MotivoCancelamento { get; set; }
    public DateTime? CanceladaEm { get; set; }

    public List<ItemCompra> Itens { get; set; } = new();
}

public class ItemCompra
{
    public int Id { get; set; }
    public int CompraId { get; set; }
    public int ProdutoId { get; set; }

    /// <summary>Nome do produto na hora da compra (o produto pode ser renomeado depois).</summary>
    public string NomeProduto { get; set; } = string.Empty;
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;
    public int Quantidade { get; set; }
    public long CustoUnitarioCentavos { get; set; }

    public long TotalCentavos => CustoUnitarioCentavos * Quantidade;
}
