using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.BarTender.ViewModels;

namespace NexusLS.App.Tests;

/// <summary>Etapa 9 do BarTender: seleção por retângulo (marquee) e ordem de camadas (z-order).</summary>
public class CamadasESelecaoRetanguloTests
{
    private static ElementoDesign Ret(string nome, double x, double y, double l, double a, string cor = "#000000", bool preenchido = true)
        => new()
        {
            Tipo = TipoElemento.Retangulo,
            Nome = nome,
            X = x,
            Y = y,
            Largura = l,
            Altura = a,
            Preenchido = preenchido,
            CorTexto = cor,
            CorFundo = cor
        };

    private static string[] Nomes(IEnumerable<ElementoDesign> els) => els.Select(e => e.Nome).ToArray();

    private static List<string> Lista(params string[] itens) => itens.ToList();

    // ---------------------------------------------------------------- ordem (lógica pura)

    [Fact]
    public void Ordem_TrazerParaFrente_MoveSelecionadosParaOFimMantendoARelativa()
    {
        var r = OrdemCamadas.Reordenar(Lista("a", "b", "c", "d", "e"), x => x is "b" or "d", OperacaoCamada.TrazerParaFrente);
        Assert.Equal(new[] { "a", "c", "e", "b", "d" }, r);
    }

    [Fact]
    public void Ordem_EnviarParaTras_MoveSelecionadosParaOInicio()
    {
        var r = OrdemCamadas.Reordenar(Lista("a", "b", "c", "d", "e"), x => x is "c" or "e", OperacaoCamada.EnviarParaTras);
        Assert.Equal(new[] { "c", "e", "a", "b", "d" }, r);
    }

    [Fact]
    public void Ordem_SubirUmaCamada_TrocaComOVizinhoDaFrente()
    {
        var r = OrdemCamadas.Reordenar(Lista("a", "b", "c", "d"), x => x == "b", OperacaoCamada.SubirUmaCamada);
        Assert.Equal(new[] { "a", "c", "b", "d" }, r);
    }

    [Fact]
    public void Ordem_DescerUmaCamada_TrocaComOVizinhoDeTras()
    {
        var r = OrdemCamadas.Reordenar(Lista("a", "b", "c", "d"), x => x == "c", OperacaoCamada.DescerUmaCamada);
        Assert.Equal(new[] { "a", "c", "b", "d" }, r);
    }

    [Fact]
    public void Ordem_SubirComVariosSelecionados_NaoUltrapassaEntreSi()
    {
        var r = OrdemCamadas.Reordenar(Lista("a", "b", "c", "d", "e"), x => x is "b" or "c", OperacaoCamada.SubirUmaCamada);
        Assert.Equal(new[] { "a", "d", "b", "c", "e" }, r);
    }

    [Fact]
    public void Ordem_SelecionadoNoTopo_NaoSobeEMarcaComoSemAlteracao()
    {
        var lista = Lista("a", "b", "c");
        Assert.False(OrdemCamadas.Altera(lista, x => x == "c", OperacaoCamada.SubirUmaCamada));
        Assert.False(OrdemCamadas.Altera(lista, x => x == "c", OperacaoCamada.TrazerParaFrente));
        Assert.False(OrdemCamadas.Altera(lista, x => x == "a", OperacaoCamada.DescerUmaCamada));
        Assert.False(OrdemCamadas.Altera(lista, x => x == "a", OperacaoCamada.EnviarParaTras));
        Assert.True(OrdemCamadas.Altera(lista, x => x == "a", OperacaoCamada.SubirUmaCamada));
    }

    [Fact]
    public void Ordem_ListaVaziaOuSemSelecao_NaoMuda()
    {
        Assert.Empty(OrdemCamadas.Reordenar(new List<string>(), _ => true, OperacaoCamada.TrazerParaFrente));
        Assert.Equal(new[] { "a", "b" }, OrdemCamadas.Reordenar(Lista("a", "b"), _ => false, OperacaoCamada.EnviarParaTras));
    }

    [Fact]
    public void Ordem_TodosSelecionados_NaoMuda()
    {
        var lista = Lista("a", "b", "c");
        foreach (var op in Enum.GetValues<OperacaoCamada>())
        {
            Assert.Equal(lista, OrdemCamadas.Reordenar(lista, _ => true, op));
        }
    }

    // ---------------------------------------------------------------- retângulo (lógica pura)

