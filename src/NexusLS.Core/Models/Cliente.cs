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
