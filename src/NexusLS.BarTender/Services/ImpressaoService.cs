using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;

namespace NexusLS.BarTender.Services;

public class ImpressaoService
{
    public static bool Imprimir(
        IEnumerable<ItemFila> itens,
        ConfiguracaoEtiqueta config,
        int primeiraPosicao = 1,
        string? nomeImpressora = null)
    {
        var listaItens = itens.ToList();
        if (listaItens.Count == 0) return false;

        var dialogo = new PrintDialog();
        if (!string.IsNullOrWhiteSpace(nomeImpressora))
        {
            try
            {
                var server = new System.Printing.LocalPrintServer();
                dialogo.PrintQueue = server.GetPrintQueue(nomeImpressora);
            }
            catch { }
        }
        if (dialogo.ShowDialog() != true) return false;

        var formatoCore = config.Formato.FormatoCore;

        try
        {
            if (formatoCore.PaginaLarguraMm >= 200) // Folha A4
            {
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    System.Printing.PageMediaSizeName.ISOA4);
            }
            else // Bobina térmica (50x30, 40x25, 60x40, etc.)
            {
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    DesenhoEtiquetaService.Dip(formatoCore.PaginaLarguraMm),
                    DesenhoEtiquetaService.Dip(formatoCore.PaginaAlturaMm));
            }
        }
        catch
        {
            // Alguns drivers de impressora térmica não aceitam configurar o papel programaticamente
        }

        var paginator = new BarTenderPaginator(listaItens, config, primeiraPosicao);
        dialogo.PrintDocument(paginator, "Nexus BarTender - Impressão de Etiquetas");
        return true;
    }

    public static bool ImprimirDesign(
        IEnumerable<ItemFila> itens,
        ModeloEtiquetaDesign modelo,
        string nomeLoja,
        int primeiraPosicao = 1,
        string? nomeImpressora = null)
    {
        var listaItens = itens.ToList();
        if (listaItens.Count == 0) return false;

        var dialogo = new PrintDialog();
        if (!string.IsNullOrWhiteSpace(nomeImpressora))
        {
            try
            {
                var server = new System.Printing.LocalPrintServer();
                dialogo.PrintQueue = server.GetPrintQueue(nomeImpressora);
            }
            catch { }
        }
        if (dialogo.ShowDialog() != true) return false;

        try
        {
            if (modelo.LarguraMm >= 200)
            {
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    System.Printing.PageMediaSizeName.ISOA4);
            }
            else
            {
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    EditorDesenhoService.Dip(modelo.LarguraMm),
                    EditorDesenhoService.Dip(modelo.AlturaMm));
            }
        }
        catch { }

        var paginator = new BarTenderDesignPaginator(listaItens, modelo, nomeLoja, primeiraPosicao);
        dialogo.PrintDocument(paginator, $"Nexus BarTender - {modelo.Nome}");
        return true;
    }
}

public class BarTenderPaginator : DocumentPaginator
{
    private readonly List<List<PosicaoItemBarTender>> _paginas = new();
    private readonly FormatoEtiqueta _formato;
    private readonly ConfiguracaoEtiqueta _config;

    public BarTenderPaginator(
        IEnumerable<ItemFila> itens,
        ConfiguracaoEtiqueta config,
        int primeiraPosicao = 1)
    {
        _config = config;
        _formato = config.Formato.FormatoCore;
        PageSize = new Size(
            DesenhoEtiquetaService.Dip(_formato.PaginaLarguraMm),
            DesenhoEtiquetaService.Dip(_formato.PaginaAlturaMm));

        Paginar(itens, primeiraPosicao);
    }

