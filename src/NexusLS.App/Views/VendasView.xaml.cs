using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

/// <summary>Texto do selo da lista: "Pedido PED-..." se a venda já foi enviada para Pedidos, senão vazio.</summary>
public sealed class SeloPedidoConverter : System.Windows.Data.IMultiValueConverter
{
    public static readonly SeloPedidoConverter Instancia = new();

    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        values.Length == 2 && values[0] is int id && values[1] is IReadOnlyDictionary<int, string> selos && selos.TryGetValue(id, out var numero)
            ? $"Enviada · {numero}" : "";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class VendasView : UserControl
{
    public VendasView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VendasViewModel vm)
            {
                vm.Confirmar = mensagem =>
                    MessageBox.Show(Window.GetWindow(this), mensagem, "Nexus",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                vm.PedirCancelamento = PedirMotivo;
                vm.EscolherArquivoParaSalvar = SalvarComo;
                if (vm.Fechamento is { } fechamento) fechamento.Confirmar = vm.Confirmar;
            }
        };
    }

    /// <summary>Janelinha que pergunta o motivo do cancelamento e se o estoque volta.</summary>
    private DadosCancelamento? PedirMotivo(string pergunta)
    {
        var motivo = new TextBox { MinWidth = 340, Margin = new Thickness(0, 6, 0, 10) };
        var devolver = new CheckBox { Content = "Devolver os itens ao estoque", IsChecked = true, Margin = new Thickness(0, 0, 0, 14) };
        var erro = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 8) };
        var confirmar = new Button { Content = "Cancelar venda", IsDefault = true, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        var voltar = new Button { Content = "Voltar", IsCancel = true, MinWidth = 80 };

        var painel = new StackPanel { Margin = new Thickness(18) };
        painel.Children.Add(new TextBlock { Text = pergunta, FontWeight = FontWeights.SemiBold });
        painel.Children.Add(new TextBlock { Text = "Motivo (obrigatório):", Margin = new Thickness(0, 10, 0, 0) });
        painel.Children.Add(motivo);
        painel.Children.Add(devolver);
        painel.Children.Add(erro);
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        botoes.Children.Add(confirmar);
        botoes.Children.Add(voltar);
        painel.Children.Add(botoes);

        var janela = new Window
        {
            Title = "Cancelar venda",
            Content = painel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this)
        };
        confirmar.Click += (_, _) =>
        {
            if (motivo.Text.Trim().Length < 3) { erro.Text = "Informe o motivo (pelo menos 3 letras)."; motivo.Focus(); return; }
            janela.DialogResult = true;
        };
        janela.Loaded += (_, _) => motivo.Focus();

        return janela.ShowDialog() == true ? new DadosCancelamento(motivo.Text.Trim(), devolver.IsChecked == true) : null;
    }

    /// <summary>A caixa "Salvar como" do Windows. Devolve o caminho escolhido ou nulo se cancelou.</summary>
    private string? SalvarComo(string nomeSugerido)
    {
        var pdf = nomeSugerido.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            FileName = nomeSugerido,
            DefaultExt = pdf ? ".pdf" : ".csv",
            Filter = pdf ? "Documento PDF|*.pdf" : "Planilha CSV (Excel)|*.csv|Relatório em PDF (padrão Nexus)|*.pdf"
        };
        return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
    }
}
