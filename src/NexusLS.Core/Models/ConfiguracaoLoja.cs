namespace NexusLS.Core.Models;

/// <summary>Dados da loja e do cupom. Existe uma única linha (Id = 1).</summary>
public class ConfiguracaoLoja
{
    public int Id { get; set; } = 1;
    public string NomeLoja { get; set; } = "Nexus LS";
    public string? Documento { get; set; }
    public string? Endereco { get; set; }
    public string? Telefone { get; set; }

    /// <summary>Texto no rodapé do cupom, ex.: "Obrigado pela preferência!"</summary>
    public string? MensagemCupom { get; set; }

    /// <summary>Nome da impressora do Windows que imprime o cupom. Vazio = a impressora padrão do Windows.</summary>
    public string? ImpressoraCupom { get; set; }

    /// <summary>Quantos caracteres cabem numa linha do cupom (32 = bobina de 58 mm, 42 ou 48 = bobina de 80 mm).</summary>
    public int LarguraCupom { get; set; } = 42;

    /// <summary>Imprime o cupom sozinho quando a venda é finalizada.</summary>
    public bool ImprimirCupomAoFinalizar { get; set; } = true;
}
