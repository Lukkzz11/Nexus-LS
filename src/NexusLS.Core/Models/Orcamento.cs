namespace NexusLS.Core.Models;

/// <summary>Os números ficam gravados no banco: nunca reaproveitar nem mudar um número. "Vencido" não é gravado (vem da data).</summary>
public enum StatusOrcamento
{
    Aberto = 1,
    Aprovado = 2,
    Recusado = 3
}

/// <summary>
/// Um preço passado ao cliente antes de ele fechar. NÃO mexe no estoque, no caixa nem nos Pedidos; só quando é aprovado
/// vira um pedido (<see cref="PedidoId"/>). Nunca é apagado pela tela: marca-se como recusado.
/// Fica só neste computador (não entra na sincronização).
/// </summary>
public class Orcamento
{
    public int Id { get; set; }

    /// <summary>ORC-AAMM-0001, montado com o mês e o número do orçamento.</summary>
    public string Numero { get; set; } = string.Empty;

    public int? ClienteId { get; set; }
    public string ClienteNome { get; set; } = string.Empty;
    public string? ClienteTelefone { get; set; }

    public StatusOrcamento Status { get; set; } = StatusOrcamento.Aberto;

    public DateTime CriadoEm { get; set; }
    public DateTime ValidoAte { get; set; }

    public long DescontoCentavos { get; set; }

    /// <summary>Soma dos itens menos o desconto.</summary>
    public long TotalCentavos { get; set; }

    /// <summary>Condições que o cliente lê no PDF (prazo de entrega, forma de pagamento).</summary>
    public string? Observacao { get; set; }

    public int? PedidoId { get; set; }

    public List<ItemOrcamento> Itens { get; set; } = new();
}

public class ItemOrcamento
{
    public int Id { get; set; }
    public int OrcamentoId { get; set; }

    /// <summary>Nulo quando o item foi digitado livremente (serviço, encomenda).</summary>
    public int? ProdutoId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;
    public int Quantidade { get; set; }
    public long PrecoUnitarioCentavos { get; set; }

    public long TotalCentavos => PrecoUnitarioCentavos * Quantidade;
}
