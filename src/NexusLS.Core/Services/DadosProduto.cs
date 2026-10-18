namespace NexusLS.Core.Services;

/// <summary>
/// Dados que o operador preenche no cadastro. É um "record": uma classe curta e imutável,
/// boa para carregar valores de um lugar a outro. O estoque NÃO entra aqui na edição:
/// ele só muda por ajuste, venda ou cancelamento (para sempre deixar rastro).
/// </summary>
public record DadosProduto(
    string Nome,
    string? CodigoBarras,
    long PrecoVendaCentavos,
    long PrecoCustoCentavos,
    int EstoqueMinimo);
