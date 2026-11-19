namespace NexusLS.Core.Models;

/// <summary>
/// Preço de um produto definido pelo administrador PARA ESTA LOJA (recebido da nuvem). Fica numa tabela local própria:
/// o preço do cadastro (<see cref="Produto.PrecoVendaCentavos"/>), que sincroniza com o ERP Life, não é tocado.
/// O PDV usa este preço quando ele existe. O produto é identificado pelo <see cref="Produto.Guid"/>.
/// </summary>
public class PrecoLoja
{
    public int Id { get; set; }
    public Guid ProdutoGuid { get; set; }
    public long PrecoVarejoCentavos { get; set; }

    /// <summary>Zero = sem preço de atacado para a loja (vale o do cadastro, como no resto do programa).</summary>
    public long PrecoAtacadoCentavos { get; set; }

    /// <summary>Quando o administrador definiu o preço na nuvem (UTC).</summary>
    public DateTime DefinidoEmUtc { get; set; }

    /// <summary>Quando este computador recebeu o preço (UTC).</summary>
    public DateTime RecebidoEmUtc { get; set; }
}
