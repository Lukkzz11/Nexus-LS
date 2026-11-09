namespace NexusLS.Core.Services;

/// <summary>
/// Dados que o operador preenche no cadastro. É um "record": uma classe curta e imutável,
/// boa para carregar valores de um lugar a outro. O estoque NÃO entra aqui na edição:
/// ele só muda por ajuste, venda ou cancelamento (para sempre deixar rastro).
/// Referência e preço de atacado são opcionais (por isso têm valor padrão).
/// </summary>
public record DadosProduto(
    string Nome,
    string? CodigoBarras,
    long PrecoVendaCentavos,
    long PrecoCustoCentavos,
    int EstoqueMinimo,
    string? Referencia = null,
    long PrecoAtacadoCentavos = 0,
    string? Categoria = null,
    bool VenderSemBaixa = false,
    bool Sobra = false,
    string? Cores = null);