    [Fact]
    public void Retangulo_DeDoisPontos_AceitaArrastoEmQualquerDirecao()
    {
        var r = RetanguloMm.DeDoisPontos(30, 20, 10, 5);
        Assert.Equal(new RetanguloMm(10, 5, 20, 15), r);
    }

    [Fact]
    public void Retangulo_Toca_IncluiSobreposicaoParcialEncostoENaoToca()
    {
        var area = new RetanguloMm(10, 10, 10, 10);
        Assert.True(area.Toca(new RetanguloMm(15, 15, 20, 20))); // parcial
        Assert.True(area.Toca(new RetanguloMm(12, 12, 2, 2)));   // contido
        Assert.True(area.Toca(new RetanguloMm(20, 10, 5, 5)));   // encosta na borda
        Assert.False(area.Toca(new RetanguloMm(21, 10, 5, 5)));  // fora
    }

    [Fact]
    public void Retangulo_ComRotacao90_TrocaLarguraEAlturaEmTornoDoCentro()
    {
        var r = new RetanguloMm(10, 10, 20, 4).ComRotacao(90);
        Assert.Equal(4, r.Largura, 6);
        Assert.Equal(20, r.Altura, 6);
        Assert.Equal(18, r.X, 6);
        Assert.Equal(2, r.Y, 6);
    }

    [Fact]
    public void Selecao_ElementoGiradoUsaAAreaVisivel()
    {
        // Barra horizontal de 20x4 em (10,10) girada 90 graus ocupa x 18..22, y 2..22.
        var barra = Ret("barra", 10, 10, 20, 4);
        barra.Rotacao = 90;

        var emCimaDaPonta = new RetanguloMm(19, 3, 2, 2);
        var ondeEraAntes = new RetanguloMm(11, 11, 2, 2);

        Assert.Single(SelecaoPorRetangulo.ElementosTocados(new[] { barra }, emCimaDaPonta));
        Assert.Empty(SelecaoPorRetangulo.ElementosTocados(new[] { barra }, ondeEraAntes));
    }

    [Fact]
    public void Selecao_IgnoraOcultosEBloqueados_ETocaTodosOsTipos()
    {
        var texto = new ElementoDesign { Tipo = TipoElemento.Texto, Nome = "texto", X = 2, Y = 2, Largura = 20, Altura = 6 };
        var codigo = new ElementoDesign { Tipo = TipoElemento.CodigoBarras, Nome = "codigo", X = 2, Y = 10, Largura = 30, Altura = 10 };
        var imagem = new ElementoDesign { Tipo = TipoElemento.Imagem, Nome = "imagem", X = 30, Y = 2, Largura = 10, Altura = 10 };
        var linha = new ElementoDesign { Tipo = TipoElemento.Linha, Nome = "linha", X = 2, Y = 25, Largura = 40, Altura = 0 };
        var oculto = Ret("oculto", 5, 5, 5, 5);
        oculto.Visivel = false;
        var travado = Ret("travado", 5, 5, 5, 5);
        travado.Bloqueado = true;

        var todos = new List<ElementoDesign> { texto, codigo, imagem, linha, oculto, travado };
        var tudo = new RetanguloMm(0, 0, 60, 40);

        Assert.Equal(new[] { "texto", "codigo", "imagem", "linha" }, Nomes(SelecaoPorRetangulo.ElementosTocados(todos, tudo)));
        // A linha tem altura zero, mas ainda assim pode ser alcançada
        Assert.Contains(linha, SelecaoPorRetangulo.ElementosTocados(todos, new RetanguloMm(10, 24, 5, 2)));
    }

    [Fact]
    public void Selecao_SomarMantemSelecaoAnteriorEOrdemDeEmpilhamento()
    {
        var a = Ret("a", 0, 0, 5, 5);
        var b = Ret("b", 20, 0, 5, 5);
        var c = Ret("c", 40, 0, 5, 5);
        var todos = new List<ElementoDesign> { a, b, c };

        var area = new RetanguloMm(38, 0, 10, 10); // só toca o c

        var semSomar = SelecaoPorRetangulo.Resolver(todos, new[] { a }, area, somar: false);
        Assert.Equal(new[] { "c" }, Nomes(semSomar));

        var somando = SelecaoPorRetangulo.Resolver(todos, new[] { a }, area, somar: true);
        Assert.Equal(new[] { "a", "c" }, Nomes(somando));
    }

