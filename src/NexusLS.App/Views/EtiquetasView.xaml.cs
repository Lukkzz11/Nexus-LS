using System.Windows;
using System.Windows.Controls;
using NexusLS.App.Etiquetas;
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

    private EtiquetasViewModel? Vm => DataContext as EtiquetasViewModel;

    private void AoCarregar(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        // Cuidados de "janela" ficam aqui na View: a caixa de impressão do Windows e a prévia desenhada.
        vm.ImprimirEtiquetas = ImpressoraEtiquetas.Imprimir;
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
