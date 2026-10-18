using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Uma linha do carrinho: um produto num tamanho. O preço vem da tabela (varejo/atacado), mas o operador
/// pode trocá-lo: ou o preço de UMA unidade, ou o total da linha. Nunca os dois ao mesmo tempo.
/// </summary>
public class LinhaCarrinho
{
    public int ProdutoId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string Tamanho { get; init; } = Tamanhos.SemTamanho;

    public long PrecoVarejoCentavos { get; init; }

    /// <summary>Zero = produto sem preço de atacado (vale o de varejo).</summary>
    public long PrecoAtacadoCentavos { get; init; }

    /// <summary>Estoque deste tamanho quando o produto foi buscado; limita a quantidade no carrinho.</summary>
    public int EstoqueDisponivel { get; internal set; }

    public int Quantidade { get; internal set; }
    public TabelaPreco Tabela { get; internal set; } = TabelaPreco.Varejo;

    public long? PrecoUnitarioEditadoCentavos { get; internal set; }
    public long? TotalEditadoCentavos { get; internal set; }

    public bool PrecoEditado => PrecoUnitarioEditadoCentavos is not null || TotalEditadoCentavos is not null;

    /// <summary>Preço de tabela (sem edição) na tabela atual.</summary>
    public long PrecoTabelaCentavos =>
        Tabela == TabelaPreco.Atacado && PrecoAtacadoCentavos > 0 ? PrecoAtacadoCentavos : PrecoVarejoCentavos;

    /// <summary>Preço de uma unidade. Com total editado, é o total dividido pela quantidade (arredondado).</summary>
    public long PrecoUnitarioCentavos =>
        TotalEditadoCentavos is long total
            ? (long)Math.Round(total / (decimal)Quantidade, MidpointRounding.AwayFromZero)
            : PrecoUnitarioEditadoCentavos ?? PrecoTabelaCentavos;

    public long TotalCentavos => TotalEditadoCentavos ?? PrecoUnitarioCentavos * Quantidade;
}

/// <summary>
/// O carrinho da venda em andamento. É uma classe do Core (sem janela), então todas as regras dele —
/// estoque por tamanho, preço editado, tabela de preço — são testadas sem abrir tela.
/// </summary>
public class Carrinho
{
    private readonly List<LinhaCarrinho> _linhas = new();

    public IReadOnlyList<LinhaCarrinho> Linhas => _linhas;
    public TabelaPreco Tabela { get; private set; } = TabelaPreco.Varejo;

    public bool Vazio => _linhas.Count == 0;
    public int QuantidadeTotal => _linhas.Sum(l => l.Quantidade);
    public long SubtotalCentavos => _linhas.Sum(l => l.TotalCentavos);

    /// <summary>
    /// Adiciona unidades de um produto/tamanho. Se já existe uma linha desse produto e tamanho sem total
    /// editado, soma nela; senão cria uma linha nova.
    /// </summary>
    public LinhaCarrinho Adicionar(Produto produto, ProdutoTamanho tamanho, int quantidade)
    {
        var existente = _linhas.FirstOrDefault(l =>
            l.ProdutoId == produto.Id && l.Tamanho == tamanho.Tamanho && l.TotalEditadoCentavos is null);

        if (existente is null)
            return AdicionarLinha(produto, tamanho, quantidade);

        VerificarEstoque(produto.Nome, tamanho, quantidade, jaNoCarrinho: QuantidadeDe(produto.Id, tamanho.Tamanho));
        existente.Quantidade += quantidade;
        existente.EstoqueDisponivel = tamanho.Estoque;
        return existente;
    }

    /// <summary>Sempre cria uma linha nova (usada ao restaurar o rascunho, que guarda cada linha como estava).</summary>
    public LinhaCarrinho AdicionarLinha(Produto produto, ProdutoTamanho tamanho, int quantidade)
    {
        if (quantidade <= 0)
            throw new RegraDeNegocioException("A quantidade deve ser maior que zero.");
        if (!produto.Ativo)
            throw new RegraDeNegocioException($"O produto \"{produto.Nome}\" está desativado.");
        if (tamanho.ProdutoId != produto.Id)
            throw new RegraDeNegocioException("O tamanho não pertence a este produto.");

        VerificarEstoque(produto.Nome, tamanho, quantidade, jaNoCarrinho: QuantidadeDe(produto.Id, tamanho.Tamanho));

        var linha = new LinhaCarrinho
        {
            ProdutoId = produto.Id,
            Nome = produto.Nome,
            Tamanho = tamanho.Tamanho,
            PrecoVarejoCentavos = produto.PrecoVendaCentavos,
            PrecoAtacadoCentavos = produto.PrecoAtacadoCentavos,
            EstoqueDisponivel = tamanho.Estoque,
            Quantidade = quantidade,
            Tabela = Tabela
        };
        _linhas.Add(linha);
        return linha;
    }

