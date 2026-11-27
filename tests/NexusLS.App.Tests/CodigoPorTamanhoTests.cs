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
        Assert.Equal("20000000000152", CodigoBarrasTamanho.Resolver(Short("2000000000015", "1435S"), "M"));
        Assert.Equal("20000000000152", CodigoBarrasTamanho.Resolver(Short("2000000000015", "101"), "M"));
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

    [Theory]
    [InlineData("101")]
    [InlineData("1435S")]
    [InlineData(null)]
    public async Task Sem_referencia_que_sirva_o_PDV_le_o_codigo_da_casa_mais_o_tamanho(string? referencia)
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var id = (await produtos.CadastrarAsync(new DadosProduto("Bermuda", "2000000000015", 5000, 2000, 0), 0)).Id;
        await using (var db = _banco.Fabrica.CreateDbContext())
        {
            db.Produtos.Single(x => x.Id == id).Referencia = referencia;
            await db.SaveChangesAsync();
        }
        await produtos.AdicionarGradeAsync(id, new[] { "P", "M" });
        var m = (await produtos.ListarTamanhosAsync(id)).Single(t => t.Tamanho == "M");

        var lido = await produtos.ObterPorCodigoDeTamanhoAsync(m.CodigoBarras!);

        Assert.Equal("2000000000015" + m.Ordem, m.CodigoBarras);
        Assert.Equal("M", lido!.Value.Tamanho.Tamanho);
        Assert.Null(await produtos.ObterPorCodigoDeTamanhoAsync("2000000000015"));
    }

    [Fact]
    public void Codigo_de_14_digitos_num_modelo_EAN13_sai_inteiro_em_Code128()
    {
        string? lido = null;
        Capturador.Rodar(() =>
        {
            var imagem = BarcodeGeneratorService.Gerar("20000000000152", NexusLS.BarTender.Models.Designer.TipoSimboloCodigo.Ean13, 400, 80, margem: 10)!;
            var pontos = new byte[imagem.PixelWidth * imagem.PixelHeight * 4];
            imagem.CopyPixels(pontos, imagem.PixelWidth * 4, 0);
            var fonte = new ZXing.RGBLuminanceSource(pontos, imagem.PixelWidth, imagem.PixelHeight, ZXing.RGBLuminanceSource.BitmapFormat.BGR32);
            lido = new ZXing.OneD.Code128Reader().decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(fonte)))?.Text;
        });

        Assert.Equal("20000000000152", lido);
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
