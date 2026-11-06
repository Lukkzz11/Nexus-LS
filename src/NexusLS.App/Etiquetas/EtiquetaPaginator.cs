using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.Core.Etiquetas;

namespace NexusLS.App.Etiquetas;

/// <summary>
/// Entrega ao Windows as páginas de etiquetas, uma por vez. O DocumentPaginator é o "contrato" do WPF para
/// imprimir: ele diz quantas páginas existem, o tamanho delas e devolve o desenho de cada uma.
/// </summary>
internal sealed class EtiquetaPaginator : DocumentPaginator
{
    private readonly List<List<PosicaoEtiqueta>> _paginas;
    private readonly FormatoEtiqueta _formato;
    private readonly bool _contornos;

    /// <param name="contornos">Desenha o contorno de todas as posições da folha (só na prévia, nunca na impressão).</param>
    public EtiquetaPaginator(IEnumerable<ItemEtiqueta> itens, FormatoEtiqueta formato, int primeiraPosicao, bool contornos = false)
    {
        _formato = formato;
        _contornos = contornos;
        _paginas = DiagramadorEtiquetas.Paginar(itens, formato, primeiraPosicao);
        PageSize = new Size(EtiquetaDesenho.Dip(formato.PaginaLarguraMm), EtiquetaDesenho.Dip(formato.PaginaAlturaMm));
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => Math.Max(1, _paginas.Count); // sem itens, ainda há uma página em branco para a prévia
    public override Size PageSize { get; set; }
    public override IDocumentPaginatorSource Source => null!;

    public override DocumentPage GetPage(int numeroDaPagina)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(PageSize));

            if (_contornos)
            {
                var traco = new Pen(new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)), 0.8) { DashStyle = DashStyles.Dash };
                for (var linha = 0; linha < _formato.Linhas; linha++)
                    for (var coluna = 0; coluna < _formato.Colunas; coluna++)
                        dc.DrawRectangle(null, traco, new Rect(
                            EtiquetaDesenho.Dip(_formato.MargemEsquerdaMm + coluna * (_formato.EtiquetaLarguraMm + _formato.EspacoHorizontalMm)),
                            EtiquetaDesenho.Dip(_formato.MargemSuperiorMm + linha * (_formato.EtiquetaAlturaMm + _formato.EspacoVerticalMm)),
                            EtiquetaDesenho.Dip(_formato.EtiquetaLarguraMm), EtiquetaDesenho.Dip(_formato.EtiquetaAlturaMm)));
            }

            if (numeroDaPagina < _paginas.Count)
            {
                foreach (var posicao in _paginas[numeroDaPagina])
                {
                    dc.PushTransform(new TranslateTransform(EtiquetaDesenho.Dip(posicao.XMm), EtiquetaDesenho.Dip(posicao.YMm)));
                    EtiquetaDesenho.Desenhar(dc, posicao.Item,
                        EtiquetaDesenho.Dip(_formato.EtiquetaLarguraMm), EtiquetaDesenho.Dip(_formato.EtiquetaAlturaMm));
                    dc.Pop();
                }
            }
        }
        return new DocumentPage(visual, PageSize, new Rect(PageSize), new Rect(PageSize));
    }

    /// <summary>Uma imagem da página, para a prévia da tela (<paramref name="escala"/> 2 = o dobro da resolução).</summary>
    public BitmapSource Imagem(int numeroDaPagina, double escala)
    {
        var pagina = GetPage(numeroDaPagina);
        var imagem = new RenderTargetBitmap(
            (int)Math.Ceiling(PageSize.Width * escala), (int)Math.Ceiling(PageSize.Height * escala),
            96 * escala, 96 * escala, PixelFormats.Pbgra32);
        imagem.Render(pagina.Visual);
        imagem.Freeze();
        return imagem;
    }
}

/// <summary>Imprime as etiquetas numa impressora do Windows (a pessoa escolhe a impressora na caixa de impressão).</summary>
internal static class ImpressoraEtiquetas
{
    /// <summary>Devolve true se imprimiu e false se a pessoa cancelou a caixa de impressão.</summary>
    public static bool Imprimir(IEnumerable<ItemEtiqueta> itens, FormatoEtiqueta formato, int primeiraPosicao)
    {
        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true) return false;

        // Informa o tamanho do papel ao driver (bobinas costumam vir configuradas para outro tamanho).
        try
        {
            if (formato.PaginaLarguraMm >= 200) // folha A4
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(System.Printing.PageMediaSizeName.ISOA4);
            else
                dialogo.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                    EtiquetaDesenho.Dip(formato.PaginaLarguraMm), EtiquetaDesenho.Dip(formato.PaginaAlturaMm));
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException or ArgumentException)
        {
            // Alguns drivers não aceitam trocar o papel por aqui: imprime com o que a impressora já tem configurado.
        }

        dialogo.PrintDocument(new EtiquetaPaginator(itens, formato, primeiraPosicao), "Etiquetas Nexus");
        return true;
    }
}
