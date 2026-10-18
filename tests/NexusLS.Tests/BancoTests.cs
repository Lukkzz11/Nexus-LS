using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class BancoTests
{
    [Fact]
    public void Salva_e_le_produto_com_valores_em_centavos()
    {
        using var banco = new BancoDeTeste();
        using (var db = banco.CriarContexto())
        {
            db.Produtos.Add(new Produto { Nome = "Café", PrecoVendaCentavos = 1250, PrecoCustoCentavos = 800, EstoqueAtual = 5 });
            db.SaveChanges();
        }

        using var leitura = banco.CriarContexto();
        var p = leitura.Produtos.Single();
        Assert.Equal(1250, p.PrecoVendaCentavos);
    }

    [Fact]
    public void Nao_permite_dois_produtos_com_o_mesmo_codigo_de_barras()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.CriarContexto();
        db.Produtos.Add(new Produto { Nome = "A", CodigoBarras = "123" });
        db.Produtos.Add(new Produto { Nome = "B", CodigoBarras = "123" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Permite_varios_produtos_sem_codigo_de_barras()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.CriarContexto();
        db.Produtos.Add(new Produto { Nome = "A" });
        db.Produtos.Add(new Produto { Nome = "B" });
        db.SaveChanges();
        Assert.Equal(2, db.Produtos.Count());
    }
}
