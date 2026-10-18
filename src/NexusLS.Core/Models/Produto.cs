namespace NexusLS.Core.Models;

/// <summary>
/// Um produto vendido na loja. Todos os valores em dinheiro ficam em CENTAVOS (long),
/// porque double/decimal-de-ponto-flutuante acumulam erros de arredondamento (R$ 0,10 + R$ 0,20 != R$ 0,30).
/// </summary>
public class Produto
{
    public int Id { get; set; }

    /// <summary>Código de barras (opcional). O "?" significa que pode ser nulo.</summary>
    public string? CodigoBarras { get; set; }

    public string Nome { get; set; } = string.Empty;

    public long PrecoVendaCentavos { get; set; }
    public long PrecoCustoCentavos { get; set; }

    public int EstoqueAtual { get; set; }
    public int EstoqueMinimo { get; set; }

    /// <summary>Produto desativado some do PDV, mas continua no histórico de vendas.</summary>
    public bool Ativo { get; set; } = true;

    public bool EstoqueBaixo => EstoqueAtual < EstoqueMinimo;
}
