using System.IO;
using NexusLS.BarTender.Services;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class CodigoPorTamanhoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private static Produto Short(string codigo, string? referencia) => new()
    {
        Nome = "SHORT TACTEL", CodigoBarras = codigo, Referencia = referencia,
        Tamanhos = new List<ProdutoTamanho> { new() { Tamanho = "P", Ordem = 1 }, new() { Tamanho = "M", Ordem = 2 } }
    };

    [Fact]
    public void Codigo_da_casa_vira_referencia_mais_tamanho_e_o_de_fabricante_nao_muda()
    {
        Assert.Equal("14352", CodigoBarrasTamanho.Resolver(Short("2000000000015", "1435"), "M"));
        Assert.Equal("16021", CodigoBarrasTamanho.Resolver(Short("1000000016024", "1602"), "P"));
        Assert.Equal("7891000315507", CodigoBarrasTamanho.Resolver(Short("7891000315507", "1435"), "M"));
        Assert.Equal("2000000000015", CodigoBarrasTamanho.Resolver(Short("2000000000015", "1435S"), "M"));
        Assert.Equal("2000000000015", CodigoBarrasTamanho.Resolver(Short("2000000000015", "101"), "M"));
        Assert.Equal("2000000000015", CodigoBarrasTamanho.Resolver(Short("2000000000015", "1435"), ""));
    }

    [Fact]
    public async Task O_PDV_le_o_codigo_novo_e_escolhe_o_tamanho()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var id = (await produtos.CadastrarAsync(new DadosProduto("Short Tactel", "2000000000015", 5000, 2000, 0), 0)).Id;
        await using (var db = _banco.Fabrica.CreateDbContext())
        {
            var p = db.Produtos.Single(x => x.Id == id);
            p.Referencia = "1435";
            await db.SaveChangesAsync();
        }
        await produtos.AdicionarGradeAsync(id, new[] { "P", "M" });
        var m = (await produtos.ListarTamanhosAsync(id)).Single(t => t.Tamanho == "M");

        var lido = await produtos.ObterPorCodigoDeTamanhoAsync(m.CodigoBarras!);

        Assert.Equal("1435" + m.Ordem, m.CodigoBarras);
        Assert.Equal("M", lido!.Value.Tamanho.Tamanho);
    }

    [Fact]
    public void Baixar_salva_uma_imagem_por_tamanho()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "nexus-codigos-" + Guid.NewGuid().ToString("N"));
        var salvos = 0;
        try
        {
            Capturador.Rodar(() => salvos = ExportadorCodigosDeBarras.Exportar(new[] { Short("2000000000015", "1435") }, pasta));

            Assert.Equal(2, salvos);
            Assert.Equal(2, Directory.GetFiles(pasta, "*.png").Length);
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        }
    }
}