    [Fact]
    public void Selecao_LimiteDeArraste_DiferenciaCliqueDeArraste()
    {
        Assert.False(SelecaoPorRetangulo.PassouDoLimite(2, -3));
        Assert.True(SelecaoPorRetangulo.PassouDoLimite(5, 0));
        Assert.True(SelecaoPorRetangulo.PassouDoLimite(0, -9));
    }

    // ---------------------------------------------------------------- ViewModel (multisseleção existente)

    private static DesignerViewModel NovoVm(out ElementoDesign fundo, out ElementoDesign meio, out ElementoDesign topo)
    {
        var vm = new DesignerViewModel();
        vm.Modelo = ModeloEtiquetaDesign.CriarEmBranco(50, 30, "Camadas");
        fundo = Ret("fundo", 5, 5, 20, 15, "#CC0000");
        meio = Ret("meio", 15, 8, 20, 15, "#00AA00");
        topo = Ret("topo", 25, 10, 20, 15, "#0000CC");
        vm.Modelo.Elementos.Add(fundo);
        vm.Modelo.Elementos.Add(meio);
        vm.Modelo.Elementos.Add(topo);
        return vm;
    }

    [Fact]
    public void ViewModel_Duplicar_ComSelecaoMultipla_DuplicaTodosESelecionaOsNovos()
    {
        var vm = NovoVm(out var fundo, out var meio, out var topo);
        fundo.IsSelected = true;
        meio.IsSelected = true;
        vm.ElementoSelecionado = meio;

        vm.DuplicarSelecionadoCommand.Execute(null);

        Assert.Equal(5, vm.Modelo.Elementos.Count);
        var novos = vm.Modelo.Elementos.Skip(3).ToList();
        Assert.All(novos, n => Assert.True(n.IsSelected));
        Assert.False(fundo.IsSelected);
        Assert.False(meio.IsSelected);
        Assert.True(novos[0].X > fundo.X);
        Assert.True(novos[1].X > meio.X);
    }

    [Fact]
    public void ViewModel_CopiarERecortarEColar_ComSelecaoMultipla_LevamTodos()
    {
        var vm = NovoVm(out var fundo, out var meio, out var topo);
        fundo.IsSelected = true;
        topo.IsSelected = true;
        vm.ElementoSelecionado = topo;

        vm.CopiarCommand.Execute(null);
        vm.ColarCommand.Execute(null);
        Assert.Equal(5, vm.Modelo.Elementos.Count);
        Assert.Equal(2, vm.Modelo.Elementos.Count(e => e.IsSelected));

        vm.RecortarCommand.Execute(null);
        Assert.Equal(3, vm.Modelo.Elementos.Count);
        vm.ColarCommand.Execute(null);
        Assert.Equal(5, vm.Modelo.Elementos.Count);
    }

    [Fact]
    public void ViewModel_Duplicar_SemMarcacao_UsaOElementoPrincipal()
    {
        var vm = NovoVm(out _, out var meio, out _);
        vm.ElementoSelecionado = meio;
        vm.DuplicarSelecionadoCommand.Execute(null);
        Assert.Equal(4, vm.Modelo.Elementos.Count);
    }

    [Fact]
    public void ViewModel_TrazerParaFrente_ComSelecaoMultipla_MoveOsDoisEDesfazRefaz()
    {
        var vm = NovoVm(out var fundo, out var meio, out var topo);
        fundo.IsSelected = true;
        meio.IsSelected = true;
        vm.ElementoSelecionado = meio;

        vm.TrazerParaFrenteCommand.Execute(null);
        Assert.Equal(new[] { "topo", "fundo", "meio" }, Nomes(vm.Modelo.Elementos));

        vm.Desfazer();
        Assert.Equal(new[] { "fundo", "meio", "topo" }, Nomes(vm.Modelo.Elementos));

        vm.Refazer();
        Assert.Equal(new[] { "topo", "fundo", "meio" }, Nomes(vm.Modelo.Elementos));
    }