    /// <summary>
    /// Troca a quantidade da linha (mínimo 1, máximo o estoque do tamanho já descontadas as outras linhas
    /// do mesmo produto/tamanho). Se a linha tinha o TOTAL editado, o preço da unidade passa a valer
    /// e o total é recalculado.
    /// </summary>
    public void AlterarQuantidade(LinhaCarrinho linha, int quantidade)
    {
        Conferir(linha);
        if (quantidade < 1)
            throw new RegraDeNegocioException("A quantidade mínima é 1. Para tirar o item, remova a linha.");

        var outras = QuantidadeDe(linha.ProdutoId, linha.Tamanho) - linha.Quantidade;
        if (outras + quantidade > linha.EstoqueDisponivel)
            throw new RegraDeNegocioException(MensagemSemEstoque(linha.Nome, linha.Tamanho, linha.EstoqueDisponivel, outras));

        if (linha.TotalEditadoCentavos is not null)
        {
            linha.PrecoUnitarioEditadoCentavos = linha.PrecoUnitarioCentavos;
            linha.TotalEditadoCentavos = null;
        }
        linha.Quantidade = quantidade;
    }

    /// <summary>Define o preço de UMA unidade (zero é permitido, para brinde).</summary>
    public void DefinirPrecoUnitario(LinhaCarrinho linha, long centavos)
    {
        Conferir(linha);
        if (centavos < 0) throw new RegraDeNegocioException("O preço não pode ser negativo.");
        linha.PrecoUnitarioEditadoCentavos = centavos;
        linha.TotalEditadoCentavos = null;
    }

    /// <summary>Define o TOTAL da linha (útil para "levo as 3 por R$ 100").</summary>
    public void DefinirTotalLinha(LinhaCarrinho linha, long centavos)
    {
        Conferir(linha);
        if (centavos < 0) throw new RegraDeNegocioException("O total não pode ser negativo.");
        linha.TotalEditadoCentavos = centavos;
        linha.PrecoUnitarioEditadoCentavos = null;
    }

    /// <summary>Volta ao preço da tabela.</summary>
    public void RestaurarPreco(LinhaCarrinho linha)
    {
        Conferir(linha);
        linha.PrecoUnitarioEditadoCentavos = null;
        linha.TotalEditadoCentavos = null;
    }

    public void Remover(LinhaCarrinho linha)
    {
        Conferir(linha);
        _linhas.Remove(linha);
    }

    public void Limpar() => _linhas.Clear();

    /// <summary>Troca varejo/atacado. Linhas com preço editado mantêm o preço que o operador digitou.</summary>
    public void MudarTabela(TabelaPreco tabela)
    {
        Tabela = tabela;
        foreach (var linha in _linhas) linha.Tabela = tabela;
    }

    /// <summary>Converte o carrinho nas linhas que o VendaService espera.</summary>
    public List<ItemCarrinho> ParaPedido() => _linhas
        .Select(l => new ItemCarrinho(
            l.ProdutoId, l.Quantidade, l.Tamanho, l.PrecoUnitarioEditadoCentavos, l.TotalEditadoCentavos))
        .ToList();

    /// <summary>Unidades do produto/tamanho que já estão no carrinho (somando todas as linhas dele).</summary>
    public int QuantidadeDe(int produtoId, string tamanho) =>
        _linhas.Where(l => l.ProdutoId == produtoId && l.Tamanho == tamanho).Sum(l => l.Quantidade);

    private void Conferir(LinhaCarrinho linha)
    {
        if (!_linhas.Contains(linha))
            throw new RegraDeNegocioException("Esta linha não está mais no carrinho.");
    }

    private static void VerificarEstoque(string nome, ProdutoTamanho tamanho, int quantidade, int jaNoCarrinho)
    {
        if (tamanho.Estoque <= 0)
            throw new RegraDeNegocioException(
                tamanho.EhSemTamanho ? $"\"{nome}\" está sem estoque." : $"\"{nome}\" está sem estoque no tamanho {tamanho.Tamanho}.");
        if (jaNoCarrinho + quantidade > tamanho.Estoque)
            throw new RegraDeNegocioException(MensagemSemEstoque(nome, tamanho.Tamanho, tamanho.Estoque, jaNoCarrinho));
    }

    private static string MensagemSemEstoque(string nome, string tamanho, int estoque, int jaNoCarrinho)
    {
        var onde = tamanho == Tamanhos.SemTamanho ? "" : $" no tamanho {tamanho}";
        return $"Estoque insuficiente: \"{nome}\" tem {estoque} unidade(s){onde} (no carrinho: {jaNoCarrinho}).";
    }
}
