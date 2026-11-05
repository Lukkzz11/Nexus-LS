namespace NexusLS.Core.Models;

/// <summary>
/// O que toda entidade que pode ser copiada para a nuvem precisa ter: um identificador global estável (o id do documento
/// remoto, igual em qualquer computador) e o horário da última alteração. O <c>Id</c> inteiro continua existindo só para o SQLite.
/// </summary>
public interface IEntidadeSincronizavel
{
    Guid Guid { get; set; }

    /// <summary>Horário (UTC) da última alteração; mantido pelo contexto ao salvar, nunca pelos serviços.</summary>
    DateTime AtualizadoEm { get; set; }
}

/// <summary>Base das entidades novas na sincronização: já traz <see cref="Guid"/> e <see cref="AtualizadoEm"/>.</summary>
public abstract class EntidadeSincronizavel : IEntidadeSincronizavel
{
    public Guid Guid { get; set; } = Guid.NewGuid();
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Nomes estáveis das entidades na fila (e, no futuro, nas coleções remotas). Não mudam com renomeações de classe.</summary>
public static class NomesEntidade
{
    public const string Produto = "produto";
    public const string Venda = "venda";
    public const string Cliente = "cliente";
    public const string PagamentoCliente = "pagamentocliente";
    public const string TurnoCaixa = "turnocaixa";
    public const string MovimentoCaixa = "movimentocaixa";
    public const string FechamentoCaixa = "fechamentocaixa";
    public const string Lancamento = "lancamento";
    public const string Fornecedor = "fornecedor";
    public const string Compra = "compra";
    public const string Pedido = "pedido";
    public const string MovimentacaoEstoque = "movimentacaoestoque";
    public const string ContaFinanceira = "contafinanceira";
    public const string TransferenciaConta = "transferenciaconta";
    public const string LiquidacaoCartao = "liquidacaocartao";
    public const string ChequeRecebido = "chequerecebido";
    public const string CategoriaFinanceira = "categoriafinanceira";
    public const string AjusteTituloPrazo = "ajustetituloprazo";
    public const string MateriaPrima = "materiaprima";
    public const string OrdemProducao = "ordemproducao";
    public const string PerdaProducao = "perdaproducao";

    /// <summary>O nome da entidade para uma classe (o nome da classe em minúsculas).</summary>
    public static string De(Type tipo) => tipo.Name.ToLowerInvariant();
}
