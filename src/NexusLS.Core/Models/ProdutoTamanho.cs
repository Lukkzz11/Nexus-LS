namespace NexusLS.Core.Models;

/// <summary>Nomes reservados da grade de tamanhos.</summary>
public static class Tamanhos
{
    /// <summary>
    /// Todo produto tem sempre esta linha na grade: é onde fica o estoque que não pertence a nenhum
    /// tamanho (produtos sem grade, ou a opção "Sem tamanho" do atacado).
    /// </summary>
    public const string SemTamanho = "Sem tamanho";
}

/// <summary>Um tamanho do produto (P, M, G, 38, 40...) com o estoque dele.</summary>
public class ProdutoTamanho
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;
    public int Estoque { get; set; }

    /// <summary>
    /// Número fixo do tamanho dentro do produto (1, 2, 3... na ordem em que foi cadastrado; "Sem tamanho" fica 0). Nunca é
    /// reaproveitado: é ele que forma o código de barras do tamanho (referência + número), então não pode mudar se um tamanho sair.
    /// </summary>
    public int Ordem { get; set; }

    /// <summary>Código de barras deste tamanho (código da referência + <see cref="Ordem"/>). Calculado, não fica no banco.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? CodigoBarras { get; set; }

    public bool EhSemTamanho => Tamanho == Tamanhos.SemTamanho;
}
