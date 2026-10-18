using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

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
                    MessageBox.Show(Window.GetWindow(this), mensagem, "Nexus LS",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                vm.PedirCancelamento = PedirMotivo;
                vm.EscolherArquivoParaSalvar = SalvarComo;
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
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            FileName = nomeSugerido,
            DefaultExt = ".csv",
            Filter = "Planilha CSV (Excel)|*.csv"
        };
        return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
    }
}
