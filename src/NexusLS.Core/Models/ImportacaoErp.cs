namespace NexusLS.Core.Models;

/// <summary>
/// Liga um documento do Firebase do ERP ao registro daqui. Importar de novo o mesmo documento atualiza o mesmo registro
/// (nunca cria outro), e o hash do conteúdo evita regravar o que não mudou.
/// </summary>
public class ImportacaoErpItem
{
    public int Id { get; set; }

    /// <summary>Coleção do ERP: produtos, clientes, vendas, caixa_turnos.</summary>
    public string Colecao { get; set; } = "";

    /// <summary>Id do documento no ERP.</summary>
    public string ErpId { get; set; } = "";

    /// <summary>Guid do registro criado aqui (produto, cliente, venda ou turno).</summary>
    public Guid EntidadeGuid { get; set; }

    /// <summary>Retrato do documento na última importação; igual = nada a fazer.</summary>
    public string Hash { get; set; } = "";

    public DateTime ImportadoEm { get; set; }
}

/// <summary>Até onde a leitura do ERP já foi (por coleção e campo de data). Só o que veio depois é lido na próxima vez.</summary>
public class ImportacaoErpCursor
{
    /// <summary>Ex.: "produtos:atualizadoEm", "vendas:criadoEm".</summary>
    public string Chave { get; set; } = "";

    /// <summary>UTC.</summary>
    public DateTime Valor { get; set; }
}
