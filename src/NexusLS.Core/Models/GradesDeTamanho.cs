namespace NexusLS.Core.Models;

/// <summary>Uma grade pronta de tamanhos (o nome que aparece na tela e a lista, na ordem de venda).</summary>
public record GradeDeTamanho(string Nome, IReadOnlyList<string> Tamanhos);

/// <summary>As grades de tamanho usadas pela loja. O operador escolhe uma e cadastra todos os tamanhos de uma vez.</summary>
public static class GradesDeTamanho
{
    public static readonly GradeDeTamanho Letras = new("Letras (PP ao G10)",
        new[] { "PP", "P", "M", "G", "GG", "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "G9", "G10" });

    public static readonly GradeDeTamanho CalcaBermuda = new("Calça e bermuda (34 ao 70)",
        Enumerable.Range(17, 19).Select(n => (n * 2).ToString()).ToArray()); // 34, 36, 38 ... 70

    public static readonly GradeDeTamanho CamisaBotao = new("Camisa de botão (1 ao 10)",
        Enumerable.Range(1, 10).Select(n => n.ToString()).ToArray());

    public static readonly IReadOnlyList<GradeDeTamanho> Todas = new[] { Letras, CalcaBermuda, CamisaBotao };
}
