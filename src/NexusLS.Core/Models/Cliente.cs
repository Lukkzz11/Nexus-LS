using NexusLS.Core.Services;

namespace NexusLS.Core.Models;

public class Cliente : EntidadeSincronizavel
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    /// <summary>Razão social (para cliente com CNPJ). Opcional.</summary>
    public string? RazaoSocial { get; set; }

    /// <summary>
    /// CPF ou CNPJ. Cadastros novos guardam só os dígitos (já conferidos); cadastros antigos podem ter vindo com máscara
    /// ou sem documento.
    /// </summary>
    public string? Documento { get; set; }
    public string? Telefone { get; set; }

    /// <summary>E-mail de contato. Opcional.</summary>
    public string? Email { get; set; }
    public string? Observacao { get; set; }

    /// <summary>Inscrição estadual (cliente com CNPJ). Opcional.</summary>
    public string? InscricaoEstadual { get; set; }

    // Endereço (tudo opcional)
    public string? Cep { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }

    /// <summary>Sigla do estado (UF), ex.: "SP".</summary>
    public string? Estado { get; set; }

    /// <summary>Endereço em uma linha: "Rua X, 10 - Centro, Cidade/UF". Vazio se nada foi preenchido.</summary>
    public string EnderecoCompleto
    {
        get
        {
            var rua = string.Join(", ", new[] { Endereco, Numero }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var cidadeUf = string.Join("/", new[] { Cidade, Estado }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var partes = new[] { rua, Complemento, Bairro, cidadeUf, Cep }.Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join(" - ", partes);
        }
    }

    /// <summary>Cliente desativado some da busca, mas continua nas vendas antigas.</summary>
    public bool Ativo { get; set; } = true;

    public DateTime DataCadastro { get; set; }

    /// <summary>Quanto o cliente pode dever a prazo ao mesmo tempo. Zero = sem limite.</summary>
    public long LimiteCreditoCentavos { get; set; }

    /// <summary>
    /// Crédito do cliente: saldo (em centavos) que ele tem a favor na loja. Só muda por ajuste com motivo
    /// (<see cref="AjusteCreditoCliente"/>), que fica no histórico e na trilha de atividades.
    /// </summary>
    public long CreditoCentavos { get; set; }

    /// <summary>Documento com a máscara de CPF/CNPJ, para mostrar na tela.</summary>
    public string DocumentoFormatado => ValidadorDocumento.Formatar(Documento);
}
