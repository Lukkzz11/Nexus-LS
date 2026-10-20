using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class FinanceiroView : UserControl
{
    public FinanceiroView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not FinanceiroViewModel vm) return;
            // Caixas de janela ficam aqui na View, não no ViewModel.
            vm.EscolherArquivoParaSalvar = nome =>
            {
                var dialogo = new Microsoft.Win32.SaveFileDialog { FileName = nome, DefaultExt = ".csv", Filter = "Planilha CSV (Excel)|*.csv" };
                return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
            };
            vm.Confirmar = texto =>
                MessageBox.Show(Window.GetWindow(this), texto, "Financeiro", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            vm.PedirMotivo = PedirMotivo;
        };
    }

    /// <summary>Janelinha com uma caixa de texto para o motivo. Devolve nulo se cancelar.</summary>
    private string? PedirMotivo(string pergunta)
    {
        var motivo = new TextBox { MinHeight = 34, Margin = new Thickness(0, 10, 0, 0) };
        var erro = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "Confirmar", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancelar = new Button { Content = "Voltar", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        var janela = new Window
        {
            Title = "Cancelar conta", Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = pergunta, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold },
                    motivo, erro,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancelar } }
                }
            }
        };
        ok.Click += (_, _) =>
        {
            if (motivo.Text.Trim().Length < 3) { erro.Text = "Informe o motivo (pelo menos 3 letras)."; motivo.Focus(); return; }
            janela.DialogResult = true;
        };
        janela.Loaded += (_, _) => motivo.Focus();
        return janela.ShowDialog() == true ? motivo.Text.Trim() : null;
    }
}