    private void Paginar(IEnumerable<ItemFila> itens, int primeiraPosicao)
    {
        var porPagina = _formato.EtiquetasPorPagina;
        if (porPagina < 1) porPagina = 1;

        var indice = Math.Max(0, primeiraPosicao - 1);

        foreach (var item in itens)
        {
            for (var copia = 0; copia < item.Quantidade; copia++, indice++)
            {
                var pagina = indice / porPagina;
                var naPagina = indice % porPagina;
                var coluna = naPagina % _formato.Colunas;
                var linha = naPagina / _formato.Colunas;

                while (_paginas.Count <= pagina)
                {
                    _paginas.Add(new List<PosicaoItemBarTender>());
                }

                var xMm = _formato.MargemEsquerdaMm + (coluna * (_formato.EtiquetaLarguraMm + _formato.EspacoHorizontalMm));
                var yMm = _formato.MargemSuperiorMm + (linha * (_formato.EtiquetaAlturaMm + _formato.EspacoVerticalMm));

                _paginas[pagina].Add(new PosicaoItemBarTender(xMm, yMm, item.Produto, item.Tamanho));
            }
        }
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => Math.Max(1, _paginas.Count);
    public override Size PageSize { get; set; }
    public override IDocumentPaginatorSource Source => null!;

    public override DocumentPage GetPage(int pageNumber)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(PageSize));

            if (pageNumber < _paginas.Count)
            {
                var largDip = DesenhoEtiquetaService.Dip(_formato.EtiquetaLarguraMm);
                var altDip = DesenhoEtiquetaService.Dip(_formato.EtiquetaAlturaMm);

                foreach (var pos in _paginas[pageNumber])
                {
                    dc.PushTransform(new TranslateTransform(
                        DesenhoEtiquetaService.Dip(pos.XMm),
                        DesenhoEtiquetaService.Dip(pos.YMm)));

                    DesenhoEtiquetaService.Desenhar(dc, pos.Produto, pos.Tamanho, _config, largDip, altDip);

                    dc.Pop();
                }
            }
        }

        return new DocumentPage(visual, PageSize, new Rect(PageSize), new Rect(PageSize));
    }
}

public class BarTenderDesignPaginator : DocumentPaginator
{
    private readonly List<List<PosicaoItemBarTender>> _paginas = new();
    private readonly ModeloEtiquetaDesign _modelo;
    private readonly string _nomeLoja;

    public BarTenderDesignPaginator(
        IEnumerable<ItemFila> itens,
        ModeloEtiquetaDesign modelo,
        string nomeLoja,
        int primeiraPosicao = 1)
    {
        _modelo = modelo;
        _nomeLoja = nomeLoja;

        PageSize = new Size(
            EditorDesenhoService.Dip(modelo.LarguraMm),
            EditorDesenhoService.Dip(modelo.AlturaMm));

        Paginar(itens);
    }

    private void Paginar(IEnumerable<ItemFila> itens)
    {
        // Para bobina/modelo individual: 1 etiqueta por página
        foreach (var item in itens)
        {
            for (var copia = 0; copia < item.Quantidade; copia++)
            {
                var pagina = new List<PosicaoItemBarTender>
                {
                    new(0, 0, item.Produto, item.Tamanho)
                };
                _paginas.Add(pagina);
            }
        }
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => Math.Max(1, _paginas.Count);
    public override Size PageSize { get; set; }
    public override IDocumentPaginatorSource Source => null!;

    public override DocumentPage GetPage(int pageNumber)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(PageSize));

            if (pageNumber < _paginas.Count)
            {
                foreach (var pos in _paginas[pageNumber])
                {
                    var modeloEfetivo = _modelo;
                    if (GerenciadorEtiquetasProdutoService.TemEtiquetaPersonalizada(pos.Produto.Id))
                    {
                        var modeloCustom = GerenciadorEtiquetasProdutoService.CarregarEtiquetaProduto(pos.Produto.Id);
                        if (modeloCustom != null)
                        {
                            modeloEfetivo = modeloCustom;
                        }
                    }
                    EditorDesenhoService.Desenhar(dc, modeloEfetivo, pos.Produto, pos.Tamanho, _nomeLoja, desenharBordaExterna: false);
                }
            }
        }

        return new DocumentPage(visual, PageSize, new Rect(PageSize), new Rect(PageSize));
    }
}

public record PosicaoItemBarTender(double XMm, double YMm, Produto Produto, string? Tamanho);
