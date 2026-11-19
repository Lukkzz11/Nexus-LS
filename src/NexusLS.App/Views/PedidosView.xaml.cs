using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

public partial class PedidosView : UserControl
{
    private Point _inicioDoArraste;
    private CartaoPedido? _cartaoApertado;

    public PedidosView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not PedidosViewModel vm) return;
            vm.PedirMotivo = pergunta => DialogoMotivo.Pedir(System.Windows.Window.GetWindow(this), "Cancelar pedido", pergunta);
        };
    }

    private void AoDarDoisCliques(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PedidosViewModel vm && vm.AdicionarProdutoCommand.CanExecute(null))
            vm.AdicionarProdutoCommand.Execute(null);
    }

    // ----- arrastar e soltar o cartão entre as etapas (os botões Avançar/Voltar continuam valendo) -----

    private void AoApertarCartao(object sender, MouseButtonEventArgs e)
    {
        _cartaoApertado = (sender as FrameworkElement)?.DataContext as CartaoPedido;
        _inicioDoArraste = e.GetPosition(null);
    }

    private void AoMoverCartao(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _cartaoApertado is not { } cartao) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _inicioDoArraste.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _inicioDoArraste.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _cartaoApertado = null;
        if (DataContext is not PedidosViewModel vm) return;
        vm.MarcarDestinos(cartao.Status, null);
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(CartaoPedido), cartao), DragDropEffects.Move); }
        finally { vm.MarcarDestinos(null, null); }
    }

    private void AoArrastarSobre(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (DataContext is not PedidosViewModel vm
            || (sender as FrameworkElement)?.DataContext is not ColunaPedido coluna
            || e.Data.GetData(typeof(CartaoPedido)) is not CartaoPedido cartao) return;

        vm.MarcarDestinos(cartao.Status, coluna);
        if (RegrasPedido.MotivoDeNaoPoderMover(cartao.Status, coluna.Status) is null) e.Effects = DragDropEffects.Move;
    }

    private void AoSairDoArraste(object sender, DragEventArgs e)
    {
        if (DataContext is PedidosViewModel vm && e.Data.GetData(typeof(CartaoPedido)) is CartaoPedido cartao)
            vm.MarcarDestinos(cartao.Status, null);
    }

    private void AoSoltarNaColuna(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not PedidosViewModel vm
            || (sender as FrameworkElement)?.DataContext is not ColunaPedido coluna
            || e.Data.GetData(typeof(CartaoPedido)) is not CartaoPedido cartao) return;
        vm.MarcarDestinos(null, null);
        _ = vm.MoverPedidoAsync(cartao.Id, coluna.Status);
    }

    /// <summary>Teclado: Alt + seta direita avança o cartão focado; Alt + seta esquerda volta.</summary>
    private void AoTeclarNoCartao(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla is not (Key.Right or Key.Left)) return;
        if (DataContext is not PedidosViewModel vm || (sender as FrameworkElement)?.DataContext is not CartaoPedido cartao) return;

        vm.SelecionarCommand.Execute(cartao);
        var comando = tecla == Key.Right ? vm.AvancarCommand : vm.VoltarCommand;
        if (comando.CanExecute(null)) comando.Execute(null);
        e.Handled = true;
    }
}
