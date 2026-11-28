using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.BarTender.ViewModels;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using Xunit;

namespace NexusLS.App.Tests;

public sealed class CapturaPreviaTags
{
    private const double Dpi = 203;

    /// <summary>
    /// Gera as imagens das tags 50 × 75 com o mesmo código que desenha e imprime. Só roda com
    /// NEXUSLS_PREVIA_TAGS (pasta de saída) e NEXUSLS_PREVIA_BANCO (nexus.db, aberto somente para leitura).
    /// </summary>
    [Fact]
    public void Captura_PreviaDasTags()
    {
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PREVIA_TAGS");
        var banco = Environment.GetEnvironmentVariable("NEXUSLS_PREVIA_BANCO");
        if (string.IsNullOrWhiteSpace(pasta) || string.IsNullOrWhiteSpace(banco)) return;
        Directory.CreateDirectory(pasta);

        Exception? erro = null;
        var thread = new Thread(() =>
        {
            try { Gerar(pasta, banco); }
            catch (Exception ex) { erro = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (erro is not null) throw new InvalidOperationException(erro.ToString());
    }

    private static void Gerar(string pasta, string caminhoBanco)
    {
        using var conexao = new SqliteConnection($"Data Source={caminhoBanco};Mode=ReadOnly");
        conexao.Open();

        var atual = JsonSerializer.Deserialize<ModeloEtiquetaDesign>(
            (string)Consultar(conexao, "select Json from EtiquetasProduto where ProdutoId = 45")[0][0],
            ModeloEtiquetaDesign.JsonOpts)!;
        var logo = PadronizarEtiquetaViewModel.LogoDaLoja(new[] { atual });
        var loja = Consultar(conexao, "select NomeLoja from ConfiguracoesLoja limit 1")
            .Select(l => l[0] as string).FirstOrDefault() ?? "NEXUS LS";

        // produto, tamanho mostrado, apelido do arquivo
        var casos = new (int Id, string Tamanho, string Apelido)[]
        {
            (45, "42", "bermuda-cargo-42"),
            (58, "GG", "camisa-botao-tec-GG"),
            (15, "EXG", "camisa-texturizada-EXG"),
            (20, "G10", "polo-manga-comprida-G10"),
            (82, "G", "machao-ref-0001M-G"),
            (3, "48", "short-mauricinho-48"),
        };
        var produtos = casos.Select(c => LerProduto(conexao, c.Id, c.Tamanho)).ToList();

        var variantes = new (string Letra, Func<ModeloEtiquetaDesign> Fabrica)[]
        {
            ("A", ModeloEtiquetaDesign.CriarTagRoupaSemPreco50x75),
            ("B", ModeloEtiquetaDesign.CriarTagRoupaComPreco50x75),
            ("C", ModeloEtiquetaDesign.CriarTagRoupaCompacta50x75),
        };

        foreach (var (letra, fabrica) in variantes)
        {
            var modelo = fabrica().AplicarLogo(logo);
            var baixo = ModeloMidiaService.Ajustar(modelo, 50, 63).Modelo;

            for (var i = 0; i < casos.Length; i++)
                Salvar(Etiqueta(modelo, produtos[i], casos[i].Tamanho, loja), 2, pasta, $"{letra}-{casos[i].Apelido}.png");

            // Todos os casos lado a lado, para conferir de uma vez.
            Salvar(Painel(casos.Select((c, i) => ($"{c.Tamanho} · ref {produtos[i].Referencia ?? "(sem)"}",
                Etiqueta(modelo, produtos[i], c.Tamanho, loja))).ToList(), 3), 1.5, pasta, $"{letra}-todos.png");

            // A página de 2 colunas sai do mesmo paginador da impressão.
            Salvar(Pagina(modelo, 75, 0, produtos[0], "42", produtos[1], "GG", loja), 2, pasta, $"{letra}-pagina-2colunas-100x75.png");
            Salvar(Pagina(baixo, 63, 12, produtos[0], "42", produtos[1], "GG", loja), 2, pasta, $"{letra}-pagina-descer12mm.png");
            Salvar(Painel(new()
            {
                ("75 mm (sem ajuste)", Etiqueta(modelo, produtos[1], "GG", loja)),
                ("63 mm (Descer 12 mm)", Etiqueta(baixo, produtos[1], "GG", loja)),
            }, 2), 1.5, pasta, $"{letra}-altura75-x-63.png");

            Salvar(ComFuro(Etiqueta(modelo, produtos[1], "GG", loja)), 2, pasta, $"{letra}-marcacao-furo-e-margens.png");

            // Como a térmica de 203 dpi enxerga: só preto ou branco.
            Salvar(PretoEBranco(Etiqueta(modelo, produtos[0], "42", loja)), 1, pasta, $"{letra}-203dpi-preto-e-branco.png");
        }

        var antigo = ModeloMidiaService.Ajustar(atual, 50, 75).Modelo;
        foreach (var (indice, nome) in new[] { (0, "bermuda"), (1, "camisa-nome-longo") })
        {
            var itens = new List<(string, DrawingVisual)> { ("ATUAL (35×60 ampliada)", Etiqueta(antigo, produtos[indice], casos[indice].Tamanho, loja)) };
            itens.AddRange(variantes.Select(v => ($"NOVA {v.Letra}",
                Etiqueta(v.Fabrica().AplicarLogo(logo), produtos[indice], casos[indice].Tamanho, loja))));
            Salvar(Painel(itens, 4), 1.5, pasta, $"00-atual-x-novo-{nome}.png");
        }
    }

    private static List<object[]> Consultar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        using var leitor = comando.ExecuteReader();
        var linhas = new List<object[]>();
        while (leitor.Read())
        {
            var valores = new object[leitor.FieldCount];
            leitor.GetValues(valores);
            linhas.Add(valores);
        }

        return linhas;
    }

    private static Produto LerProduto(SqliteConnection conexao, int id, string tamanho)
    {
        var l = Consultar(conexao,
            $"select Nome, Referencia, PrecoVendaCentavos, CodigoBarras, CodigoInterno from Produtos where Id = {id}")[0];
        var tamanhos = Consultar(conexao, $"select Tamanho, Ordem from TamanhosProduto where ProdutoId = {id}")
            .Select(t => new ProdutoTamanho { Tamanho = (string)t[0], Ordem = Convert.ToInt32(t[1], CultureInfo.InvariantCulture) })
            .ToList();
        if (tamanhos.All(t => t.Tamanho != tamanho))
            tamanhos.Add(new ProdutoTamanho { Tamanho = tamanho, Ordem = tamanhos.Count + 1 });

        return new Produto
        {
            Id = id,
            Nome = (string)l[0],
            Referencia = l[1] as string,
            PrecoVendaCentavos = Convert.ToInt32(l[2], CultureInfo.InvariantCulture),
            CodigoBarras = l[3] as string,
            CodigoInterno = l[4] as string,
            Tamanhos = tamanhos
        };
    }

    private static DrawingVisual Etiqueta(ModeloEtiquetaDesign modelo, Produto produto, string tamanho, string loja)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        EditorDesenhoService.Desenhar(dc, modelo, produto, tamanho, loja);
        return visual;
    }

    private static Visual Pagina(ModeloEtiquetaDesign modelo, double altura, double margemTopo,
        Produto p1, string t1, Produto p2, string t2, string loja)
    {
        var midia = PerfisMidia.RoloTagRoupas2Colunas50x75 with { AlturaEtiquetaMm = altura, MargemSuperiorMm = margemTopo };
        var itens = new[]
        {
            new ItemFila(p1, 1, t1),
            new ItemFila(p2, 1, t2),
        };
        var pagina = new BarTenderMidiaPaginator(itens, modelo, loja, midia).GetPage(0);

        // Linha cinza no meio só na imagem, para mostrar onde uma coluna acaba e a outra começa.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(pagina.Visual)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(pagina.Size)
            }, null, new Rect(pagina.Size));
            var x = pagina.Size.Width / 2;
            dc.DrawLine(new Pen(Brushes.LightGray, 1) { DashStyle = DashStyles.Dash }, new Point(x, 0), new Point(x, pagina.Size.Height));
        }

        return visual;
    }

    private static Visual ComFuro(DrawingVisual etiqueta)
    {
        var d = EditorDesenhoService.Dip(1);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawDrawing(etiqueta.Drawing);
            var area = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0x30, 0x30));
            dc.DrawRectangle(area, null, new Rect(0, 0, 50 * d, 10 * d));
            dc.DrawEllipse(null, new Pen(Brushes.Red, 1.5), new Point(25 * d, 5.5 * d), 2.5 * d, 2.5 * d);
            dc.DrawRectangle(null, new Pen(Brushes.Blue, 0.8) { DashStyle = DashStyles.Dash },
                new Rect(3 * d, 3 * d, 44 * d, 69 * d));
        }

        return visual;
    }

    private static Visual Painel(List<(string Titulo, DrawingVisual Visual)> itens, int colunas)
    {
        var d = EditorDesenhoService.Dip(1);
        double celulaL = 56 * d, celulaA = 83 * d;
        var linhas = (itens.Count + colunas - 1) / colunas;
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), null,
            new Rect(0, 0, colunas * celulaL, linhas * celulaA));
        for (var i = 0; i < itens.Count; i++)
        {
            var x = (i % colunas) * celulaL + 3 * d;
            var y = (i / colunas) * celulaA;
            var titulo = new FormattedText(itens[i].Titulo, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.Black, 1.0);
            dc.DrawText(titulo, new Point(x, y + 1 * d));
            var desenho = itens[i].Visual.Drawing;
            // O fundo branco da etiqueta mostra o tamanho dela (a de 63 mm fica menor).
            dc.PushTransform(new TranslateTransform(x, y + 6 * d));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 50 * d, 75 * d)));
            dc.DrawDrawing(desenho);
            dc.Pop();
            dc.Pop();
        }

        return visual;
    }

    private static BitmapSource Rasterizar(Visual visual, double ampliacao)
    {
        var area = ((DrawingVisual)visual).ContentBounds;
        var dpi = Dpi * ampliacao;
        var bmp = new RenderTargetBitmap(
            (int)Math.Round(area.Right * dpi / 96), (int)Math.Round(area.Bottom * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return bmp;
    }

    private static Visual PretoEBranco(DrawingVisual etiqueta)
    {
        var cinza = new FormatConvertedBitmap(Rasterizar(etiqueta, 1), PixelFormats.Gray8, null, 0);
        var pixels = new byte[cinza.PixelWidth * cinza.PixelHeight];
        cinza.CopyPixels(pixels, cinza.PixelWidth, 0);
        for (var i = 0; i < pixels.Length; i++) pixels[i] = pixels[i] < 128 ? (byte)0 : (byte)255;
        var preto = BitmapSource.Create(cinza.PixelWidth, cinza.PixelHeight, Dpi, Dpi, PixelFormats.Gray8, null, pixels, cinza.PixelWidth);

        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawImage(preto, new Rect(0, 0, preto.PixelWidth * 96 / Dpi, preto.PixelHeight * 96 / Dpi));
        return visual;
    }

    private static void Salvar(Visual visual, double ampliacao, string pasta, string nome)
    {
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(Rasterizar(visual, ampliacao)));
        using var arquivo = File.Create(Path.Combine(pasta, nome));
        png.Save(arquivo);
    }
}
