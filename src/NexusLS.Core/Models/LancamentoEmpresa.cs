namespace NexusLS.Core.Models;

/// <summary>Os números ficam gravados no banco: nunca reaproveitar nem trocar um número existente.</summary>
public enum TipoMovimentoEmpresa
{
    Entrada = 1,
    Saida = 2
}

/// <summary>
/// Entrada ou saída de dinheiro da empresa lançada à mão (com anexo opcional: foto de nota, PDF, recibo).
/// É um registro SOMENTE LOCAL: não herda de <see cref="EntidadeSincronizavel"/>, não entra na fila do ERP Life e não tem
/// campos do ERP nem hash do ERP. Só vai à nuvem no pacote da loja (botão "Compartilhar com a nuvem"). Não confundir com
/// <see cref="Lancamento"/> (conta a pagar/receber do Financeiro).
/// </summary>
public class LancamentoEmpresa
{
    public int Id { get; set; }

    /// <summary>Identificador estável, usado só como id do documento no pacote da loja. Não é do ERP.</summary>
    public Guid Guid { get; set; } = Guid.NewGuid();

    /// <summary>Só a data (hora zerada).</summary>
    public DateTime Data { get; set; }
    public TipoMovimentoEmpresa Tipo { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public long ValorCentavos { get; set; }
    public FormaPagamento? Forma { get; set; }
    public string? Observacao { get; set; }
    public string? Operador { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public List<AnexoLancamento> Anexos { get; set; } = new();
}

/// <summary>Arquivo guardado na pasta "anexos" ao lado do banco. No banco ficam só o caminho relativo e os dados do arquivo.</summary>
public class AnexoLancamento
{
    public int Id { get; set; }
    public int LancamentoEmpresaId { get; set; }

    /// <summary>Nome do arquivo como o usuário o escolheu (só para mostrar).</summary>
    public string NomeOriginal { get; set; } = string.Empty;

    /// <summary>Caminho dentro da pasta de anexos, com "/" (ex.: 2026/11/3f2a...-nota.pdf). Nunca absoluto.</summary>
    public string CaminhoRelativo { get; set; } = string.Empty;
    public string TipoMime { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }

    /// <summary>SHA-256 do conteúdo, em hexadecimal minúsculo.</summary>
    public string Sha256 { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
}
