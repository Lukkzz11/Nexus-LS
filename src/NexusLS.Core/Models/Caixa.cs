namespace NexusLS.Core.Models;

/// <summary>Tipos de movimento de dinheiro que não são venda.</summary>
public enum TipoMovimentoCaixa
{
    Sangria = 1,        // tirou dinheiro da gaveta (ex.: levou ao banco)
    Reforco = 2,        // colocou dinheiro na gaveta (ex.: troco)
    Despesa = 3,        // pagou algo com dinheiro da gaveta
    EntradaAvulsa = 4   // entrou dinheiro que não é venda
}

/// <summary>Um turno de caixa: da abertura (com o troco inicial) até o fechamento. Só existe um aberto por vez.</summary>
public class TurnoCaixa
{
    public int Id { get; set; }
    public DateTime DataAbertura { get; set; }
    public long TrocoInicialCentavos { get; set; }
    public string? Operador { get; set; }

    /// <summary>Nulo enquanto o turno está aberto.</summary>
    public DateTime? DataFechamento { get; set; }

    public bool Aberto => DataFechamento is null;
}

public class MovimentoCaixa
{
    public int Id { get; set; }
    public int TurnoId { get; set; }
    public DateTime DataHora { get; set; }
    public TipoMovimentoCaixa Tipo { get; set; }
    public long ValorCentavos { get; set; }
    public string? Descricao { get; set; }
}

/// <summary>
/// Foto da conferência do caixa. Nunca é alterada: para refazer a conferência cria-se uma nova versão,
/// e a anterior continua no histórico.
/// </summary>
public class FechamentoCaixa
{
    public int Id { get; set; }
    public int TurnoId { get; set; }
    public int Versao { get; set; } = 1;
    public DateTime DataHora { get; set; }
    public string? Operador { get; set; }

    public long EsperadoDinheiroCentavos { get; set; }
    public long ConferidoDinheiroCentavos { get; set; }
    public long EsperadoCartaoCentavos { get; set; }
    public long ConferidoCartaoCentavos { get; set; }
    public long EsperadoPixCentavos { get; set; }
    public long ConferidoPixCentavos { get; set; }

    /// <summary>Obrigatória quando alguma diferença não é zero.</summary>
    public string? Observacao { get; set; }

    public long DiferencaDinheiroCentavos => ConferidoDinheiroCentavos - EsperadoDinheiroCentavos;
    public long DiferencaCartaoCentavos => ConferidoCartaoCentavos - EsperadoCartaoCentavos;
    public long DiferencaPixCentavos => ConferidoPixCentavos - EsperadoPixCentavos;
    public long DiferencaTotalCentavos => DiferencaDinheiroCentavos + DiferencaCartaoCentavos + DiferencaPixCentavos;
}

/// <summary>Dinheiro que um cliente pagou da dívida a prazo. Nunca é apagado.</summary>
public class PagamentoCliente
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public DateTime DataHora { get; set; }
    public long ValorCentavos { get; set; }
    public FormaPagamento Forma { get; set; }
    public string? Observacao { get; set; }
    public string? Operador { get; set; }
}
