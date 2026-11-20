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

    [Fact]
    public void Bancada_IntersectaEtiqueta_IdentificaCorretamenteDentroEFora()
    {
        // Totalmente dentro
        Assert.True(Bancada.IntersectaEtiqueta(5, 5, 20, 10, 50, 30));
        // Parcialmente dentro (corta borda)
        Assert.True(Bancada.IntersectaEtiqueta(-5, 5, 20, 10, 50, 30));
        Assert.True(Bancada.IntersectaEtiqueta(45, 5, 20, 10, 50, 30));
        // Totalmente na mesa de trabalho fora da folha
        Assert.False(Bancada.IntersectaEtiqueta(-25, 5, 10, 10, 50, 30));
        Assert.False(Bancada.IntersectaEtiqueta(60, 5, 10, 10, 50, 30));
        Assert.False(Bancada.IntersectaEtiqueta(5, -20, 10, 10, 50, 30));
        Assert.False(Bancada.IntersectaEtiqueta(5, 40, 10, 10, 50, 30));
    }

    [Fact]
    public void Bancada_Prender_MantemElementoNosLimitesDoDesktop()
    {
        var (x1, y1) = Bancada.Prender(-100, -100, 50, 30);
        Assert.Equal(Bancada.LimiteMinimoMm, x1);
        Assert.Equal(Bancada.LimiteMinimoMm, y1);

        var (x2, y2) = Bancada.Prender(200, 200, 50, 30);
        Assert.Equal(50 + Bancada.MargemMesaMm, x2);
        Assert.Equal(30 + Bancada.MargemMesaMm, y2);
    }

    [Fact]
    public void EditorDesenho_Zpl_EscapaCaracteresEspeciais()
    {
        var modelo = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.CriarPadrao50x30();
        var el = modelo.Elementos.First(e => e.Tipo == NexusLS.BarTender.Models.Designer.TipoElemento.Texto);
        el.Variavel = NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Nenhuma;
        el.Texto = "TESTE ^XA E ~XZ ESPECIAL";

        var zpl = EditorDesenhoService.GerarZpl(modelo, null, "MINHA LOJA", 1);
        Assert.Contains("TESTE _5EXA E _7EXZ ESPECIAL", zpl);
    }

    [Fact]
    public void Predefinicao_TagVestuarioRefTamanho35x60_ContemElementosObrigatorios()
    {
        var m = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.CriarTagVestuarioRefTamanho35x60();
        Assert.Equal(35.0, m.LarguraMm);
        Assert.Equal(60.0, m.AlturaMm);

        Assert.Contains(m.Elementos, e => e.Variavel == NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Tamanho && e.TamanhoFonte >= 20);
        Assert.Contains(m.Elementos, e => e.Variavel == NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Referencia);
        Assert.Contains(m.Elementos, e => e.Tipo == NexusLS.BarTender.Models.Designer.TipoElemento.CodigoBarras);
    }

    [Theory]
    [InlineData("7891234567895", true)]
    [InlineData("2000000000015", true)]
    [InlineData("2000000000022", true)]
    [InlineData("2000000000018", false)] // Checksum incorreto (verificador real é 5)
    [InlineData("7891234567890", false)] // Checksum incorreto (verificador real é 5)
    [InlineData("123", false)]           // Tamanho menor
    [InlineData("78912345678", false)]   // 11 dígitos
    [InlineData("78912345678901", false)] // 14 dígitos
    [InlineData("789123456789A", false)] // Não numérico
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Ean13_ValidacaoEDigitoVerificador_ValidaCasosCorretosEIncorretos(string? codigo, bool esperadoValido)
    {
        var valido = CodigoBarrasInterno.EhEan13Valido(codigo);
        Assert.Equal(esperadoValido, valido);

        if (codigo is { Length: 13 } && codigo.All(char.IsAsciiDigit))
        {
            var digitoCalculado = CodigoBarrasInterno.DigitoVerificador(codigo[..12]);
            var digitoReal = codigo[12] - '0';
            Assert.Equal(esperadoValido, digitoCalculado == digitoReal);
        }
    }

    [Fact]
    public void Paginacao_FolhaA4_OffsetPosicaoInicial_CalculaPaginasEPosicoesCorretamente()
    {
        // 21 etiquetas por folha A4 (Pimaco 3x7)
        var produto = new Produto { Nome = "CAMISA POLO", PrecoVendaCentavos = 7990, CodigoBarras = "2000000000018" };
        var itens = new List<ItemFila> { new(produto, 15) };
        var configA4 = new ConfiguracaoEtiqueta { Formato = FormatoOpcao.FolhaA4_3x7 };

        // Começando na posição 1: 15 etiquetas cabem na 1ª página (máx 21)
        var paginatorInicio = new BarTenderPaginator(itens, configA4, primeiraPosicao: 1);
        Assert.Equal(1, paginatorInicio.PageCount);

        // Começando na posição 10 (9 etiquetas já usadas/gastas na folha):
        // 12 cabem na página 1 (posições 10 a 21) e as restantes 3 vão para a página 2 (posições 1 a 3)
        var paginatorComOffset = new BarTenderPaginator(itens, configA4, primeiraPosicao: 10);
        Assert.Equal(2, paginatorComOffset.PageCount);
    }

    [Fact]
    public void ModeloEtiquetaDesign_SalvarECarregarJson_MantemTodasAsPropriedades()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"modelo_teste_{Guid.NewGuid():N}.json");
        try
        {
            var original = new NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign
            {
                Nome = "Etiqueta Teste Roundtrip",
                LarguraMm = 50,
                AlturaMm = 30
            };

            original.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
            {
                Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
                Texto = "Camiseta DryFit",
                TamanhoFonte = 14,
                X = 5,
                Y = 5,
                Largura = 40,
                Altura = 6
            });

            original.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
            {
                Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.CodigoBarras,
                Texto = "7891234567895",
                TipoCodigo = NexusLS.BarTender.Models.Designer.TipoSimboloCodigo.Ean13,
                X = 5,
                Y = 12,
                Largura = 40,
                Altura = 12
            });

            original.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
            {
                Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Retangulo,
                Preenchido = false,
                X = 2,
                Y = 2,
                Largura = 46,
                Altura = 26
            });

            original.Salvar(tempFile);
            Assert.True(File.Exists(tempFile));

            var carregado = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.Carregar(tempFile);
            Assert.NotNull(carregado);
            Assert.Equal(original.Nome, carregado.Nome);
            Assert.Equal(original.LarguraMm, carregado.LarguraMm);
            Assert.Equal(original.AlturaMm, carregado.AlturaMm);
            Assert.Equal(original.Elementos.Count, carregado.Elementos.Count);

            var texto = carregado.Elementos[0];
            Assert.Equal(NexusLS.BarTender.Models.Designer.TipoElemento.Texto, texto.Tipo);
            Assert.Equal("Camiseta DryFit", texto.Texto);
            Assert.Equal(14, texto.TamanhoFonte);

            var barcode = carregado.Elementos[1];
            Assert.Equal(NexusLS.BarTender.Models.Designer.TipoElemento.CodigoBarras, barcode.Tipo);
            Assert.Equal("7891234567895", barcode.Texto);
            Assert.Equal(NexusLS.BarTender.Models.Designer.TipoSimboloCodigo.Ean13, barcode.TipoCodigo);

            var forma = carregado.Elementos[2];
            Assert.Equal(NexusLS.BarTender.Models.Designer.TipoElemento.Retangulo, forma.Tipo);
            Assert.False(forma.Preenchido);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void EditorDesenho_GerarZpl_ModeloSimples_GeraComandosComEscape()
    {
        var modelo = new NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign
        {
            Nome = "Modelo ZPL Teste",
            LarguraMm = 50,
            AlturaMm = 30
        };

        modelo.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
            Variavel = NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Nenhuma,
            Texto = "PRODUTO ^TOP~ ESPECIAL",
            X = 5,
            Y = 5,
            Largura = 40,
            Altura = 6
        });

        modelo.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.CodigoBarras,
            Variavel = NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Nenhuma,
            ValorCodigo = "7891234567895",
            TipoCodigo = NexusLS.BarTender.Models.Designer.TipoSimboloCodigo.Ean13,
            X = 5,
            Y = 12,
            Largura = 40,
            Altura = 12
        });

        var zpl = EditorDesenhoService.GerarZpl(modelo, null, "MINHA LOJA", copias: 1);

        Assert.StartsWith("^XA", zpl.Trim());
        Assert.EndsWith("^XZ", zpl.Trim());
        Assert.Contains("^PW", zpl);
        Assert.Contains("^LL", zpl);

        // Caracteres especiais ^ e ~ devem ser escapados como _5E e _7E
        Assert.Contains("PRODUTO _5ETOP_7E ESPECIAL", zpl);
        Assert.DoesNotContain("PRODUTO ^TOP~ ESPECIAL", zpl);

        // Código de barras
        Assert.Contains("7891234567895", zpl);
    }

    [Fact]
    public void DesignerViewModel_RegraInteligenteGrade_ConverteTamanhoFixoEAtribuiCodigoPorReferenciaETamanho()
    {
        var bermuda = new Produto
        {
            Id = 45,
            Nome = "BERMUDA CARGO",
            Referencia = "1402",
            PrecoVendaCentavos = 7000,
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = "Sem tamanho", Ordem = 0, Estoque = 0 },
                new() { Tamanho = "42", Ordem = 1, Estoque = 6 },
                new() { Tamanho = "44", Ordem = 2, Estoque = 8 },
                new() { Tamanho = "46", Ordem = 3, Estoque = 8 },
                new() { Tamanho = "48", Ordem = 4, Estoque = 12 },
                new() { Tamanho = "50", Ordem = 5, Estoque = 2 }
            }
        };

        var vm = new DesignerViewModel();
        vm.DefinirProduto(bermuda, "LIFESURF", carregarEtiquetaSalvaSeHouver: false);

        // "Sem tamanho" com estoque zero deve ser ocultado quando o produto tem tamanhos reais
        Assert.DoesNotContain("Sem tamanho", vm.TamanhosParaTesteDisponiveis);
        Assert.Equal(new[] { "42", "44", "46", "48", "50" }, vm.TamanhosParaTesteDisponiveis);

        // Simula o usuário criando um texto livre com "42" (como na amostra da foto)
        vm.Modelo = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.CriarEmBranco(35, 60, "Tag 1402");
        var elTamanhoDigitado = new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
            Variavel = NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Nenhuma,
            Texto = "42",
            TamanhoFonte = 47.6,
            Largura = 74.7,
            X = 10.5,
            Y = 8
        };
        var elRef = new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
            Variavel = NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Referencia,
            Texto = "{Ref}"
        };
        var elBarcode = new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.CodigoBarras,
            Variavel = NexusLS.BarTender.Models.Designer.VariavelEtiqueta.CodigoBarras,
            TipoCodigo = NexusLS.BarTender.Models.Designer.TipoSimboloCodigo.Code128
        };
        vm.Modelo.Elementos.Add(elTamanhoDigitado);
        vm.Modelo.Elementos.Add(elRef);
        vm.Modelo.Elementos.Add(elBarcode);

        // Ao aplicar a regra inteligente (chamada ao salvar ou trocar tamanho):
        vm.AplicarRegrasInteligentesDeGrade();

        // O texto livre "42" vira variável dinâmica {Tamanho} e se ajusta dentro da folha de 35mm
        Assert.Equal(NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Tamanho, elTamanhoDigitado.Variavel);
        Assert.Equal("{Tamanho}", elTamanhoDigitado.Texto);
        Assert.True(elTamanhoDigitado.Largura <= 35.0);

        // Para cada tamanho da grade, o tamanho muda e o código de barras é Referência + Ordem, mas a Referência continua "1402"
        Assert.Equal("42", EditorDesenhoService.ResolverTexto(elTamanhoDigitado, bermuda, "42", "LIFESURF"));
        Assert.Equal("1402", EditorDesenhoService.ResolverTexto(elRef, bermuda, "42", "LIFESURF"));
        Assert.Equal("14021", EditorDesenhoService.ResolverCodigo(elBarcode, bermuda, "42"));

        Assert.Equal("44", EditorDesenhoService.ResolverTexto(elTamanhoDigitado, bermuda, "44", "LIFESURF"));
        Assert.Equal("1402", EditorDesenhoService.ResolverTexto(elRef, bermuda, "44", "LIFESURF"));
        Assert.Equal("14022", EditorDesenhoService.ResolverCodigo(elBarcode, bermuda, "44"));

        Assert.Equal("50", EditorDesenhoService.ResolverTexto(elTamanhoDigitado, bermuda, "50", "LIFESURF"));
        Assert.Equal("1402", EditorDesenhoService.ResolverTexto(elRef, bermuda, "50", "LIFESURF"));
        Assert.Equal("14025", EditorDesenhoService.ResolverCodigo(elBarcode, bermuda, "50"));

        // A referência cadastrada no produto não foi alterada
        Assert.Equal("1402", bermuda.Referencia);
    }

    [Fact]
    public void DesignerViewModel_AdicionarTamanho_InsereElementoVinculadoQueMudaComTamanhoSelecionado()
    {
        var vm = new DesignerViewModel();
        vm.TamanhoParaTeste = "G";
        var qtdAntes = vm.Modelo.Elementos.Count;

        vm.AdicionarTamanhoCommand.Execute(null);

        Assert.Equal(qtdAntes + 1, vm.Modelo.Elementos.Count);
        var inserido = vm.Modelo.Elementos.Last();
        Assert.Equal(NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Tamanho, inserido.Variavel);
        Assert.Equal("G", EditorDesenhoService.ResolverTexto(inserido, null, vm.TamanhoParaTeste, "NEXUS"));

        vm.TamanhoParaTeste = "GG";
        Assert.Equal("GG", EditorDesenhoService.ResolverTexto(inserido, null, vm.TamanhoParaTeste, "NEXUS"));
    }

    [Fact]
    public void DesignerViewModel_DistribuirHorizontalmenteEVerticalmente_CalculaPosicoesUniformes()
    {
        var vm = new DesignerViewModel();
        vm.Modelo = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.CriarEmBranco(50, 60, "Teste Distribuicao");

        var e1 = new NexusLS.BarTender.Models.Designer.ElementoDesign { Nome = "E1", X = 2, Y = 5, Largura = 6, Altura = 5, IsSelected = true };
        var e2 = new NexusLS.BarTender.Models.Designer.ElementoDesign { Nome = "E2", X = 10, Y = 12, Largura = 6, Altura = 5, IsSelected = true };
        var e3 = new NexusLS.BarTender.Models.Designer.ElementoDesign { Nome = "E3", X = 42, Y = 45, Largura = 6, Altura = 5, IsSelected = true };

        vm.Modelo.Elementos.Add(e1);
        vm.Modelo.Elementos.Add(e2);
        vm.Modelo.Elementos.Add(e3);

        vm.DistribuirHorizontalmenteCommand.Execute(null);
        // minX = 2, maxX = 48 (42+6), larguras = 18, livre = 28, passo = 14 -> E2.X = 2 + 6 + 14 = 22
        Assert.Equal(2.0, e1.X);
        Assert.Equal(22.0, e2.X);
        Assert.Equal(42.0, e3.X);

        vm.DistribuirVerticalmenteCommand.Execute(null);
        // minY = 5, maxY = 50 (45+5), alturas = 15, livre = 30, passo = 15 -> E2.Y = 5 + 5 + 15 = 25
        Assert.Equal(5.0, e1.Y);
        Assert.Equal(25.0, e2.Y);
        Assert.Equal(45.0, e3.Y);
    }

    [Fact]
    public void DesignerViewModel_AbrirPreviaImpressao_GeraTodasAsEtiquetasDaGradeParaConferencia()
    {
        var bermuda = new Produto
        {
            Id = 45,
            Nome = "BERMUDA CARGO",
            Referencia = "1402",
            PrecoVendaCentavos = 7000,
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = "42", Ordem = 1, Estoque = 6 },
                new() { Tamanho = "44", Ordem = 2, Estoque = 8 },
                new() { Tamanho = "46", Ordem = 3, Estoque = 8 }
            }
        };

        var vm = new DesignerViewModel();
        vm.DefinirProduto(bermuda, "LIFESURF", carregarEtiquetaSalvaSeHouver: false);
        vm.Modelo = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.CriarTagVestuarioRefTamanho35x60();

        vm.AbrirPreviaImpressaoCommand.Execute(null);

        Assert.True(vm.PreviaImpressaoAberta);
        Assert.Equal(3, vm.ItensPreviaImpressao.Count);
        Assert.Equal("42", vm.ItensPreviaImpressao[0].Tamanho);
        Assert.Equal("14021", vm.ItensPreviaImpressao[0].CodigoBarras);
        Assert.Equal("44", vm.ItensPreviaImpressao[1].Tamanho);
        Assert.Equal("14022", vm.ItensPreviaImpressao[1].CodigoBarras);
        Assert.Equal("46", vm.ItensPreviaImpressao[2].Tamanho);
        Assert.Equal("14023", vm.ItensPreviaImpressao[2].CodigoBarras);
    }

    [Fact]
    public void ElementoDesign_EspacamentoLetrasEResumoCamada_FuncionamCorretamente()
    {
        var el = new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Nome = "Marca",
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
            Texto = "LIFESURF",
            TamanhoFonte = 12,
            EspacamentoLetras = 1.5
        };

        Assert.Contains("LIFESURF", el.ResumoCamada);
        var textoEspacado = EditorDesenhoService.AplicarEspacamento(el.Texto, el.EspacamentoLetras);
        Assert.True(textoEspacado.Length > el.Texto.Length);
    }

    [Fact]
    public void ProdutoItemViewModel_CodigoExibicaoColuna_PriorizaReferencia_E_ProtegeTamanhoContraReset()
    {
        var camisa = new Produto
        {
            Id = 15,
            Nome = "CAMISA GOLA CARECA TEXTURIZADA",
            Referencia = "1020",
            CodigoInterno = "INT-000013",
            Categoria = "Camisa",
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = "P", Ordem = 1 },
                new() { Tamanho = "M", Ordem = 2 },
                new() { Tamanho = "G", Ordem = 3 }
            }
        };

        var vm = new ProdutoItemViewModel(camisa);
        Assert.Equal("1020", vm.CodigoExibicaoColuna);

        vm.TamanhoSelecionado = "M";
        Assert.Equal("10202", vm.CodigoExibicaoColuna);
        Assert.Equal("10202", vm.CodigoBarrasExibicao);

        // Simula o ComboBox do DataGrid enviando null ou tamanho fora da lista durante reciclagem/troca de tela
        vm.TamanhoSelecionado = null!;
        Assert.Equal("M", vm.TamanhoSelecionado);

        vm.TamanhoSelecionado = "";
        Assert.Equal("M", vm.TamanhoSelecionado);

        vm.TamanhoSelecionado = "Sem tamanho";
        Assert.Equal("M", vm.TamanhoSelecionado);
    }

    [Fact]
    public async Task MainViewModel_PadronizarEtiqueta_AplicaModeloATodosRespeitandoTamanhoReferenciaECodigo()
    {
        using (var db = CriarContexto())
        {
            db.Produtos.AddRange(
                new Produto
                {
                    Id = 45,
                    Nome = "BERMUDA CARGO",
                    Referencia = "1402",
                    CodigoInterno = "INT-000043",
                    Categoria = "Bermuda",
                    Ativo = true,
                    PrecoVendaCentavos = 7000,
                    Tamanhos = new List<ProdutoTamanho>
                    {
                        new() { Tamanho = "42", Ordem = 1, Estoque = 6 },
                        new() { Tamanho = "44", Ordem = 2, Estoque = 8 }
                    }
                },
                new Produto
                {
                    Id = 62,
                    Nome = "CAMISA GOLA POLO",
                    Referencia = "1202",
                    CodigoInterno = "INT-000060",
                    Categoria = "Camisa Gola Polo",
                    Ativo = true,
                    PrecoVendaCentavos = 6000,
                    Tamanhos = new List<ProdutoTamanho>
                    {
                        new() { Tamanho = "P", Ordem = 1, Estoque = 0 },
                        new() { Tamanho = "M", Ordem = 2, Estoque = 0 },
                        new() { Tamanho = "G", Ordem = 3, Estoque = 0 }
                    }
                }
            );
            await db.SaveChangesAsync();
        }

        var dbService = new BancoDadosService(_tempDbPath);
        var mainVm = new MainViewModel(dbService, iniciarAutomaticamente: false);
        await mainVm.RecarregarDadosAsync();

        Assert.Equal(2, mainVm.TodosProdutos.Count);

        // Simula o usuário criando o modelo da etiqueta no 1402 com "42" digitado e salvando
        var modeloFeitoPeloUsuario = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.CriarEmBranco(35, 60, "Tag Loja 1402");
        modeloFeitoPeloUsuario.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
            Texto = "42",
            TamanhoFonte = 42,
            X = 2, Y = 10, Largura = 31, Altura = 16
        });
        modeloFeitoPeloUsuario.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
            Texto = "REF.:",
            TamanhoFonte = 8,
            X = 2, Y = 30, Largura = 10, Altura = 4
        });
        modeloFeitoPeloUsuario.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.Texto,
            Texto = "1402",
            TamanhoFonte = 10,
            X = 18, Y = 30, Largura = 15, Altura = 4
        });
        modeloFeitoPeloUsuario.Elementos.Add(new NexusLS.BarTender.Models.Designer.ElementoDesign
        {
            Tipo = NexusLS.BarTender.Models.Designer.TipoElemento.CodigoBarras,
            TipoCodigo = NexusLS.BarTender.Models.Designer.TipoSimboloCodigo.Code128,
            X = 2, Y = 36, Largura = 31, Altura = 12
        });

        var padronizados = mainVm.PadronizarEtiquetasComModelo(modeloFeitoPeloUsuario);
        Assert.Equal(2, padronizados);

        // Todos os produtos agora estão com o modelo padronizado vinculado
        Assert.All(mainVm.TodosProdutos, p =>
        {
            Assert.True(p.TemEtiquetaPersonalizada);
            Assert.Equal("Tag Loja 1402", p.NomeEtiquetaPersonalizada);
        });

        // Verifica que na Camisa Gola Polo (Ref 1202), o modelo padronizado exibe Tamanho P/M, Ref 1202 e Código 12021/12022
        var camisaVm = mainVm.TodosProdutos.First(p => p.Id == 62);
        var modeloCamisa = GerenciadorEtiquetasProdutoService.CarregarEtiquetaProduto(62);
        Assert.NotNull(modeloCamisa);

        var elTam = modeloCamisa!.Elementos.First(e => e.Variavel == NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Tamanho);
        var elRef = modeloCamisa.Elementos.First(e => e.Variavel == NexusLS.BarTender.Models.Designer.VariavelEtiqueta.Referencia);
        var elCod = modeloCamisa.Elementos.First(e => e.Tipo == NexusLS.BarTender.Models.Designer.TipoElemento.CodigoBarras);

        Assert.Equal("P", EditorDesenhoService.ResolverTexto(elTam, camisaVm.Produto, "P", "NEXUS"));
        Assert.Equal("1202", EditorDesenhoService.ResolverTexto(elRef, camisaVm.Produto, "P", "NEXUS"));
        Assert.Equal("12021", EditorDesenhoService.ResolverCodigo(elCod, camisaVm.Produto, "P"));

        Assert.Equal("M", EditorDesenhoService.ResolverTexto(elTam, camisaVm.Produto, "M", "NEXUS"));
        Assert.Equal("1202", EditorDesenhoService.ResolverTexto(elRef, camisaVm.Produto, "M", "NEXUS"));
        Assert.Equal("12022", EditorDesenhoService.ResolverCodigo(elCod, camisaVm.Produto, "M"));

        // Preserva o tamanho e o produto selecionado após RecarregarDadosAsync
        camisaVm.TamanhoSelecionado = "G";
        camisaVm.QuantidadeAdicionar = 7;
        mainVm.ProdutoSelecionado = camisaVm;

        await mainVm.RecarregarDadosAsync();

        Assert.Equal(62, mainVm.ProdutoSelecionado?.Id);
        Assert.Equal("G", mainVm.ProdutoSelecionado?.TamanhoSelecionado);
        Assert.Equal(7, mainVm.ProdutoSelecionado?.QuantidadeAdicionar);
    }
}
