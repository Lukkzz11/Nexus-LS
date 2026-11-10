using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Services;
using NexusLS.BarTender.ViewModels;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Tests;

public class BarTenderTests : IDisposable
{
    private readonly string _tempDbPath;

    public BarTenderTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"nexus_bartender_test_{Guid.NewGuid():N}.db");
        using var db = CriarContexto();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath))
                File.Delete(_tempDbPath);
        }
        catch { }
    }

    private PdvDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<PdvDbContext>()
            .UseSqlite($"Data Source={_tempDbPath}")
            .Options;
        return new PdvDbContext(options);
    }

    [Fact]
    public void Formatos_PossuiTodosOsFormatosExigidos()
    {
        var formatos = FormatoOpcao.ListaPadrao;

        Assert.Contains(formatos, f => f.FormatoCore.EtiquetaLarguraMm == 50 && f.FormatoCore.EtiquetaAlturaMm == 30);
        Assert.Contains(formatos, f => f.FormatoCore.EtiquetaLarguraMm == 40 && f.FormatoCore.EtiquetaAlturaMm == 25);
        Assert.Contains(formatos, f => f.FormatoCore.EtiquetaLarguraMm == 60 && f.FormatoCore.EtiquetaAlturaMm == 40);
        Assert.Contains(formatos, f => f.FormatoCore.PaginaLarguraMm == 210 && f.FormatoCore.PaginaAlturaMm == 297);
    }

    [Fact]
    public void DesenhoEtiqueta_GerarZpl_GeraComandosZplValidos()
    {
        var config = new ConfiguracaoEtiqueta
        {
            NomeLoja = "LOJA TESTE NEXUS",
            MostrarNomeLoja = true,
            MostrarNomeProduto = true,
            MostrarPrecoVarejo = true,
            MostrarPrecoAtacado = true,
            MostrarCodigoBarras = true
        };

        var produto = new Produto
        {
            Nome = "VESTIDO FLORAL MIDI",
            Referencia = "VEST-01",
            CodigoInterno = "INT-000001",
            CodigoBarras = "2000000000018",
            PrecoVendaCentavos = 18990,
            PrecoAtacadoCentavos = 12990
        };

        var zpl = DesenhoEtiquetaService.GerarZpl(produto, config, 2);

        Assert.StartsWith("^XA", zpl.Trim());
        Assert.EndsWith("^XZ", zpl.Trim());
        Assert.Contains("^PW", zpl);
        Assert.Contains("^LL", zpl);
        Assert.Contains("LOJA TESTE NEXUS", zpl);
        Assert.Contains("VESTIDO FLORAL MIDI", zpl);
        Assert.Contains("2000000000018", zpl);
        Assert.Contains("^PQ2", zpl);
    }

    [Fact]
    public void DesenhoEtiqueta_RenderizarBitmap_CriaImagemValida()
    {
        var config = new ConfiguracaoEtiqueta();
        var produto = new Produto
        {
            Nome = "CALÇA JEANS SLIM",
            Referencia = "CJ-09",
            CodigoInterno = "INT-000002",
            CodigoBarras = "2000000000025",
            PrecoVendaCentavos = 19990
        };

        var bitmap = DesenhoEtiquetaService.RenderizarBitmap(produto, "42", config);

        Assert.NotNull(bitmap);
        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
    }

    [Fact]
    public void Paginator_CalculaCorretamenteEtiquetasPorPagina()
    {
        var prod1 = new Produto { Nome = "Produto 1", PrecoVendaCentavos = 5000, CodigoBarras = "2000000000018" };
        var prod2 = new Produto { Nome = "Produto 2", PrecoVendaCentavos = 6000, CodigoBarras = "2000000000025" };

        var itens = new List<ItemFila>
        {
            new(prod1, 3),
            new(prod2, 2)
        };

        // Bobina 50x30: 1 por página -> 5 etiquetas = 5 páginas
        var configBobina = new ConfiguracaoEtiqueta { Formato = FormatoOpcao.Bobina50x30 };
        var paginatorBobina = new BarTenderPaginator(itens, configBobina);
        Assert.Equal(5, paginatorBobina.PageCount);

        // Folha A4 3x7 (21 por página) -> 5 etiquetas = 1 página
        var configA4 = new ConfiguracaoEtiqueta { Formato = FormatoOpcao.FolhaA4_3x7 };
        var paginatorA4 = new BarTenderPaginator(itens, configA4);
        Assert.Equal(1, paginatorA4.PageCount);
    }

    [Fact]
    public async Task BancoDadosService_GeraCodigosEan13UnicosEValidos()
    {
        using (var db = CriarContexto())
        {
            db.Produtos.AddRange(
                new Produto { Nome = "Item Sem Código 1", Ativo = true, PrecoVendaCentavos = 1000 },
                new Produto { Nome = "Item Sem Código 2", Ativo = true, PrecoVendaCentavos = 2000 },
                new Produto { Nome = "Item Sem Código 3", Ativo = true, PrecoVendaCentavos = 3000 }
            );
            await db.SaveChangesAsync();
        }

        var dbService = new BancoDadosService(_tempDbPath);
        var produtos = await dbService.ObterProdutosAsync();
        Assert.Equal(3, produtos.Count);
        Assert.All(produtos, p => Assert.Null(p.CodigoBarras));

        // Gera em lote
        var alterados = await dbService.GerarCodigosLoteAsync(produtos.Select(p => p.Id), TipoCodigoBarras.Ean13);
        Assert.Equal(3, alterados);

        var produtosAtualizados = await dbService.ObterProdutosAsync();
        var codigos = produtosAtualizados.Select(p => p.CodigoBarras).ToList();

        // Todos têm código
        Assert.All(codigos, c => Assert.NotNull(c));
        // Todos são EAN-13 válidos
        Assert.All(codigos, c => Assert.True(CodigoBarrasInterno.EhEan13Valido(c)));
        // Todos são únicos
        Assert.Equal(3, codigos.Distinct().Count());
    }

    [Fact]
    public void ItemFila_CalculaPropriedadesEFormatacao()
    {
        var produto = new Produto
        {
            Nome = "CAMISETA BÁSICA",
            Referencia = "CAM-01",
            PrecoVendaCentavos = 4990,
            PrecoAtacadoCentavos = 3490
        };

        var item = new ItemFila(produto, 5, "G");

        Assert.Equal(5, item.Quantidade);
        Assert.Equal("G", item.Tamanho);
        Assert.Equal("R$ 49,90", item.PrecoVarejoFormatado);
        Assert.Equal("R$ 34,90", item.PrecoAtacadoFormatado);

        item.Quantidade = 10;
        Assert.Equal(10, item.Quantidade);
    }

    [Fact]
    public void ItemFila_UsaReferenciaMaisNumeroDoTamanho()
    {
        var produto = new Produto
        {
            Nome = "CAMISETA BÁSICA",
            Referencia = "5555",
            CodigoInterno = "INT-000001",
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = Tamanhos.SemTamanho, Ordem = 0 },
                new() { Tamanho = "P", Ordem = 1 },
                new() { Tamanho = "M", Ordem = 2 }
            }
        };

        Assert.Equal("55551", new ItemFila(produto, 1, "P").CodigoBarras);
        Assert.Equal("55552", new ItemFila(produto, 1, "M").CodigoBarras);
        Assert.Equal("5555", new ItemFila(produto, 1, "").CodigoBarras);

        var item = new ItemFila(produto, 1, "P");
        item.Tamanho = "M";
        Assert.Equal("55552", item.CodigoBarras);
        Assert.Equal("M", item.TamanhoExibicao);
    }

    [Fact]
    public void DesenhoEtiqueta_GerarZpl_UsaReferenciaMaisNumeroDoTamanho()
    {
        var config = new ConfiguracaoEtiqueta();
        var produto = new Produto
        {
            Nome = "CAMISETA BÁSICA",
            Referencia = "5555",
            CodigoInterno = "INT-000001",
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = "P", Ordem = 1 },
                new() { Tamanho = "M", Ordem = 2 }
            }
        };

        var zpl = DesenhoEtiquetaService.GerarZpl(produto, config, 1, "M");
        Assert.Contains("^FD55552^FS", zpl);

        var semCodigo = DesenhoEtiquetaService.GerarZpl(new Produto { Nome = "SEM CODIGO" }, config);
        Assert.DoesNotContain("2000000000018", semCodigo);
    }

    [Fact]
    public void ProdutoItemViewModel_ExpandeAGradeEmOpcoesDeTamanho()
    {
        var produto = new Produto
        {
            Nome = "CAMISETA BÁSICA",
            Referencia = "5555",
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = Tamanhos.SemTamanho, Ordem = 0 },
                new() { Tamanho = "P", Ordem = 1 },
                new() { Tamanho = "M", Ordem = 2 }
            }
        };

        var vm = new ProdutoItemViewModel(produto);

        Assert.True(vm.TemGrade);
        Assert.Equal(ProdutoItemViewModel.TodosOsTamanhos, vm.TamanhoSelecionado);
        Assert.Contains("P", vm.OpcoesTamanho);
        Assert.Contains("M", vm.OpcoesTamanho);
        Assert.DoesNotContain(Tamanhos.SemTamanho, vm.OpcoesTamanho);

        vm.TamanhoSelecionado = "M";
        Assert.Equal("55552", vm.CodigoBarrasExibicao);
    }

    [Fact]
    public void ItemFila_NormalizaSemTamanho_NoConstrutor_E_NoSetter()
    {
        var produto = new Produto { Nome = "CAMISETA", Referencia = "5555" };

        var item = new ItemFila(produto, 1, "Sem tamanho");
        Assert.Equal("", item.Tamanho);
        Assert.Equal(Tamanhos.SemTamanho, item.TamanhoExibicao);

        item.Tamanho = "Sem tamanho";
        Assert.Equal("", item.Tamanho);
        Assert.Equal("5555", item.CodigoBarras);
    }

    [Fact]
    public void ProdutoItemViewModel_MostraSemTamanho_QuandoHaEstoqueNele()
    {
        var produto = new Produto
        {
            Nome = "CAMISETA",
            Referencia = "5555",
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = Tamanhos.SemTamanho, Ordem = 0, Estoque = 3 },
                new() { Tamanho = "P", Ordem = 1 }
            }
        };

        var vm = new ProdutoItemViewModel(produto);

        Assert.True(vm.TemGrade);
        Assert.Contains(Tamanhos.SemTamanho, vm.OpcoesTamanho);
        Assert.Equal("5555", vm.CodigoBarrasExibicao);
    }

    [Fact]
    public void ProdutoItemViewModel_SoComReferencia_JaContaComoComCodigo()
    {
        // A etiqueta monta referência + número do tamanho: o produto tem código mesmo sem EAN gravado.
        var comReferencia = new ProdutoItemViewModel(new Produto
        {
            Nome = "CAMISETA",
            Referencia = "5555",
            Tamanhos = new List<ProdutoTamanho> { new() { Tamanho = "M", Ordem = 3 } }
        });
        comReferencia.TamanhoSelecionado = "M";

        Assert.True(comReferencia.TemCodigoBarras);
        Assert.Equal("Com Código", comReferencia.StatusCodigoTexto);
        Assert.Equal("55553", comReferencia.CodigoBarrasExibicao);

        var soCodigoInterno = new ProdutoItemViewModel(new Produto { Nome = "BONE", CodigoInterno = "INT-000007" });
        Assert.True(soCodigoInterno.TemCodigoBarras);
        Assert.Equal("INT-000007", soCodigoInterno.CodigoBarrasExibicao);

        var semNada = new ProdutoItemViewModel(new Produto { Nome = "SEM CODIGO" });
        Assert.False(semNada.TemCodigoBarras);
        Assert.Equal("Sem Código", semNada.StatusCodigoTexto);
        Assert.Equal("—", semNada.CodigoBarrasExibicao);
    }
}
