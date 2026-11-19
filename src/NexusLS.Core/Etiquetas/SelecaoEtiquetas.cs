using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Etiquetas;

/// <summary>Uma linha da grade na preparação: o tamanho e quantas etiquetas dele serão impressas.</summary>
public sealed record LinhaSelecaoTamanho(string Tamanho, int Quantidade, int Estoque = 0)
{
    /// <summary>Vazio quando o produto não tem grade.</summary>
    public bool EhSemTamanho => string.IsNullOrWhiteSpace(Tamanho);

    public string TamanhoExibicao => EhSemTamanho ? "Único" : Tamanho.Trim();

    public bool TemQuantidade => Quantidade > 0;
}

/// <summary>
/// O que o operador escolheu imprimir de um produto: os tamanhos e a quantidade de cada um.
/// Não grava nada no banco: é só a seleção da tela, que pode ser revista antes de imprimir.
/// </summary>
public sealed class SelecaoEtiquetas
{
    public Produto Produto { get; }

    public IReadOnlyList<LinhaSelecaoTamanho> Linhas { get; }

    public SelecaoEtiquetas(Produto produto, IReadOnlyList<LinhaSelecaoTamanho> linhas)
    {
        Produto = produto ?? throw new ArgumentNullException(nameof(produto));
        Linhas = linhas ?? throw new ArgumentNullException(nameof(linhas));

        var negativa = Linhas.FirstOrDefault(l => l.Quantidade < 0);
        if (negativa is not null)
            throw new ArgumentOutOfRangeException(nameof(linhas),
                $"A quantidade do tamanho {negativa.TamanhoExibicao} não pode ser negativa.");
    }

    public int Total => Linhas.Sum(l => l.Quantidade);

    public bool Vazia => Total <= 0;

    public IReadOnlyList<LinhaSelecaoTamanho> ComQuantidade =>
        Linhas.Where(l => l.TemQuantidade).ToList();

    public string Nome => Produto.Nome;

    public string Referencia => string.IsNullOrWhiteSpace(Produto.Referencia) ? "Sem referência" : Produto.Referencia!;

    /// <summary>Resumo curto usado nos avisos e na conferência.</summary>
    public string Resumo => Vazia
        ? "Nenhuma etiqueta selecionada"
        : string.Join(" · ", ComQuantidade.Select(l => $"{l.TamanhoExibicao}: {l.Quantidade}"));

    /// <summary>
    /// Monta a seleção a partir do produto. Tamanhos sem quantidade continuam na lista (com zero)
    /// para o operador poder preencher depois. "Sem tamanho" só aparece quando o produto não tem
    /// grade real ou quando tem estoque nele.
    /// </summary>
    public static SelecaoEtiquetas DoProduto(Produto produto, IReadOnlyDictionary<string, int>? quantidades = null)
    {
        if (produto is null) throw new ArgumentNullException(nameof(produto));

        var linhas = new List<LinhaSelecaoTamanho>();
        var tamanhos = (produto.Tamanhos ?? new List<ProdutoTamanho>())
            .Where(t => !string.IsNullOrWhiteSpace(t.Tamanho))
            .OrderBy(t => t.Ordem > 0 ? t.Ordem : int.MaxValue)
            .ThenBy(t => t.Tamanho, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var reais = tamanhos
            .Where(t => !EhSemTamanho(t.Tamanho))
            .GroupBy(t => t.Tamanho.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var t in reais)
        {
            var quantidade = QuantidadeDe(quantidades, t.Tamanho);
            linhas.Add(new LinhaSelecaoTamanho(t.Tamanho.Trim(), quantidade, t.Estoque));
        }

        if (reais.Count == 0)
        {
            var semTamanho = tamanhos.FirstOrDefault(t => EhSemTamanho(t.Tamanho));
            linhas.Add(new LinhaSelecaoTamanho("", QuantidadeDe(quantidades, ""), semTamanho?.Estoque ?? produto.EstoqueAtual));
        }
        else
        {
            var semTamanho = tamanhos.FirstOrDefault(t => EhSemTamanho(t.Tamanho));
            if (semTamanho is { Estoque: > 0 })
                linhas.Add(new LinhaSelecaoTamanho("", QuantidadeDe(quantidades, ""), semTamanho.Estoque));
        }

        if (linhas.Count == 0)
            linhas.Add(new LinhaSelecaoTamanho("", QuantidadeDe(quantidades, ""), produto.EstoqueAtual));

        return new SelecaoEtiquetas(produto, linhas);
    }

    /// <summary>Converte a seleção no formato que o paginador de impressão já usa.</summary>
    public IReadOnlyList<ItemEtiqueta> ParaItens()
    {
        return ComQuantidade.Select(l => new ItemEtiqueta(
            Produto.Id,
            Produto.Nome,
            l.EhSemTamanho ? "" : l.Tamanho.Trim(),
            Produto.PrecoVendaCentavos,
            CodigoBarrasTamanho.Resolver(Produto, l.EhSemTamanho ? null : l.Tamanho),
            l.Quantidade)).ToList();
    }

    /// <summary>Páginas/folhas que a seleção ocupa no formato escolhido.</summary>
    public List<List<PosicaoEtiqueta>> Paginar(FormatoEtiqueta formato, int primeiraPosicao = 1)
    {
        if (Vazia) return new List<List<PosicaoEtiqueta>>();
        return DiagramadorEtiquetas.Paginar(ParaItens(), formato, primeiraPosicao);
    }

    /// <summary>Quantas páginas/folhas a seleção ocupa (0 quando não há etiqueta).</summary>
    public int TotalDePaginas(FormatoEtiqueta formato, int primeiraPosicao = 1) =>
        Vazia ? 0 : Paginar(formato, primeiraPosicao).Count;

    /// <summary>Lê a quantidade do dicionário aceitando "Sem tamanho" e "" como a mesma linha.</summary>
    private static int QuantidadeDe(IReadOnlyDictionary<string, int>? quantidades, string tamanho)
    {
        if (quantidades is null || quantidades.Count == 0) return 0;

        var chave = tamanho?.Trim() ?? "";
        if (quantidades.TryGetValue(chave, out var achou)) return ValidarQuantidade(achou, chave);

        if (EhSemTamanho(chave))
        {
            foreach (var par in quantidades)
                if (EhSemTamanho(par.Key))
                    return ValidarQuantidade(par.Value, par.Key);
        }

        foreach (var par in quantidades)
            if (string.Equals(par.Key?.Trim(), chave, StringComparison.OrdinalIgnoreCase))
                return ValidarQuantidade(par.Value, par.Key ?? "");

        return 0;
    }

    private static int ValidarQuantidade(int quantidade, string tamanho)
    {
        if (quantidade < 0)
            throw new ArgumentOutOfRangeException(nameof(quantidade),
                $"A quantidade do tamanho {tamanho} não pode ser negativa.");
        return quantidade;
    }

    private static bool EhSemTamanho(string? tamanho) =>
        string.IsNullOrWhiteSpace(tamanho) ||
        string.Equals(tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase);
}
