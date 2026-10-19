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

    /// <summary>Nome de quem está no caixa. Aparece nas vendas, nos cancelamentos e nos relatórios.</summary>
    public string NomeOperador { get; set; } = "Caixa";

    /// <summary>
    /// Ligado (padrão): o PDV sempre abre a janela de tamanho. Desligado ("modo rápido"): produto com uma única
    /// opção de tamanho (por exemplo, só "Sem tamanho") vai direto para a quantidade.
    /// </summary>
    public bool PerguntarTamanho { get; set; } = true;

    /// <summary>Imprime o cupom sozinho quando a venda é finalizada.</summary>
    public bool ImprimirCupomAoFinalizar { get; set; } = true;

    /// <summary>
    /// Desligado (padrão): o programa abre na tela Início (indicadores e atalhos) e o PDV fica a um clique ou
    /// ao apertar F1. Ligado: abre direto no PDV, como nas versões antigas.
    /// </summary>
    public bool AbrirNoPdv { get; set; }
}
