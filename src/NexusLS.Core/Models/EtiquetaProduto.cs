namespace NexusLS.Core.Models;

/// <summary>
/// Modelo de etiqueta exclusivo de um produto, criado no Nexus BarTender. Fica no banco (e não em arquivo solto)
/// para entrar no backup do Nexus. O conteúdo é o JSON do modelo, que só o BarTender sabe ler.
/// </summary>
public class EtiquetaProduto
{
    public int Id { get; set; }

    /// <summary>Id do produto dono da etiqueta. Um produto tem no máximo um modelo exclusivo.</summary>
    public int ProdutoId { get; set; }

    public string Nome { get; set; } = "";
    public double LarguraMm { get; set; }
    public double AlturaMm { get; set; }

    /// <summary>Modelo completo em JSON.</summary>
    public string Json { get; set; } = "";

    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
