namespace NexusLS.Core.Models;

/// <summary>Tipos de ação que ficam na trilha de atividades. Os números ficam gravados no banco: não mude a ordem.</summary>
public enum TipoAtividade
{
    VendaCancelada = 1,
    CompraCancelada = 2,
    LancamentoCancelado = 3,
    PagamentoEstornado = 4,
    PedidoCancelado = 5,
    EstoqueAjustado = 6,
    FuncionarioCadastrado = 7,
    FuncionarioEditado = 8,
    FuncionarioDesativado = 9,
    FuncionarioReativado = 10,
    PinRedefinido = 11,
    DadosApagados = 12,
    TransferenciaCancelada = 13,
    LiquidacaoEstornada = 14,
    ChequeDevolvido = 15,
    ChequeCancelado = 16,
    ContaAlterada = 17
}

/// <summary>
/// Trilha de atividades: quem fez o quê e quando, nas ações que mexem em dinheiro, estoque ou acesso.
/// É gravada na mesma transação da ação (se a ação falha, o registro também não existe) e nunca é alterada nem apagada
/// (nem pelo "Limpar tudo").
/// </summary>
public class Atividade
{
    public int Id { get; set; }
    public DateTime DataHora { get; set; }
    public TipoAtividade Tipo { get; set; }

    /// <summary>Nome de quem fez (funcionário logado ou operador configurado). Vazio se ninguém estiver identificado.</summary>
    public string? Operador { get; set; }

    /// <summary>Frase pronta para mostrar: "Cancelou a venda #12: cliente desistiu".</summary>
    public string Descricao { get; set; } = string.Empty;

    /// <summary>A que se refere, em texto curto ("Venda #12", "Produto Camisa"). Opcional.</summary>
    public string? Referencia { get; set; }
}
