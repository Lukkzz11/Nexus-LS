using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

/// <summary>O cupom junta os tamanhos do mesmo produto numa entrada só ("P x1, M x1") sem mexer no total.</summary>
public class CupomTamanhosAgrupadosTests
{
    private static ConfiguracaoLoja Loja(int largura = 42) => new() { NomeLoja = "Loja", LarguraCupom = largura };

    private static Venda VendaComTamanhos(Action<Venda>? ajustar = null)
    {
        var v = new Venda
        {
            Id = 9,
            DataHora = new DateTime(2026, 11, 19, 10, 0, 0),
            FormaPagamento = FormaPagamento.Dinheiro,
            SubtotalCentavos = 5990 * 2 + 4000,
            TotalCentavos = 5990 * 2 + 4000,
            Itens =
            {
                new ItemVenda { ProdutoId = 1, NomeProduto = "Camiseta Básica", Referencia = "1435", Tamanho = "P", PrecoUnitarioCentavos = 5990, Quantidade = 1, TotalCentavos = 5990 },
                new ItemVenda { ProdutoId = 2, NomeProduto = "Boné", Tamanho = Tamanhos.SemTamanho, PrecoUnitarioCentavos = 4000, Quantidade = 1, TotalCentavos = 4000 },
                new ItemVenda { ProdutoId = 1, NomeProduto = "Camiseta Básica", Referencia = "1435", Tamanho = "M", PrecoUnitarioCentavos = 5990, Quantidade = 1, TotalCentavos = 5990 }
            }
        };
        ajustar?.Invoke(v);
        return v;
    }

    [Theory]
    [InlineData(ModeloCupom.Life)]
    [InlineData(ModeloCupom.Compacto)]
    [InlineData(ModeloCupom.Comprovante)]
    public void Mostra_cada_tamanho_e_quantidade_numa_entrada_so_e_mantem_o_total(ModeloCupom modelo)
    {
        var loja = Loja();
        loja.CupomModelo = (int)modelo;
        var cupom = MontadorCupom.Montar(VendaComTamanhos(), loja);

        Assert.Contains("P x1, M x1", cupom);
        Assert.Equal(1, cupom.Split("Camiseta Básica").Length - 1);   // o nome sai uma vez só
        Assert.Contains("R$ 119,80", cupom);                          // 2 camisetas somadas
        Assert.Contains("R$ 159,80", cupom);                          // total da venda
        Assert.Contains("Boné", cupom);
    }

    [Fact]
    public void Quantidade_do_mesmo_tamanho_soma_e_a_ordem_e_a_da_primeira_aparicao()
    {
        var venda = VendaComTamanhos(v => v.Itens.Add(
            new ItemVenda { ProdutoId = 1, NomeProduto = "Camiseta Básica", Tamanho = "P", PrecoUnitarioCentavos = 5990, Quantidade = 2, TotalCentavos = 11980 }));
        var cupom = MontadorCupom.Montar(venda, Loja());

        Assert.Contains("Tam: P x3, M x1", cupom);
        Assert.Contains("4 pç", cupom);
    }

    [Fact]
    public void Item_de_um_tamanho_so_continua_no_formato_antigo()
    {
        var venda = VendaComTamanhos(v => v.Itens.RemoveAt(2));
        var cupom = MontadorCupom.Montar(venda, Loja());

        Assert.Contains("Camiseta Básica (P)", cupom);
        Assert.DoesNotContain("Tam:", cupom);
    }

    [Fact]
    public void Comprovante_mostra_a_referencia_do_grupo()
    {
        var loja = Loja();
        loja.CupomModelo = (int)ModeloCupom.Comprovante;
        Assert.Contains("Ref: 1435", MontadorCupom.Montar(VendaComTamanhos(), loja));
    }

    [Fact]
    public void Produtos_diferentes_sem_id_nao_se_misturam()
    {
        var venda = VendaComTamanhos(v =>
        {
            v.Itens.Clear();
            v.Itens.Add(new ItemVenda { NomeProduto = "Calça", Tamanho = "40", PrecoUnitarioCentavos = 100, Quantidade = 1, TotalCentavos = 100 });
            v.Itens.Add(new ItemVenda { NomeProduto = "Saia", Tamanho = "38", PrecoUnitarioCentavos = 100, Quantidade = 1, TotalCentavos = 100 });
        });
        var cupom = MontadorCupom.Montar(venda, Loja());

        Assert.Contains("Calça (40)", cupom);
        Assert.Contains("Saia (38)", cupom);
    }
}