    [Fact]
    public void ViewModel_EnviarParaTras_SubirEDescer_FuncionamComSelecaoUnica()
    {
        var vm = NovoVm(out _, out var meio, out var topo);
        vm.SelecionarElemento(topo);
        vm.EnviarParaTrasCommand.Execute(null);
        Assert.Equal(new[] { "topo", "fundo", "meio" }, Nomes(vm.Modelo.Elementos));

        vm.SubirCamadaCommand.Execute(null);
        Assert.Equal(new[] { "fundo", "topo", "meio" }, Nomes(vm.Modelo.Elementos));

        vm.DescerCamadaCommand.Execute(null);
        Assert.Equal(new[] { "topo", "fundo", "meio" }, Nomes(vm.Modelo.Elementos));

        vm.SelecionarElemento(meio);
        vm.AvancarCamadaCommand.Execute(null); // já é o do topo
        Assert.Equal(new[] { "topo", "fundo", "meio" }, Nomes(vm.Modelo.Elementos));
    }

    [Fact]
    public void ViewModel_OperacaoSemEfeito_NaoGeraPassoDeDesfazer()
    {
        var vm = NovoVm(out _, out _, out var topo);
        vm.SelecionarElemento(topo);

        vm.TrazerParaFrenteCommand.Execute(null); // já está na frente

        // Se tivesse gravado um passo, o desfazer reverteria esta troca de nome e o teste falharia.
        vm.RegistrarSnapshot();
        topo.Nome = "topo renomeado";
        vm.Desfazer();
        Assert.Equal(new[] { "fundo", "meio", "topo" }, Nomes(vm.Modelo.Elementos));
    }

    [Fact]
    public void ViewModel_ExcluirComSelecaoMultipla_RemoveTodosOsSelecionados()
    {
        var vm = NovoVm(out var fundo, out var meio, out var topo);
        fundo.IsSelected = true;
        topo.IsSelected = true;
        vm.ElementoSelecionado = topo;

        vm.ExcluirSelecionadoCommand.Execute(null);

        Assert.Equal(new[] { "meio" }, Nomes(vm.Modelo.Elementos));
        vm.Desfazer();
        Assert.Equal(3, vm.Modelo.Elementos.Count);
    }

    // ---------------------------------------------------------------- arquivo, ZPL e imagem

    [Fact]
    public void Arquivo_OrdemSalvaVoltaIgualEEtiquetaAntigaSemCampoDeOrdemUsaAOrdemDaLista()
    {
        var vm = NovoVm(out var fundo, out _, out _);
        vm.SelecionarElemento(fundo);
        vm.TrazerParaFrenteCommand.Execute(null);

        var caminho = Path.Combine(Path.GetTempPath(), $"camadas_{Guid.NewGuid():N}.json");
        try
        {
            vm.Modelo.Salvar(caminho);
            var json = File.ReadAllText(caminho);
            Assert.DoesNotContain("ordem", json, StringComparison.OrdinalIgnoreCase); // a ordem é a da lista
            var carregado = ModeloEtiquetaDesign.Carregar(caminho);
            Assert.Equal(new[] { "meio", "topo", "fundo" }, Nomes(carregado.Elementos));
        }
        finally
        {
            File.Delete(caminho);
        }

        // Etiqueta antiga (sem nenhum campo extra): a ordem do arquivo continua sendo a de empilhamento
        const string antigo = """
        { "Nome": "Antiga", "LarguraMm": 50, "AlturaMm": 30,
          "Elementos": [ { "Tipo": 0, "Nome": "primeiro" }, { "Tipo": 0, "Nome": "segundo" } ] }
        """;
        var m = System.Text.Json.JsonSerializer.Deserialize<ModeloEtiquetaDesign>(antigo, ModeloEtiquetaDesign.JsonOpts)!;
        Assert.Equal(new[] { "primeiro", "segundo" }, Nomes(m.Elementos));
    }

    [Fact]
    public void Zpl_SegueAOrdemDeEmpilhamento()
    {
        var vm = NovoVm(out var fundo, out _, out _);
        fundo.Largura = 11; // identifica o retângulo no ZPL (^GB largura em dots)
        var antes = EditorDesenhoService.GerarZpl(vm.Modelo, null, "LOJA");

        vm.SelecionarElemento(fundo);
        vm.TrazerParaFrenteCommand.Execute(null);
        var depois = EditorDesenhoService.GerarZpl(vm.Modelo, null, "LOJA");

        static List<int> Larguras(string zpl) => System.Text.RegularExpressions.Regex
            .Matches(zpl, @"\^GB(\d+),")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        var la = Larguras(antes);
        var ld = Larguras(depois);
        Assert.Equal(3, la.Count);
        Assert.Equal(3, ld.Count);

        // O "fundo" (o mais estreito) era o primeiro e passa a ser o último
        Assert.Equal(la.Min(), la[0]);
        Assert.Equal(la.Min(), ld[^1]);
    }

