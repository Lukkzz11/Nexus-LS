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
    /// Monta a seleção a partir do produto. Todos os tamanhos cadastrados entram na lista (com zero),
    /// mesmo sem estoque: esta tela é de produção, e a etiqueta pode ser impressa antes de o saldo existir.
    /// Se o produto de vestuário veio do banco com apenas 1 tamanho (ex.: só 42) ou só "Sem tamanho",
    /// completa a grade padrão do modelo para o operador poder clicar nas quantidades de todos os tamanhos.
    /// </summary>
    public static SelecaoEtiquetas DoProduto(Produto produto, IReadOnlyDictionary<string, int>? quantidades = null)
    {
        if (produto is null) throw new ArgumentNullException(nameof(produto));

        GarantirGradeDoModelo(produto);

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

        if (quantidades is not null)
        {
            foreach (var par in quantidades)
            {
                var chave = par.Key?.Trim() ?? "";
                if (EhSemTamanho(chave)) continue;
                if (linhas.Any(l => string.Equals(l.Tamanho, chave, StringComparison.OrdinalIgnoreCase))) continue;

                linhas.Add(new LinhaSelecaoTamanho(chave, ValidarQuantidade(par.Value, chave), 0));
            }
        }

        var semTamanho = tamanhos.FirstOrDefault(t => EhSemTamanho(t.Tamanho));
        if (semTamanho is not null || reais.Count == 0)
            linhas.Add(new LinhaSelecaoTamanho("", QuantidadeDe(quantidades, ""), semTamanho?.Estoque ?? produto.EstoqueAtual));

        if (linhas.Count == 0)
            linhas.Add(new LinhaSelecaoTamanho("", QuantidadeDe(quantidades, ""), produto.EstoqueAtual));

        return new SelecaoEtiquetas(produto, linhas);
    }

    /// <summary>
    /// Completa em memória os tamanhos padrão do modelo quando um produto de vestuário possui
    /// no máximo 1 tamanho real cadastrado no banco (ex.: só "42" ou apenas "Sem tamanho").
    /// Preserva estoque e ordem dos tamanhos já existentes.
    /// </summary>
    public static void GarantirGradeDoModelo(Produto? produto)
    {
        if (produto is null) return;

        var lista = produto.Tamanhos ?? new List<ProdutoTamanho>();
        var reais = lista
            .Where(t => !EhSemTamanho(t.Tamanho))
            .Select(t => t.Tamanho.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reais.Count > 1) return;

        var sugeridos = InferirGradePadrao(produto, reais);
        if (sugeridos.Count <= 1) return;

        produto.Tamanhos ??= new List<ProdutoTamanho>();

        var existentes = produto.Tamanhos
            .Where(t => !EhSemTamanho(t.Tamanho))
            .ToDictionary(t => t.Tamanho.Trim(), StringComparer.OrdinalIgnoreCase);

        var semTamanho = produto.Tamanhos.FirstOrDefault(t => EhSemTamanho(t.Tamanho));
        var novaLista = new List<ProdutoTamanho>();
        if (semTamanho is not null)
            novaLista.Add(semTamanho);

        var ordem = 1;
        foreach (var tam in sugeridos)
        {
            if (existentes.TryGetValue(tam, out var existente))
            {
                if (existente.Ordem <= 0) existente.Ordem = ordem;
                novaLista.Add(existente);
            }
            else
            {
                novaLista.Add(new ProdutoTamanho
                {
                    ProdutoId = produto.Id,
                    Tamanho = tam,
                    Ordem = ordem,
                    Estoque = 0
                });
            }
            ordem++;
        }

        produto.Tamanhos = novaLista;
    }

    public static IReadOnlyList<string> InferirGradePadrao(Produto produto, IReadOnlyList<string>? tamanhosReais = null)
    {
        var reais = tamanhosReais ?? (produto.Tamanhos ?? new List<ProdutoTamanho>())
            .Where(t => !EhSemTamanho(t.Tamanho))
            .Select(t => t.Tamanho.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var categoria = (produto.Categoria ?? "").Trim();
        var nome = (produto.Nome ?? "").Trim();
        var unico = reais.Count == 1 ? reais[0].Trim() : "";

        // Se só tem 1 tamanho numérico (ex.: "42" na Bermuda Cargo), expande a grade numérica correspondente
        if (int.TryParse(unico, out var numUnico) && numUnico >= 34 && numUnico <= 70 && numUnico % 2 == 0)
        {
            if (numUnico >= 52)
                return new[] { "52", "54", "56" };
            if (numUnico < 42)
                return new[] { "36", "38", "40", "42", "44", "46", "48", "50" };
            return new[] { "42", "44", "46", "48", "50" };
        }

        // Sem categoria definida (como produtos avulsos/testes sintéticos), não inventa grade
        if (string.IsNullOrWhiteSpace(categoria))
            return Array.Empty<string>();

        var ehExtra = nome.Contains("EXTRA", StringComparison.OrdinalIgnoreCase) ||
                      categoria.Contains("EXTRA", StringComparison.OrdinalIgnoreCase);
        var ehSuper = nome.Contains("SUPER", StringComparison.OrdinalIgnoreCase) ||
                      categoria.Contains("SUPER", StringComparison.OrdinalIgnoreCase);

        if (categoria.Contains("Bermuda", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Calça", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Calca", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Jeans", StringComparison.OrdinalIgnoreCase))
        {
            if (ehExtra)
                return new[] { "52", "54", "56" };
            return new[] { "42", "44", "46", "48", "50" };
        }

        if (categoria.Contains("Camisa", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Camiseta", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Polo", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Short", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Casaco", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Blusa", StringComparison.OrdinalIgnoreCase) ||
            categoria.Contains("Moletom", StringComparison.OrdinalIgnoreCase))
        {
            if (ehSuper)
                return new[] { "G7", "G8", "G9", "G10" };
            if (ehExtra)
                return new[] { "G1", "G2", "G3", "G4", "G5", "G6" };
            return new[] { "P", "M", "G", "GG", "EXG" };
        }

        return Array.Empty<string>();
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
