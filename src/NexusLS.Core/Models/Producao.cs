namespace NexusLS.Core.Models;

/// <summary>Etapas de uma ordem de produção, na ordem em que acontecem. "Concluída" só acontece pela transferência para a loja.</summary>
public enum StatusOrdemProducao
{
    Planejamento = 1,
    Corte = 2,
    Costura = 3,
    Estampa = 4,
    Acabamento = 5,
    Concluida = 6,
    Cancelada = 7
}

public enum MotivoPerda
{
    FalhaNoCorte = 1,
    DefeitoNoTecido = 2,
    ErroNaCostura = 3,
    FalhaNaEstampa = 4,
    SobraDeRolo = 5,
    Outro = 6
}

/// <summary>Insumo da fábrica (tecido, linha, zíper, etiqueta...). A quantidade pode ser fracionada (metros, kg).</summary>
public class MateriaPrima : EntidadeSincronizavel
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    /// <summary>Tipo livre, com sugestões na tela (tecido, aviamento, zíper, linha, etiqueta, elástico, embalagem).</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>"metros", "kg", "un"...</summary>
    public string Unidade { get; set; } = "un";

    public decimal EstoqueAtual { get; set; }
    public decimal EstoqueMinimo { get; set; }

    /// <summary>Custo de uma unidade (metro, kg...), em centavos.</summary>
    public long CustoUnitarioCentavos { get; set; }

    public string? Fornecedor { get; set; }
    public string? Localizacao { get; set; }

    /// <summary>Desativada some da lista, mas continua nas perdas antigas.</summary>
    public bool Ativo { get; set; } = true;

    public bool AbaixoDoMinimo => EstoqueAtual < EstoqueMinimo;
}

/// <summary>Uma ordem de produção (OP): o que a fábrica vai fazer, em que etapa está e a grade planejada.</summary>
public class OrdemProducao : EntidadeSincronizavel
{
    public int Id { get; set; }

    /// <summary>OP-AAAA-NNNN, sequencial por ano.</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Modelo { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public string? Categoria { get; set; }
    public StatusOrdemProducao Status { get; set; } = StatusOrdemProducao.Planejamento;
    public DateTime? PrevisaoTermino { get; set; }
    public string? TecidoUtilizado { get; set; }
    public string? Responsavel { get; set; }
    public long CustoEstimadoPecaCentavos { get; set; }
    public string? Observacoes { get; set; }

    public DateTime CriadoEm { get; set; }

    /// <summary>Preenchidos quando a ordem foi transferida para a loja.</summary>
    public DateTime? TransferidaEm { get; set; }
    public int? ProdutoDestinoId { get; set; }

    public List<OrdemProducaoGrade> Grade { get; set; } = new();

    public int TotalPlanejado => Grade.Sum(g => g.Quantidade);
    public long CustoEstimadoTotalCentavos => CustoEstimadoPecaCentavos * TotalPlanejado;
}

/// <summary>Quantas peças de um tamanho a ordem planeja (e, depois, quantas foram de fato transferidas).</summary>
public class OrdemProducaoGrade
{
    public int Id { get; set; }
    public int OrdemProducaoId { get; set; }
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;
    public int Quantidade { get; set; }
}

/// <summary>Perda ou refugo de produção. Perda nunca é apagada: é a trilha do que se desperdiçou.</summary>
public class PerdaProducao : EntidadeSincronizavel
{
    public int Id { get; set; }
    public DateTime DataHora { get; set; }
    public int? OrdemProducaoId { get; set; }

    /// <summary>Código da OP no momento (fica mesmo se a lista mudar). Vazio = perda geral.</summary>
    public string? OrdemCodigo { get; set; }

    public MotivoPerda Motivo { get; set; }
    public int? MateriaPrimaId { get; set; }
    public string MaterialNome { get; set; } = string.Empty;
    public decimal Quantidade { get; set; }
    public string Unidade { get; set; } = "un";
    public long CustoEstimadoCentavos { get; set; }

    /// <summary>A perda foi tirada do estoque da matéria-prima?</summary>
    public bool BaixouEstoque { get; set; }

    public string? Observacao { get; set; }
    public string? RegistradoPor { get; set; }
}
