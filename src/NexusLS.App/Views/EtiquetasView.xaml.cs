using System.Windows;
using System.Windows.Controls;
using NexusLS.App.Etiquetas;
using NexusLS.App.Impressao;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class EtiquetasView : UserControl
{
    public EtiquetasView()
    {
        InitializeComponent();
        Loaded += AoCarregar;
        Unloaded += (_, _) =>
        {
            if (Vm is { } vm) vm.PreviaMudou -= Redesenhar;
        };
    }

    /// <summary>A última impressora usada, enquanto o programa está aberto.</summary>
    private static string? _impressoraEscolhida;

    private EtiquetasViewModel? Vm => DataContext as EtiquetasViewModel;

    private void AoCarregar(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        // Cuidados de "janela" ficam aqui na View: a impressora escolhida e a prévia desenhada.
        ComboImpressora.ItemsSource = new[] { ImpressoraWindows.ImpressoraPadrao }.Concat(ImpressoraWindows.ListarImpressoras()).ToList();
        ComboImpressora.SelectedItem = _impressoraEscolhida;
        if (ComboImpressora.SelectedItem is null) ComboImpressora.SelectedIndex = 0;
        vm.ImprimirEtiquetas = (itens, formato, posicao) =>
        {
            _impressoraEscolhida = ComboImpressora.SelectedItem as string;
            return ImpressoraEtiquetas.Imprimir(itens, formato, posicao, _impressoraEscolhida);
        };
        vm.PreviaMudou -= Redesenhar;
        vm.PreviaMudou += Redesenhar;
        Redesenhar();
        CaixaBusca.Focus();
    }

    /// <summary>Desenha a primeira página das etiquetas (com o contorno de cada posição da folha) na prévia.</summary>
    private void Redesenhar()
    {
        if (Vm is not { } vm) return;

        var paginador = new EtiquetaPaginator(vm.ItensParaImprimir(), vm.FormatoSelecionado, vm.PrimeiraPosicaoValida, contornos: true);
        var escala = vm.FormatoSelecionado.PaginaLarguraMm >= 200 ? 1.2 : 4.0; // bobinas são pequenas: desenha com mais detalhe
        PreviaImagem.Source = paginador.Imagem(0, escala);
    }
}
