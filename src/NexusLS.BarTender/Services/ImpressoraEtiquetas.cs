using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.Core.Etiquetas;

namespace NexusLS.BarTender.Services;

/// <summary>
/// Envio da preparação para a impressora. A implementação real usa o diálogo do Windows
/// (o mesmo do resto do BarTender); os testes usam uma implementação de mentira.
/// </summary>
public interface IImpressoraEtiquetas
{
    bool ImprimirPreparacao(
        IEnumerable<ItemFila> itens,
        ModeloEtiquetaDesign modelo,
        string nomeLoja,
        PerfilMidia midia,
        string? nomeImpressora,
        int primeiraPosicao = 1);
}

/// <summary>Comunicação real com as impressoras instaladas no Windows.</summary>
public sealed class ImpressoraWindowsEtiquetas : IImpressoraEtiquetas
{
    public bool ImprimirPreparacao(
        IEnumerable<ItemFila> itens,
        ModeloEtiquetaDesign modelo,
        string nomeLoja,
        PerfilMidia midia,
        string? nomeImpressora,
        int primeiraPosicao = 1)
    {
        return ImpressaoService.ImprimirComMidia(itens, modelo, nomeLoja, midia, primeiraPosicao, nomeImpressora);
    }
}
