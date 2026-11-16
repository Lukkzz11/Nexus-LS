using NexusLS.Core.Etiquetas;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Se o Nexus abriu o BarTender pedindo etiquetas (<c>--item=ID|TAMANHO|QTD</c>), põe esses itens na Fila e abre a Fila.
    /// Roda uma vez, depois que o catálogo foi carregado.
    /// </summary>
    private void AplicarPedidoDoNexus()
    {
        var pedido = PedidoBarTender.Ler(Environment.GetCommandLineArgs().Skip(1));
        if (pedido.Count == 0) return;

        var naoAchados = 0;
        foreach (var item in pedido)
        {
            var produto = TodosProdutos.FirstOrDefault(p => p.Id == item.ProdutoId)?.Produto;
            if (produto is null) { naoAchados++; continue; }

            var tamanho = EhSemTamanho(item.Tamanho) ? "" : item.Tamanho;
            AdicionarNaFila(produto, tamanho, item.Quantidade);
        }

        AtualizarTotaisFila();
        App.Log($"Pedido do Nexus: {pedido.Count} item(ns), {naoAchados} não encontrado(s) no catálogo.");

        if (FilaImpressao.Count > 0) IrParaFila();

        if (naoAchados > 0)
            _dialogo.Informar($"{naoAchados} produto(s) pedido(s) pelo Nexus não foram encontrados no catálogo (inativos ou removidos).");
    }
}
