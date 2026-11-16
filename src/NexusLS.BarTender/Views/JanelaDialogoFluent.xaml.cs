using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace NexusLS.BarTender.Views;

public enum TipoDialogoFluent
{
    Informacao,
    Confirmacao,
    Erro,
    Sucesso
}

public partial class JanelaDialogoFluent : Window
{
    public bool Resultado { get; private set; }

    public JanelaDialogoFluent(
        string mensagem,
        string titulo,
        TipoDialogoFluent tipo,
        string textoConfirmar = "OK",
        string textoCancelar = "Cancelar")
    {
        InitializeComponent();

        TxtTitulo.Text = titulo;
        TxtMensagem.Text = mensagem;
        BtnConfirmar.Content = textoConfirmar;
        BtnCancelar.Content = textoCancelar;

        ConfigurarAparencia(tipo);

        // Define o Owner para centralizar sobre a janela ativa
        try
        {
            var principal = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow;
            if (principal != null && principal != this && principal.IsLoaded)
            {
                Owner = principal;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Resultado = false;
                Close();
            }
        };
    }

    private void ConfigurarAparencia(TipoDialogoFluent tipo)
    {
        switch (tipo)
        {
            case TipoDialogoFluent.Confirmacao:
                IconeTipo.Symbol = SymbolRegular.QuestionCircle24;
                IconeTipo.Foreground = FindResource("CorAviso") as Brush ?? Brushes.DarkOrange;
                BordaIcone.Background = FindResource("CorAvisoFundo") as Brush ?? Brushes.FloralWhite;
                BtnCancelar.Visibility = Visibility.Visible;
                break;

            case TipoDialogoFluent.Erro:
                IconeTipo.Symbol = SymbolRegular.ErrorCircle24;
                IconeTipo.Foreground = FindResource("CorPerigo") as Brush ?? Brushes.Crimson;
                BordaIcone.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xC4, 0x2B, 0x1C));
                BtnCancelar.Visibility = Visibility.Collapsed;
                break;

            case TipoDialogoFluent.Sucesso:
                IconeTipo.Symbol = SymbolRegular.CheckmarkCircle24;
                IconeTipo.Foreground = FindResource("CorPositivo") as Brush ?? Brushes.Green;
                BordaIcone.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x15, 0x80, 0x3D));
                BtnCancelar.Visibility = Visibility.Collapsed;
                break;

            case TipoDialogoFluent.Informacao:
            default:
                IconeTipo.Symbol = SymbolRegular.Info24;
                IconeTipo.Foreground = FindResource("CorPrimaria") as Brush ?? Brushes.RoyalBlue;
                BordaIcone.Background = FindResource("CorSelecaoSuave") as Brush ?? Brushes.AliceBlue;
                BtnCancelar.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
    {
        Resultado = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        Resultado = false;
        DialogResult = false;
        Close();
    }
}
