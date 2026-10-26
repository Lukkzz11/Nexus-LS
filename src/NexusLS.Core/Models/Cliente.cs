namespace NexusLS.Core.Models;

public class Cliente : EntidadeSincronizavel
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    /// <summary>CPF ou CNPJ (opcional, guardado como foi digitado).</summary>
    public string? Documento { get; set; }
    public string? Telefone { get; set; }
    public string? Observacao { get; set; }

    /// <summary>Cliente desativado some da busca, mas continua nas vendas antigas.</summary>
    public bool Ativo { get; set; } = true;

    public DateTime DataCadastro { get; set; }

    /// <summary>Quanto o cliente pode dever a prazo ao mesmo tempo. Zero = sem limite.</summary>
    public long LimiteCreditoCentavos { get; set; }
}
