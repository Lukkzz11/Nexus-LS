using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Uma linha do carrinho: produto, tamanho e quantidade.
/// Preço: normalmente vem do cadastro (varejo ou atacado, conforme a tabela da venda). O operador pode
/// trocá-lo NA LINHA informando o preço de uma unidade OU o total da linha (nunca os dois).
/// </summary>
/// <param name="Tamanho">Nulo ou vazio = "Sem tamanho".</param>
public record ItemCarrinho(
    int ProdutoId,
    int Quantidade,
    string? Tamanho = null,
    long? PrecoUnitarioCentavos = null,
    long? TotalLinhaCentavos = null);

/// <summary>
/// Tudo o que o PDV entrega ao serviço para finalizar uma venda. Sem preço editado na linha, os preços
/// NÃO vêm da tela: o serviço lê do banco.
/// </summary>
/// <param name="DescontoCentavos">Desconto em R$.</param>
/// <param name="DescontoPercentual">Desconto em % (0 a 100). Use este OU o desconto em R$.</param>
/// <param name="Parcelas">1 a 4, só no cartão de crédito.</param>
/// <param name="ClienteId">Obrigatório na venda a prazo; opcional nas demais.</param>
/// <param name="ChaveUnica">Código desta tentativa de venda. Se a mesma chave chegar de novo, o serviço devolve a venda
/// já gravada em vez de criar outra (protege contra Enter repetido, travamento e reenvio).</param>
public record PedidoVenda(
    IReadOnlyList<ItemCarrinho> Itens,
    long DescontoCentavos,
    FormaPagamento FormaPagamento,
    long? ValorRecebidoCentavos = null,
    TabelaPreco TabelaPreco = TabelaPreco.Varejo,
    decimal? DescontoPercentual = null,
    int Parcelas = 1,
    int? ClienteId = null,
    Guid? ChaveUnica = null);
