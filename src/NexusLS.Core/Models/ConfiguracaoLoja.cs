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

    // ----- Dados da empresa (saem no cupom, conforme as opções abaixo) -----
    public string? RazaoSocial { get; set; }
    public string? InscricaoEstadual { get; set; }
    public string? Email { get; set; }
    /// <summary>Cidade e UF, ex.: Caruaru - PE.</summary>
    public string? CidadeUf { get; set; }
    /// <summary>Segmento de atuação (ex.: Confecção e varejo de moda surf). Só cadastro; não sai no cupom.</summary>
    public string? Segmento { get; set; }
    /// <summary>Número do endereço (o logradouro fica em <see cref="Endereco"/>).</summary>
    public string? EnderecoNumero { get; set; }
    public string? Bairro { get; set; }
    public string? Cep { get; set; }
    /// <summary>Logo impresso no topo do cupom: imagem PNG já reduzida pela tela (até 300 KB). Nulo = sem logo.</summary>
    public byte[]? LogoCupom { get; set; }
    /// <summary>Chave Pix da loja. Pode sair no rodapé do cupom.</summary>
    public string? ChavePix { get; set; }

    // ----- O que mostrar no cupom -----
    public bool CupomMostrarRazaoSocial { get; set; } = true;
    public bool CupomMostrarDocumento { get; set; } = true;
    public bool CupomMostrarEndereco { get; set; } = true;
    public bool CupomMostrarTelefone { get; set; } = true;
    public bool CupomMostrarPix { get; set; }
    /// <summary>Cidade e UF no cabeçalho (separado do endereço, como no ERP PDV Life).</summary>
    public bool CupomMostrarCidade { get; set; } = true;
    /// <summary>Inscrição estadual no cabeçalho (separada do CNPJ).</summary>
    public bool CupomMostrarInscricao { get; set; } = true;
    public bool CupomMostrarEmail { get; set; }
    /// <summary>Logo no topo do cupom (só aparece se houver logo enviado).</summary>
    public bool CupomMostrarLogo { get; set; } = true;
    public bool CupomMostrarOperador { get; set; } = true;
    public bool CupomMostrarCliente { get; set; } = true;
    /// <summary>Forma de pagamento, recebido e troco.</summary>
    public bool CupomMostrarPagamento { get; set; } = true;
    /// <summary>Linha "Impresso pelo Nexus LS" no rodapé.</summary>
    public bool CupomMostrarAssinatura { get; set; }
    /// <summary>Referência do produto embaixo do nome de cada item.</summary>
    public bool CupomMostrarReferencia { get; set; }
    /// <summary>"CUPOM NÃO FISCAL" dentro de uma caixa.</summary>
    public bool CupomTituloEmCaixa { get; set; }
    /// <summary>Linha para o cliente assinar (recebimento da mercadoria).</summary>
    public bool CupomMostrarAssinaturaCliente { get; set; }
    /// <summary>Texto de garantia/termos impresso antes da assinatura (opcional).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? CupomTextoGarantia { get; set; }
    /// <summary>Mensagem logo abaixo dos dados da loja, antes do "CUPOM NÃO FISCAL" (opcional, até 200 letras).</summary>
    public string? CupomMensagemCabecalho { get; set; }
    /// <summary>Linhas em branco depois do cupom, para o corte da impressora não pegar o texto (0 a 15).</summary>
    public int CupomLinhasFinais { get; set; } = 1;

    // ----- Parâmetros de operação -----
    /// <summary>Troco inicial que a tela de abertura do caixa já sugere (centavos).</summary>
    public long FundoTrocoPadraoCentavos { get; set; }
    /// <summary>Estoque mínimo sugerido ao cadastrar um produto novo.</summary>
    public int EstoqueMinimoPadrao { get; set; }
    /// <summary>Margem desejada (%) sugerida no cadastro do produto. 0 = em branco.</summary>
    public int MargemPadraoPercentual { get; set; }
}
