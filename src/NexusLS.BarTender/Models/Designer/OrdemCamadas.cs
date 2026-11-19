namespace NexusLS.BarTender.Models.Designer;

public enum OperacaoCamada
{
    TrazerParaFrente,
    EnviarParaTras,
    SubirUmaCamada,
    DescerUmaCamada
}

/// <summary>
/// Ordem de empilhamento (z-order): o índice 0 da lista fica ao fundo e o último fica na frente.
/// Essa mesma ordem é a que a tela, a impressão (ZPL/PNG) e o arquivo da etiqueta usam, por isso
/// etiquetas antigas continuam valendo: a ordem da lista sempre foi a ordem de empilhamento.
/// </summary>
public static class OrdemCamadas
{
    /// <summary>
    /// Devolve a nova ordem da lista depois da operação. Os selecionados mantêm a posição relativa
    /// entre si. Não altera a lista original.
    /// </summary>
    public static List<T> Reordenar<T>(IReadOnlyList<T> lista, Func<T, bool> selecionado, OperacaoCamada operacao)
    {
        var itens = lista.ToList();
        var sel = itens.Select(selecionado).ToList();
        var n = itens.Count;
        if (n < 2 || !sel.Any(s => s)) return itens;

        switch (operacao)
        {
            case OperacaoCamada.TrazerParaFrente:
                return itens.Where((_, i) => !sel[i]).Concat(itens.Where((_, i) => sel[i])).ToList();

            case OperacaoCamada.EnviarParaTras:
                return itens.Where((_, i) => sel[i]).Concat(itens.Where((_, i) => !sel[i])).ToList();

            case OperacaoCamada.SubirUmaCamada:
                // Do topo para baixo: cada selecionado troca com o vizinho da frente que não é selecionado.
                for (var i = n - 2; i >= 0; i--)
                {
                    if (sel[i] && !sel[i + 1])
                    {
                        (itens[i], itens[i + 1]) = (itens[i + 1], itens[i]);
                        (sel[i], sel[i + 1]) = (sel[i + 1], sel[i]);
                    }
                }
                return itens;

            case OperacaoCamada.DescerUmaCamada:
                for (var i = 1; i < n; i++)
                {
                    if (sel[i] && !sel[i - 1])
                    {
                        (itens[i], itens[i - 1]) = (itens[i - 1], itens[i]);
                        (sel[i], sel[i - 1]) = (sel[i - 1], sel[i]);
                    }
                }
                return itens;

            default:
                return itens;
        }
    }

    /// <summary>A operação mudaria alguma coisa? (para não gerar passo de desfazer à toa)</summary>
    public static bool Altera<T>(IReadOnlyList<T> lista, Func<T, bool> selecionado, OperacaoCamada operacao)
    {
        var nova = Reordenar(lista, selecionado, operacao);
        for (var i = 0; i < nova.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(nova[i], lista[i])) return true;
        }
        return false;
    }
}
