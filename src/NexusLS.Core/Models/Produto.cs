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

    /// <summary>Referência interna do produto (opcional), também usada na busca.</summary>
    public string? Referencia { get; set; }

    public string Nome { get; set; } = string.Empty;

    /// <summary>Preço de varejo.</summary>
    public long PrecoVendaCentavos { get; set; }

    /// <summary>Preço de atacado. Zero significa "não definido": nesse caso vale o preço de varejo.</summary>
    public long PrecoAtacadoCentavos { get; set; }

    public long PrecoCustoCentavos { get; set; }

    /// <summary>
    /// Estoque TOTAL do produto = soma do estoque de todos os tamanhos (inclusive "Sem tamanho").
    /// É mantido pelos serviços na mesma transação em que o estoque de um tamanho muda.
    /// </summary>
    public int EstoqueAtual { get; set; }

    public int EstoqueMinimo { get; set; }

    /// <summary>Produto desativado some do PDV, mas continua no histórico de vendas.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Grade de tamanhos, cada um com seu próprio estoque.</summary>
    public List<ProdutoTamanho> Tamanhos { get; set; } = new();

    public bool EstoqueBaixo => EstoqueAtual < EstoqueMinimo;

    /// <summary>Preço unitário na tabela escolhida (atacado sem preço definido cai no varejo).</summary>
    public long PrecoPara(TabelaPreco tabela) =>
        tabela == TabelaPreco.Atacado && PrecoAtacadoCentavos > 0 ? PrecoAtacadoCentavos : PrecoVendaCentavos;
}
