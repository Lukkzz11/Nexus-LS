using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class PdvView : UserControl
{
    public PdvView()
    {
        InitializeComponent();
        Loaded += AoCarregar;
        Unloaded += AoDescarregar;
        PreviewKeyDown += AoTeclarPrevio;
        KeyDown += AoTeclar;
        PreviewTextInput += AoDigitar;
    }

    /// <summary>Na janela de tamanho, digitar a letra ou o número do tamanho salta direto para ele.</summary>
    private void AoDigitar(object sender, TextCompositionEventArgs e)
    {
        if (Vm is { } vm && vm.Etapa == EtapaPdv.Tamanho)
            e.Handled = vm.ProcessarTexto(e.Text);
    }

    /// <summary>O campo da quantidade só aceita números.</summary>
    private void SoNumeros(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(c => !char.IsAsciiDigit(c));

    private PdvViewModel? Vm => DataContext as PdvViewModel;

    private void AoCarregar(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            // Cuidados de "janela" ficam aqui na View, não no ViewModel.
            vm.Confirmar = mensagem =>
                MessageBox.Show(Window.GetWindow(this), mensagem, "Nexus",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            vm.FocoSolicitado += AoPedirFoco;
            vm.CobrarPix = (chave, valor) =>
            {
                var janela = new PixCobrancaWindow(App.Servicos.Pix, chave, valor) { Owner = Window.GetWindow(this) };
                janela.ShowDialog();
                return Task.FromResult(janela.Aprovado);
            };
            _ = vm.AtualizarCaixaAsync();
        }
        Focar(AlvoFoco.Busca);
    }

    // O mesmo ViewModel do PDV vive o programa todo, mas a View é recriada a cada visita à tela.
    // Por isso desligamos o evento ao sair, senão Views antigas continuariam "escutando".
    private void AoDescarregar(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.FocoSolicitado -= AoPedirFoco;
    }

    private void AoPedirFoco(AlvoFoco alvo) =>
        // Prioridade baixa: espera a tela terminar de mostrar/esconder as janelas antes de mover o cursor.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Focar(alvo));

    private void Focar(AlvoFoco alvo)
    {
        switch (alvo)
        {
            case AlvoFoco.Busca:
                CaixaBusca.Focus();
                CaixaBusca.SelectAll();
                break;
            case AlvoFoco.Quantidade:
                CaixaQuantidade.Focus();
                CaixaQuantidade.SelectAll(); // o "1" fica selecionado: é só digitar o número para trocar
                break;
            case AlvoFoco.Desconto:
                CaixaDesconto.Focus();
                CaixaDesconto.SelectAll();
                break;
            case AlvoFoco.ValorRecebido:
                CaixaRecebido.Focus();
                CaixaRecebido.SelectAll();
                break;
            case AlvoFoco.Cliente:
                CaixaCliente.Focus();
                CaixaCliente.SelectAll();
                break;
            default:
                Sobreposicao.Focus(); // modais sem campo de texto: o próprio painel recebe as setas
                break;
        }
    }

    /// <summary>
    /// Teclas que precisam ser vistas ANTES dos campos de texto (que usariam as setas para mexer o cursor).
    /// </summary>
    private void AoTeclarPrevio(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;

        // Proteção: tecla Enter segurada gera vários Enter; só o primeiro vale.
        if (e.Key == Key.Enter && e.IsRepeat)
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F2:
                vm.PedirBusca();
                Focar(AlvoFoco.Busca);
                e.Handled = true;
                return;
            case Key.F8:
                vm.AlternarTabelaCommand.Execute(null);
                e.Handled = true;
                return;
            // Espaço com a busca VAZIA troca varejo/atacado. Com texto digitado, o espaço é só um espaço (ex.: "camiseta azul").
            case Key.Space when vm.Etapa == EtapaPdv.Busca && vm.Busca.Length == 0 && CaixaBusca.IsKeyboardFocused:
                e.Handled = vm.ProcessarTecla(ComandoTecla.Espaco);
                return;
            // O F10 é uma "tecla de sistema" no Windows (ativa o menu da janela): o WPF não o entrega como
            // tecla normal, e um KeyBinding no XAML não funcionaria. Por isso é tratado aqui.
            case Key.F10:
            case Key.System when e.SystemKey == Key.F10:
                e.Handled = vm.ProcessarTecla(ComandoTecla.F10);
                return;
            case Key.Delete when vm.Etapa == EtapaPdv.Busca && vm.ItemSelecionado is not null:
                vm.RemoverSelecionadoCommand.Execute(null);
                e.Handled = true;
                return;
        }

        // Campos da linha do carrinho: Enter confirma o valor digitado e volta para a busca; as setas ficam com o campo.
        if (e.OriginalSource is TextBox caixa && caixa.DataContext is ItemCarrinhoViewModel)
        {
            if (e.Key == Key.Enter)
            {
                BindingOperations.GetBindingExpression(caixa, TextBox.TextProperty)?.UpdateSource();
                Focar(AlvoFoco.Busca);
                e.Handled = true;
            }
            return;
        }

        var comando = e.Key switch
        {
            Key.Left => (ComandoTecla?)ComandoTecla.Esquerda,
            Key.Right => ComandoTecla.Direita,
            Key.Up => ComandoTecla.Cima,
            Key.Down => ComandoTecla.Baixo,
            _ => null
        };
        if (comando is { } c && vm.ProcessarTecla(c)) e.Handled = true;
    }

    /// <summary>
    /// Enter e Esc: tratados na fase de "subida" do evento. Se um botão ou outro elemento já tratou o Enter
    /// (e.Handled), o fluxo do PDV não o recebe de novo.
    /// </summary>
    private void AoTeclar(object sender, KeyEventArgs e)
    {
        if (e.Handled || Vm is not { } vm) return;

        if (e.Key == Key.Enter && !e.IsRepeat)
            e.Handled = vm.ProcessarTecla(ComandoTecla.Enter);
        else if (e.Key == Key.Escape)
            e.Handled = vm.ProcessarTecla(ComandoTecla.Esc);
    }
}
