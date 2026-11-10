using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Impressao;

/// <summary>
/// Imprime o cupom numa impressora do Windows (térmica de bobina ou comum). O texto já vem pronto do
/// MontadorCupom, em colunas fixas; aqui ele só é posto num documento de fonte de largura fixa e enviado
/// à impressora configurada (ou à padrão do Windows), sem abrir caixa de diálogo.
/// </summary>
public class ImpressoraWindows : IImpressoraCupom
{
    public const string ImpressoraPadrao = "(Impressora padrão do Windows)";

    /// <summary>Nomes das impressoras instaladas. Lista vazia se o Windows não conseguir informar.</summary>
    public static IReadOnlyList<string> ListarImpressoras()
    {
        try
        {
            using var servidor = new LocalPrintServer();
            return servidor.GetPrintQueues().Select(f => f.Name).OrderBy(n => n).ToList();
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    public Task ImprimirAsync(string texto, ConfiguracaoLoja loja)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("A impressão só funciona com o programa aberto.");

        // A impressão do WPF precisa rodar na thread da interface. Prioridade baixa: deixa a tela
        // de sucesso aparecer antes de começar a imprimir.
        return dispatcher.InvokeAsync(() => Imprimir(texto, loja), DispatcherPriority.Background).Task;
    }

    private static void Imprimir(string texto, ConfiguracaoLoja loja)
    {
        PrintQueue fila;
        try
        {
            using var servidor = new LocalPrintServer();
            fila = string.IsNullOrWhiteSpace(loja.ImpressoraCupom)
                ? LocalPrintServer.GetDefaultPrintQueue()
                : servidor.GetPrintQueues().FirstOrDefault(f => f.Name == loja.ImpressoraCupom)
                  ?? throw new InvalidOperationException(
                      $"A impressora \"{loja.ImpressoraCupom}\" não foi encontrada. Confira em Configurações.");
        }
        catch (PrintSystemException)
        {
            throw new InvalidOperationException("Nenhuma impressora disponível. Instale uma ou escolha outra em Configurações.");
        }

        var dialogo = new PrintDialog { PrintQueue = fila };
        var largura = dialogo.PrintableAreaWidth > 0 ? dialogo.PrintableAreaWidth : 300;
        var altura = dialogo.PrintableAreaHeight > 0 ? dialogo.PrintableAreaHeight : 1000;
        const double margem = 6;

        // Consolas tem cada letra com ~0,55 da altura da fonte: escolhe o tamanho para a linha cheia caber na bobina.
        var fonte = Math.Clamp((largura - 2 * margem) / (loja.LarguraCupom * 0.55), 6, 14);
        var disponivel = largura - 2 * margem;

        var blocos = new List<Block>();
        var paragrafo = new Paragraph { Margin = new Thickness(0) };
        foreach (var linha in NexusLS.Core.Services.MontadorCupom.Estilizar(texto))
        {
            // Nome da loja: linha própria, centralizada e com letra maior (sem os espaços de centralização, que
            // no tamanho maior empurrariam o nome para fora da bobina).
            if (linha.Any(s => s.Grande))
            {
                if (paragrafo.Inlines.Count > 0)
                {
                    blocos.Add(paragrafo);
                    paragrafo = new Paragraph { Margin = new Thickness(0) };
                }

                var nome = string.Concat(linha.Select(s => s.Texto)).Trim();
                // Não passa de 1,4x e diminui se o nome for comprido, para nunca sair do papel.
                var tamanho = Math.Max(fonte, Math.Min(fonte * 1.4, disponivel / (Math.Max(1, nome.Length) * 0.55)));
                blocos.Add(new Paragraph(new Run(nome) { FontWeight = FontWeights.Bold, FontSize = tamanho })
                {
                    Margin = new Thickness(0),
                    TextAlignment = TextAlignment.Center
                });
                continue;
            }

            foreach (var s in linha)
                paragrafo.Inlines.Add(new Run(s.Texto)
                {
                    FontWeight = s.Negrito ? FontWeights.Bold : FontWeights.Normal,
                    // As demais linhas mantêm o tamanho para as colunas continuarem alinhadas.
                    FontSize = fonte
                });
            paragrafo.Inlines.Add(new LineBreak());
        }
        if (paragrafo.Inlines.LastOrDefault() is LineBreak) paragrafo.Inlines.Remove(paragrafo.Inlines.Last());
        if (paragrafo.Inlines.Count > 0) blocos.Add(paragrafo);

        var documento = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = fonte,
            PagePadding = new Thickness(margem),
            PageWidth = largura,
            ColumnWidth = largura,
            PageHeight = altura
        };
        // Logo no topo, centralizado e em preto e branco puro (a térmica não imprime cinza). Vai pelo driver do Windows,
        // junto com o texto.
        if (loja.CupomMostrarLogo && LogoCupom.ParaImpressao(loja.LogoCupom) is { } logo)
        {
            var largLogo = Math.Min(logo.PixelWidth, (largura - 2 * margem) * 0.7);
            documento.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Image
            {
                Source = logo,
                Width = largLogo,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            }));
        }
        foreach (var bloco in blocos) documento.Blocks.Add(bloco);

        dialogo.PrintDocument(((IDocumentPaginatorSource)documento).DocumentPaginator, "Cupom Nexus");
    }
}
