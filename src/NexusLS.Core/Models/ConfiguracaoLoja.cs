namespace NexusLS.Core.Models;

/// <summary>Dados da loja que aparecem no cupom. Existe uma única linha (Id = 1).</summary>
public class ConfiguracaoLoja
{
    public int Id { get; set; } = 1;
    public string NomeLoja { get; set; } = "Nexus LS";
    public string? Documento { get; set; }
    public string? Endereco { get; set; }
    public string? Telefone { get; set; }

    /// <summary>Texto no rodapé do cupom, ex.: "Obrigado pela preferência!"</summary>
    public string? MensagemCupom { get; set; }
}
