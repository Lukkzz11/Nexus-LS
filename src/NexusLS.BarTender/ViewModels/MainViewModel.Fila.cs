using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    public void AdicionarSelecionadoFila()
    {
        if (ProdutoSelecionado == null) return;

        foreach (var tamanho in TamanhosParaAdicionar(ProdutoSelecionado))
            AdicionarNaFila(ProdutoSelecionado.Produto, tamanho, 1);

        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void AdicionarTodosFila()
    {
        var filtrados = ProdutosView.Cast<ProdutoItemViewModel>().ToList();
        if (filtrados.Count == 0) return;

        foreach (var p in filtrados)
            foreach (var tamanho in TamanhosParaAdicionar(p))
                AdicionarNaFila(p.Produto, tamanho, 1);

        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void PuxarEstoqueAtual()
    {
        var filtrados = ProdutosView.Cast<ProdutoItemViewModel>().ToList();
        if (filtrados.Count == 0) return;

        var linhas = 0;

        foreach (var p in filtrados)
        {
            var tamanhos = TamanhosParaAdicionar(p);
            var porTamanho = TemGrade(p.Produto) &&
                string.Equals(p.TamanhoSelecionado, ProdutoItemViewModel.TodosOsTamanhos, StringComparison.OrdinalIgnoreCase);
            var adicionou = 0;

            // Numa puxada da grade inteira, a fila do produto passa a refletir só os tamanhos com estoque.
            if (porTamanho)
            {
                var permitidos = new HashSet<string>(tamanhos, StringComparer.OrdinalIgnoreCase);
                foreach (var existente in FilaImpressao.Where(i => i.Produto.Id == p.Id).ToList())
                    if (!permitidos.Contains(existente.Tamanho ?? ""))
                        FilaImpressao.Remove(existente);
            }

            foreach (var tamanho in tamanhos)
            {
                var cadastrado = LocalizarTamanho(p.Produto, tamanho);
                var estoque = cadastrado?.Estoque ?? p.EstoqueAtual;

                // Numa grade inteira, tamanho zerado não gera etiqueta.
                if (porTamanho && estoque <= 0) continue;

                DefinirNaFila(p.Produto, tamanho, Math.Max(1, estoque));
                adicionou++;
                linhas++;
            }

            // Produto sem grade continua com o comportamento antigo: nunca fica zerado na fila.
            if (adicionou == 0 && !porTamanho)
            {
                DefinirNaFila(p.Produto, "", 1);
                linhas++;
            }
        }

        AtualizarTotaisFila();

        _dialogo.Sucesso($"Fila atualizada com base no estoque: {linhas} linha(s), totalizando {TotalEtiquetasFila} etiquetas.");
    }

    [RelayCommand]
    public void IncrementarItem(ItemFila? item)
    {
        if (item == null) return;
        item.Quantidade++;
        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void DecrementarItem(ItemFila? item)
    {
        if (item == null) return;
        if (item.Quantidade > 1)
        {
            item.Quantidade--;
            AtualizarTotaisFila();
        }
        else
        {
            RemoverItemFila(item);
        }
    }

    [RelayCommand]
    public void RemoverItemFila(ItemFila? item)
    {
        if (item == null) return;
        FilaImpressao.Remove(item);
        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void LimparFila()
    {
        if (FilaImpressao.Count == 0) return;

        var confirmacao = _dialogo.Confirmar(
            "Deseja limpar todos os itens da fila de impressão?",
            "Limpar Fila",
            "Sim, Limpar",
            "Cancelar");

        if (confirmacao)
        {
            FilaImpressao.Clear();
            AtualizarTotaisFila();
        }
    }

    partial void OnUsarModeloDesignerChanged(bool value)
    {
        AtualizarMiniaturasFila();
    }

    private void AtualizarTotaisFila()
    {
        TotalEtiquetasFila = FilaImpressao.Sum(i => i.Quantidade);
        TemItensNaFila = FilaImpressao.Count > 0;
        var porPagina = Config.Formato.FormatoCore.EtiquetasPorPagina;
        TotalPaginasFila = porPagina > 0
            ? (int)Math.Ceiling((double)TotalEtiquetasFila / porPagina)
            : TotalEtiquetasFila;
    }

    public void AtualizarMiniaturasFila()
    {
        var modelo = UsarModeloDesigner ? DesignerVM.Modelo : null;
        foreach (var item in FilaImpressao)
        {
            item.Miniatura = EditorDesenhoService.GerarMiniatura(item.Produto, item.Tamanho, modelo, Config);
        }
    }

    private void AtualizarMiniaturaItem(ItemFila item)
    {
        var modelo = UsarModeloDesigner ? DesignerVM.Modelo : null;
        item.Miniatura = EditorDesenhoService.GerarMiniatura(item.Produto, item.Tamanho, modelo, Config);
    }

    [RelayCommand]
    public void AdicionarItemComQuantidadeFila(ProdutoItemViewModel? produtoItem)
    {
        if (produtoItem == null) return;
        var qtd = Math.Max(1, produtoItem.QuantidadeAdicionar);

        foreach (var tamanho in TamanhosParaAdicionar(produtoItem))
            AdicionarNaFila(produtoItem.Produto, tamanho, qtd);

        AtualizarTotaisFila();
    }

    /// <summary>Produto tem pelo menos um tamanho de verdade (P, M, G, 38...), além de "Sem tamanho".</summary>
    private static bool TemGrade(Produto produto) =>
        produto.Tamanhos?.Any(t => !EhSemTamanho(t.Tamanho)) == true;

    private static bool EhSemTamanho(string? tamanho) =>
        string.IsNullOrWhiteSpace(tamanho) ||
        string.Equals(tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase);

    /// <summary>Tamanhos reais da grade, na ordem cadastrada (a mesma que forma o código de barras).</summary>
    private static List<string> TamanhosDaGrade(Produto produto)
    {
        var lista = (produto.Tamanhos ?? new List<ProdutoTamanho>())
            .Where(t => !EhSemTamanho(t.Tamanho))
            .OrderBy(t => t.Ordem > 0 ? t.Ordem : int.MaxValue)
            .ThenBy(t => t.Tamanho)
            .Select(t => t.Tamanho.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Se o produto também tem estoque "Sem tamanho", ele entra no fim (código sem número de tamanho).
        if (produto.Tamanhos?.Any(t => EhSemTamanho(t.Tamanho) && t.Estoque > 0) == true)
            lista.Add("");

        return lista;
    }

    /// <summary>
    /// O que deve entrar na fila para este produto: o tamanho escolhido na tela ou, quando está em
    /// "Todos os tamanhos", um item por tamanho da grade.
    /// </summary>
    private static IReadOnlyList<string> TamanhosParaAdicionar(ProdutoItemViewModel item)
    {
        var escolhido = item.TamanhoSelecionado?.Trim() ?? "";
        if (escolhido.Length > 0 &&
            !string.Equals(escolhido, ProdutoItemViewModel.TodosOsTamanhos, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { EhSemTamanho(escolhido) ? "" : escolhido };
        }

        var grade = TamanhosDaGrade(item.Produto);
        return grade.Count > 0 ? grade : new[] { "" };
    }

    /// <summary>Linha da grade que corresponde ao tamanho ("" e "Sem tamanho" são a mesma linha).</summary>
    private static ProdutoTamanho? LocalizarTamanho(Produto produto, string tamanho) =>
        produto.Tamanhos?.FirstOrDefault(t =>
            EhSemTamanho(tamanho) ? EhSemTamanho(t.Tamanho)
                : string.Equals(t.Tamanho?.Trim(), tamanho.Trim(), StringComparison.OrdinalIgnoreCase));

    private ItemFila? LocalizarNaFila(Produto produto, string tamanho) =>
        FilaImpressao.FirstOrDefault(i =>
            i.Produto.Id == produto.Id &&
            string.Equals(i.Tamanho ?? "", tamanho ?? "", StringComparison.OrdinalIgnoreCase));

    private ItemFila AdicionarNaFila(Produto produto, string tamanho, int quantidade)
    {
        var existente = LocalizarNaFila(produto, tamanho);
        if (existente != null)
        {
            existente.Quantidade += Math.Max(1, quantidade);
            return existente;
        }

        var novo = new ItemFila(produto, quantidade, tamanho);
        AtualizarMiniaturaItem(novo);
        FilaImpressao.Add(novo);
        return novo;
    }

    private void DefinirNaFila(Produto produto, string tamanho, int quantidade)
    {
        var existente = LocalizarNaFila(produto, tamanho);
        if (existente != null)
            existente.Quantidade = Math.Max(1, quantidade);
        else
        {
            var item = new ItemFila(produto, quantidade, tamanho);
            AtualizarMiniaturaItem(item);
            FilaImpressao.Add(item);
        }
    }
}
