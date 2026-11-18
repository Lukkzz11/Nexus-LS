using NexusLS.Core.Models;
using NexusLS.Tests.Infra;

namespace NexusLS.Movel.Tests;

/// <summary>Produtos de teste no banco em memória, iguais ao que a loja cadastra.</summary>
internal static class DadosDeTeste
{
    public static int SemearProduto(
        BancoDeTeste banco,
        string nome,
        string? referencia,
        long precoVarejoCentavos,
        long precoAtacadoCentavos,
        string? codigoBarras,
        params (string Tamanho, int Ordem, int Estoque)[] tamanhos)
    {
        using var db = banco.CriarContexto();

        var produto = new Produto
        {
            Nome = nome,
            Referencia = referencia,
            PrecoVendaCentavos = precoVarejoCentavos,
            PrecoAtacadoCentavos = precoAtacadoCentavos,
            CodigoBarras = codigoBarras,
            EstoqueAtual = tamanhos.Sum(t => t.Estoque)
        };

        foreach (var (tamanho, ordem, estoque) in tamanhos)
        {
            produto.Tamanhos.Add(new ProdutoTamanho { Tamanho = tamanho, Ordem = ordem, Estoque = estoque });
        }

        db.Produtos.Add(produto);
        db.SaveChanges();
        return produto.Id;
    }

    public static void DefinirLoja(BancoDeTeste banco, string nome)
    {
        using var db = banco.CriarContexto();
        db.ConfiguracoesLoja.Add(new ConfiguracaoLoja { Id = 1, NomeLoja = nome });
        db.SaveChanges();
    }
}
