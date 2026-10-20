namespace NexusLS.Core.Models;

/// <summary>Os números ficam gravados no banco: nunca reaproveitar nem mudar um número.</summary>
public enum OrigemPedido
{
    Balcao = 1,
    WhatsApp = 2,
    Catalogo = 3,
    Encomenda = 4
}

/// <summary>Etapas do pedido, na ordem do quadro. Os números ficam gravados no banco.</summary>
public enum StatusPedido
{
    Novo = 1,
    EmSeparacao = 2,
    Separado = 3,
    ProntoRetirada = 4,
    Entregue = 5,
    Cancelado = 6
}

/// <summary>
/// Um pedido de cliente que passa por etapas (separar, embalar, entregar). NÃO mexe no estoque nem no caixa: é só o
/// acompanhamento. Venda a prazo feita no PDV cria o pedido sozinha (ligado à venda); pedidos de WhatsApp, catálogo e
/// encomenda são lançados na tela Pedidos. Nunca é apagado: cancela-se com motivo.
/// </summary>
public class Pedido
{
    public int Id { get; set; }

    /// <summary>PED-AAMM-0001, montado com o mês e o número do pedido.</summary>
    public string Numero { get; set; } = string.Empty;

    public int? ClienteId { get; set; }

    /// <summary>Nome na hora do pedido (o cliente pode ser renomeado ou nem estar cadastrado).</summary>
    public string ClienteNome { get; set; } = "Consumidor final";
    public string? ClienteTelefone { get; set; }

    public OrigemPedido Origem { get; set; }
    public StatusPedido Status { get; set; } = StatusPedido.Novo;

    public long TotalCentavos { get; set; }
    public string? Observacao { get; set; }

    /// <summary>Venda do PDV que gerou este pedido (nulo se foi lançado à mão).</summary>
    public int? VendaId { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public string? MotivoCancelamento { get; set; }

    public List<ItemPedido> Itens { get; set; } = new();
    public List<EventoPedido> Historico { get; set; } = new();
}

public class ItemPedido
{
    public int Id { get; set; }
    public int PedidoId { get; set; }

    /// <summary>Nulo quando o item foi digitado livremente (sem produto cadastrado).</summary>
    public int? ProdutoId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;
    public int Quantidade { get; set; }
    public long PrecoUnitarioCentavos { get; set; }

    public long TotalCentavos => PrecoUnitarioCentavos * Quantidade;
}

/// <summary>Cada mudança de etapa, com quando e quem fez.</summary>
public class EventoPedido
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public StatusPedido Status { get; set; }
    public DateTime DataHora { get; set; }
    public string? Operador { get; set; }
    public string? Observacao { get; set; }
}
