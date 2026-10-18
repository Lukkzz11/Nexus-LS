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

    public bool EhSemTamanho => Tamanho == Tamanhos.SemTamanho;
}
