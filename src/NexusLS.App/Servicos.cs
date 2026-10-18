using NexusLS.Core.Data;
using NexusLS.Core.Services;

namespace NexusLS.App;

/// <summary>
/// Ponto único onde os serviços são criados. Existem frameworks de "injeção de dependência"
/// para isso, mas num app pequeno como este uma classe simples já resolve.
/// </summary>
public static class Servicos
{
    private static readonly FabricaContexto Fabrica = new(App.OpcoesBanco);

    public static ProdutoService Produtos { get; } = new(Fabrica);
    public static EstoqueService Estoque { get; } = new(Fabrica);
    public static VendaService Vendas { get; } = new(Fabrica);
}
