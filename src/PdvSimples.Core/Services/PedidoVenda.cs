using PdvSimples.Core.Models;

namespace PdvSimples.Core.Services;

/// <summary>Uma linha do carrinho: qual produto e quantas unidades.</summary>
public record ItemCarrinho(int ProdutoId, int Quantidade);

/// <summary>
/// Tudo o que o PDV entrega ao serviço para finalizar uma venda. Os preços NÃO vêm da tela:
/// o serviço lê do banco, para ninguém conseguir vender por um preço adulterado.
/// </summary>
public record PedidoVenda(
    IReadOnlyList<ItemCarrinho> Itens,
    long DescontoCentavos,
    FormaPagamento FormaPagamento,
    long? ValorRecebidoCentavos = null);