    private static Color CorNoPonto(BitmapSource bmp, double xMm, double yMm, double escala)
    {
        var px = (int)(EditorDesenhoService.Dip(xMm) * escala);
        var py = (int)(EditorDesenhoService.Dip(yMm) * escala);
        var pixels = new byte[4];
        bmp.CopyPixels(new Int32Rect(px, py, 1, 1), pixels, 4, 0);
        return Color.FromRgb(pixels[2], pixels[1], pixels[0]);
    }

    private static T NaThreadSta<T>(Func<T> acao)
    {
        T? resultado = default;
        Exception? falha = null;
        var t = new Thread(() =>
        {
            try { resultado = acao(); }
            catch (Exception ex) { falha = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (falha != null) throw new InvalidOperationException("Falha na thread STA", falha);
        return resultado!;
    }

    [Fact]
    public void Imagem_DaEtiqueta_MostraQuemEstaNaFrenteEMudaAoReordenar()
    {
        var (antes, depois) = NaThreadSta(() =>
        {
            var vm = NovoVm(out var fundo, out _, out _);
            // fundo (vermelho) e meio (verde) se sobrepõem em x 15..25, y 8..20
            var bmpAntes = EditorDesenhoService.RenderizarBitmap(vm.Modelo, null, null, "LOJA", 2.0);
            var corAntes = CorNoPonto(bmpAntes, 20, 12, 2.0);

            vm.SelecionarElemento(fundo);
            vm.TrazerParaFrenteCommand.Execute(null);
            var bmpDepois = EditorDesenhoService.RenderizarBitmap(vm.Modelo, null, null, "LOJA", 2.0);
            var corDepois = CorNoPonto(bmpDepois, 20, 12, 2.0);

            SalvarPngSeSolicitado("camadas-antes.png", bmpAntes);
            SalvarPngSeSolicitado("camadas-depois.png", bmpDepois);
            return (corAntes, corDepois);
        });

        Assert.Equal(Color.FromRgb(0x00, 0xAA, 0x00), antes);  // verde (meio) em cima do vermelho
        Assert.Equal(Color.FromRgb(0xCC, 0x00, 0x00), depois); // vermelho trazido para frente
    }

    private static void SalvarPngSeSolicitado(string nome, BitmapSource bmp)
    {
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(pasta)) return;
        Directory.CreateDirectory(pasta);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(Path.Combine(pasta, nome));
        png.Save(f);
    }

    /// <summary>Captura a área de criação com uma seleção múltipla. Só roda com NEXUSLS_PRINTS.</summary>
    [Fact]
    public void Captura_AreaDeCriacaoComSelecaoMultipla()
    {
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(pasta)) return;
        Directory.CreateDirectory(pasta);

        NaThreadSta(() =>
        {
            var app = new NexusLS.BarTender.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) => e.Handled = true;
            app.InitializeComponent();

            var vm = new DesignerViewModel();
            var tela = new NexusLS.BarTender.Views.Designer.DesignerView { DataContext = vm };
            var janela = new Window
            {
                Width = 1400, Height = 800, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
                ShowActivated = false, Left = -20000, Top = 0, Content = tela
            };
            janela.Show();
            janela.UpdateLayout();
            janela.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // Só depois da tela estabilizar (a combo de predefinições recarrega o modelo ao abrir)
            vm.Modelo = ModeloEtiquetaDesign.CriarEmBranco(50, 30, "Camadas");
            var fundo = Ret("fundo", 5, 5, 20, 15, "#CC0000");
            var meio = Ret("meio", 15, 8, 20, 15, "#00AA00");
            var topo = Ret("topo", 25, 10, 20, 15, "#0000CC");
            foreach (var e in new[] { fundo, meio, topo }) vm.Modelo.Elementos.Add(e);
            fundo.IsSelected = true;
            meio.IsSelected = true;
            vm.ElementoSelecionado = meio;
            tela.ReconstruirCanvas();
            janela.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            janela.UpdateLayout();

            var bmp = new RenderTargetBitmap(1400, 800, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(tela);
            SalvarPngSeSolicitado("camadas-area-de-criacao.png", bmp);
            janela.Close();
            app.Shutdown();
            return 0;
        });
    }
}
