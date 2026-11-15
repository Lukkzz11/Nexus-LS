namespace NexusLS.Core.Models;

/// <summary>
/// Devolução de peças de uma venda (a troca começa aqui). As peças voltam ao estoque do tamanho de onde saíram e o valor
/// pago por elas vira um <see cref="ValeTroca"/>. A venda original nunca muda: a devolução é um registro à parte, que
/// reduz a receita do período (devolução) e devolve o custo das mercadorias. Nunca é apagada: só se cancela, com motivo,
/// e só enquanto o vale não foi usado.
/// </summary>
public class Devolucao : EntidadeSincronizavel
{
    public int Id { get; set; }
    public int VendaId { get; set; }
    public Venda? Venda { get; set; }

    /// <summary>Cliente da venda original (se havia), copiado para a busca.</summary>
    public int? ClienteId { get; set; }

    public DateTime DataHora { get; set; }
    public string? Operador { get; set; }

    /// <summary>Por que voltou (tamanho errado, defeito, não gostou...). Obrigatório, mínimo 3 letras.</summary>
    public string Motivo { get; set; } = string.Empty;

    /// <summary>Quanto o cliente pagou pelas peças devolvidas (o valor do vale), já com o desconto da venda repartido.</summary>
    public long ValorCentavos { get; set; }

    public bool Cancelada { get; set; }
    public DateTime? DataCancelamento { get; set; }
    public string? MotivoCancelamento { get; set; }
    public string? CanceladoPor { get; set; }

    public List<ItemDevolucao> Itens { get; set; } = new();
}

/// <summary>Uma linha devolvida: de qual item da venda, quantas peças e quanto valem.</summary>
public class ItemDevolucao
{
    public int Id { get; set; }
    public int DevolucaoId { get; set; }
    public int ItemVendaId { get; set; }
    public int ProdutoId { get; set; }
    public string NomeProduto { get; set; } = string.Empty;
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;
    public int Quantidade { get; set; }

    /// <summary>Valor das peças devolvidas desta linha (preço da linha com o desconto da venda repartido).</summary>
    public long ValorCentavos { get; set; }

    /// <summary>Custo unitário que a venda guardou (para devolver o custo das mercadorias vendidas). Nulo em vendas antigas.</summary>
    public long? CustoUnitarioCentavos { get; set; }

    /// <summary>A peça voltou ao estoque? (Falso em produto "vender sem baixa", que nunca saiu do estoque.)</summary>
    public bool DevolveuEstoque { get; set; }
}

/// <summary>
/// Vale-troca: crédito com código (impresso com código de barras) que paga, no todo ou em parte, uma venda no PDV.
/// O saldo nunca é guardado: é o valor do vale menos o que as vendas não canceladas pagaram com ele. Cancelar a venda
/// que usou o vale devolve o saldo sozinho.
/// </summary>
public class ValeTroca : EntidadeSincronizavel
{
    public int Id { get; set; }

    /// <summary>Código impresso no vale (ex.: VT7K3M9Q2A). Único; é o que o PDV lê.</summary>
    public string Codigo { get; set; } = string.Empty;

    public long ValorCentavos { get; set; }

    /// <summary>A devolução que gerou o vale.</summary>
    public int? DevolucaoId { get; set; }
    public int? ClienteId { get; set; }

    public DateTime CriadoEm { get; set; }
    public string? Operador { get; set; }

    /// <summary>Vale cancelado (a devolução foi cancelada) não paga mais nada.</summary>
    public bool Cancelado { get; set; }
}
