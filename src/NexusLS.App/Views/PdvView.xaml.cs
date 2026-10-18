using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class PdvView : UserControl
{
    public PdvView()
    {
        InitializeComponent();
        Loaded += AoCarregar;
        Unloaded += AoDescarregar;
        PreviewKeyDown += AoTeclar;
    }

    private PdvViewModel? Vm => DataContext as PdvViewModel;

    private void AoCarregar(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            // Cuidados de "janela" ficam aqui na View, não no ViewModel.
            vm.Confirmar = mensagem =>
                MessageBox.Show(Window.GetWindow(this), mensagem, "Nexus LS",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            vm.FocoNaBuscaSolicitado += AoPedirFoco;
            vm.FocoNoValorRecebidoSolicitado += AoPedirFocoRecebido;
        }
        FocarBusca();
    }

    // O mesmo ViewModel do PDV vive o programa todo, mas a View é recriada a cada visita à tela.
    // Por isso desligamos o evento ao sair, senão Views antigas continuariam "escutando".
    private void AoDescarregar(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            vm.FocoNaBuscaSolicitado -= AoPedirFoco;
            vm.FocoNoValorRecebidoSolicitado -= AoPedirFocoRecebido;
        }
    }

    private void AoPedirFoco() => Dispatcher.BeginInvoke(FocarBusca);

    // Prioridade "Input": espera o campo aparecer na tela (ele some quando o pagamento não é dinheiro).
    private void AoPedirFocoRecebido() => Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Input,
        () =>
        {
            CaixaRecebido.Focus();
            CaixaRecebido.SelectAll();
        });

    private void FocarBusca()
    {
        CaixaBusca.Focus();
        CaixaBusca.SelectAll();
    }

    /// <summary>
    /// F2 e F10 tratados aqui. O F10 é uma "tecla de sistema" no Windows (ativa o menu da janela),
    /// por isso o WPF não o entrega como uma tecla normal e um KeyBinding no XAML não funcionaria.
    /// </summary>
    private void AoTeclar(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            FocarBusca();
            e.Handled = true;
        }
        else if (e.Key == Key.System && e.SystemKey == Key.F10 && Vm is { } vm)
        {
            vm.FinalizarCommand.Execute(null);
            e.Handled = true;
        }
    }
}
