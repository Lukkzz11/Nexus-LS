namespace NexusLS.Core.Models;

/// <summary>
/// Um produto vendido na loja. Todos os valores em dinheiro ficam em CENTAVOS (long),
/// porque double/decimal-de-ponto-flutuante acumulam erros de arredondamento (R$ 0,10 + R$ 0,20 != R$ 0,30).
/// </summary>
public class Produto : IEntidadeSincronizavel
{
    public int Id { get; set; }

    /// <summary>
    /// Identificador global e permanente do produto. O <see cref="Id"/> inteiro só vale neste banco; este é o que
    /// identifica o produto fora dele (por exemplo, como id do documento no Firestore), e por ser único e fixo
    /// reenviar o mesmo produto nunca cria um duplicado. Nunca muda depois de criado.
    /// </summary>
    public Guid Guid { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Quando o produto (ou o estoque de algum tamanho dele) foi alterado pela última vez, em UTC. É só informativo:
    /// quem decide o que mudou na sincronização é o hash do conteúdo, que não depende do relógio do computador.
    /// </summary>
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>Código de barras (opcional). O "?" significa que pode ser nulo.</summary>
    public string? CodigoBarras { get; set; }

    /// <summary>Referência interna do produto (opcional), também usada na busca.</summary>
    public string? Referencia { get; set; }

    public string Nome { get; set; } = string.Empty;

    /// <summary>Código interno automático (INT-000001), gerado pelo sistema e sempre único.</summary>
    public string? CodigoInterno { get; set; }

    /// <summary>Grupo do produto (ex.: Camisetas, Bonés), usado nos relatórios.</summary>
    public string? Categoria { get; set; }

    /// <summary>Cores do produto, separadas por vírgula ("Preto, Branco"). Só informativas: o estoque é por tamanho.</summary>
    public string? Cores { get; set; }

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

    /// <summary>
    /// "Vender sem baixa": o produto é vendido sem conferir nem baixar estoque (serviço, encomenda, peça sob medida).
    /// </summary>
    public bool VenderSemBaixa { get; set; }

    /// <summary>
    /// "Sobra da loja": peça que sobrou. É estoque de verdade (vende no PDV e entra nos totais); a marca só serve para
    /// separá-la na lista do Estoque. Não é publicada no catálogo online nem entra no hash dele.
    /// </summary>
    public bool Sobra { get; set; }

    /// <summary>Quantidade "infinita" mostrada no PDV para produto sem controle de estoque.</summary>
    public const int EstoqueVirtual = 99_999;

    /// <summary>Produto desativado some do PDV, mas continua no histórico de vendas.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Nome do arquivo da foto na pasta de fotos desta instalação (ex.: guid-data.jpg). Nulo = sem foto.</summary>
    public string? FotoArquivo { get; set; }

    /// <summary>Endereço público da foto já enviada ao Firebase Storage. Nulo = ainda não enviada (ou sem foto).</summary>
    public string? FotoUrl { get; set; }

    /// <summary>Grade de tamanhos, cada um com seu próprio estoque.</summary>
    public List<ProdutoTamanho> Tamanhos { get; set; } = new();

    public bool EstoqueBaixo => EstoqueAtual < EstoqueMinimo;

    /// <summary>Preço unitário na tabela escolhida (atacado sem preço definido cai no varejo).</summary>
    public long PrecoPara(TabelaPreco tabela) =>
        tabela == TabelaPreco.Atacado && PrecoAtacadoCentavos > 0 ? PrecoAtacadoCentavos : PrecoVendaCentavos;
}
