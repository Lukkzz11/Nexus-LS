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
    /// <summary>
    /// Acha a fila escolhida na tela. Sem escolha, usa a impressora padrão do Windows. Uma impressora que sumiu
    /// vira erro: sem a janela de impressão, trocar calado mandaria as etiquetas para o lugar errado.
    /// </summary>
    public static System.Printing.PrintQueue LocalizarFila(string? nomeImpressora)
    {
        if (string.IsNullOrWhiteSpace(nomeImpressora) || nomeImpressora == ImpressoraPadraoDoWindows)
            return System.Printing.LocalPrintServer.GetDefaultPrintQueue();

        var servidor = new System.Printing.LocalPrintServer();
        var fila = servidor.GetPrintQueues(new[]
            {
                System.Printing.EnumeratedPrintQueueTypes.Local,
                System.Printing.EnumeratedPrintQueueTypes.Connections
            })
            .FirstOrDefault(f => string.Equals(f.FullName, nomeImpressora, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(f.Name, nomeImpressora, StringComparison.OrdinalIgnoreCase));

        return fila ?? throw new InvalidOperationException(
            $"A impressora \"{nomeImpressora}\" não foi encontrada. Ligue-a ou escolha outra na lista de impressoras.");
    }

    public const string ImpressoraPadraoDoWindows = "Impressora Padrão do Windows";

    /// <summary>Prepara o envio direto para a fila, sem abrir a janela de impressão do Windows.</summary>
    private static PrintDialog PrepararImpressao(string? nomeImpressora, double larguraMm, double alturaMm)
    {
        var dialogo = new PrintDialog { PrintQueue = LocalizarFila(nomeImpressora) };

        try
        {
            if (larguraMm >= 200) // Folha A4
            {
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    System.Printing.PageMediaSizeName.ISOA4);
            }
            else // Bobina termica
            {
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    DesenhoEtiquetaService.Dip(larguraMm),
                    DesenhoEtiquetaService.Dip(alturaMm));
            }
        }
        catch (Exception ex)
        {
            // Drivers de impressoras termicas que nao aceitam tamanho programatico
            App.Log(ex, "Nao foi possivel configurar o tamanho de pagina no PrintTicket");
        }

        return dialogo;
    }

    public static bool Imprimir(
        IEnumerable<ItemFila> itens,
        ConfiguracaoEtiqueta config,
        int primeiraPosicao = 1,
        string? nomeImpressora = null)
    {
        var listaItens = itens.ToList();
        if (listaItens.Count == 0) return false;

        var formatoCore = config.Formato.FormatoCore;
        var dialogo = PrepararImpressao(nomeImpressora, formatoCore.PaginaLarguraMm, formatoCore.PaginaAlturaMm);

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

        var dialogo = PrepararImpressao(nomeImpressora, modelo.LarguraMm, modelo.AlturaMm);

        var paginator = new BarTenderDesignPaginator(listaItens, modelo, nomeLoja, primeiraPosicao);
        dialogo.PrintDocument(paginator, $"Nexus BarTender - {modelo.Nome}");
        return true;
    }

    /// <summary>
    /// Impressão a partir da página de preparação: usa a mídia configurada (rolo com gap,
    /// marca preta, contínuo ou folha) e o modelo já ajustado às medidas dela.
    /// Vai direto para a fila da impressora escolhida.
    /// </summary>
    public static bool ImprimirComMidia(
        IEnumerable<ItemFila> itens,
        ModeloEtiquetaDesign modelo,
        string nomeLoja,
        PerfilMidia midia,
        int primeiraPosicao = 1,
        string? nomeImpressora = null)
    {
        var listaItens = itens.ToList();
        if (listaItens.Count == 0) return false;
        if (midia is null) throw new ArgumentNullException(nameof(midia));

        var dialogo = PrepararImpressao(nomeImpressora, midia.PaginaLarguraMm, midia.PaginaAlturaMm);

        try
        {
            dialogo.PrintTicket.PageMediaSize = midia.Tipo == TipoMidiaEtiqueta.Folha
                ? new System.Printing.PageMediaSize(System.Printing.PageMediaSizeName.ISOA4)
                : new System.Printing.PageMediaSize(
                    DesenhoEtiquetaService.Dip(midia.PaginaLarguraMm),
                    DesenhoEtiquetaService.Dip(midia.PaginaAlturaMm));
        }
        catch (Exception ex)
        {
            // Drivers de térmicas que não aceitam tamanho programático: segue com o papel do driver.
            App.Log(ex, "Não foi possível configurar a mídia no PrintTicket; usando a configuração do driver.");
        }

        var paginator = new BarTenderMidiaPaginator(listaItens, modelo, nomeLoja, midia, primeiraPosicao);
        dialogo.PrintDocument(paginator, $"Nexus BarTender - {midia.Nome}");
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

/// <summary>
/// Paginador da preparação: distribui cada cópia na posição da mídia escolhida.
/// Em rolo de uma coluna, cada etiqueta ocupa uma página; em folha A4, 21 por página.
/// </summary>
public class BarTenderMidiaPaginator : DocumentPaginator
{
    private readonly List<List<PosicaoItemBarTender>> _paginas = new();
    private readonly ModeloEtiquetaDesign _modelo;
    private readonly string _nomeLoja;
    private readonly PerfilMidia _midia;

    public BarTenderMidiaPaginator(
        IEnumerable<ItemFila> itens,
        ModeloEtiquetaDesign modelo,
        string nomeLoja,
        PerfilMidia midia,
        int primeiraPosicao = 1)
    {
        _modelo = modelo;
        _nomeLoja = nomeLoja;
        _midia = midia;

        PageSize = new Size(
            EditorDesenhoService.Dip(midia.PaginaLarguraMm),
            EditorDesenhoService.Dip(midia.PaginaAlturaMm));

        Paginar(itens, primeiraPosicao);
    }

    private void Paginar(IEnumerable<ItemFila> itens, int primeiraPosicao)
    {
        var porPagina = Math.Max(1, _midia.EtiquetasPorPagina);
        var colunas = Math.Max(1, _midia.Colunas);
        var indice = Math.Max(0, primeiraPosicao - 1);

        foreach (var item in itens)
        {
            for (var copia = 0; copia < item.Quantidade; copia++, indice++)
            {
                var pagina = indice / porPagina;
                var naPagina = indice % porPagina;
                var coluna = naPagina % colunas;
                var linha = naPagina / colunas;

                while (_paginas.Count <= pagina)
                    _paginas.Add(new List<PosicaoItemBarTender>());

                var xMm = _midia.MargemEsquerdaMm + coluna * (_midia.LarguraEtiquetaMm + _midia.EspacoHorizontalMm);
                var yMm = _midia.MargemSuperiorMm + linha * (_midia.AlturaEtiquetaMm + _midia.EspacoVerticalMm);

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

            if (pageNumber >= 0 && pageNumber < _paginas.Count)
            {
                foreach (var pos in _paginas[pageNumber])
                {
                    dc.PushTransform(new TranslateTransform(
                        EditorDesenhoService.Dip(pos.XMm),
                        EditorDesenhoService.Dip(pos.YMm)));

                    EditorDesenhoService.Desenhar(dc, _modelo, pos.Produto, pos.Tamanho, _nomeLoja, desenharBordaExterna: false);

                    dc.Pop();
                }
            }
        }

        return new DocumentPage(visual, PageSize, new Rect(PageSize), new Rect(PageSize));
    }
}

