using System.Windows;
using System.Windows.Media;

namespace NexusLS.Atualizador;

public enum TipoDialogo
{
    Informacao,
    Sucesso,
    Aviso,
    Erro,
    Confirmacao
}

public partial class JanelaDialogo : Window
{
    public bool Confirmado { get; private set; }

    public JanelaDialogo(string titulo, string mensagem, TipoDialogo tipo = TipoDialogo.Informacao, Window? proprietario = null)
    {
        InitializeComponent();

        if (proprietario != null && proprietario.IsVisible)
        {
            Owner = proprietario;
        }

        TxtTitulo.Text = titulo;
        TxtMensagem.Text = mensagem;
        TxtTituloBarra.Text = $"Nexus — {titulo}";

        ConfigurarEstiloTipo(tipo);
    }

    private void ConfigurarEstiloTipo(TipoDialogo tipo)
    {
        switch (tipo)
        {
            case TipoDialogo.Sucesso:
                BorderIcone.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4));
                PathIcone.Fill = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                PathIcone.Data = Geometry.Parse("M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z");
                BtnCancelar.Visibility = Visibility.Collapsed;
                BtnConfirmar.Content = "Concluir";
                BtnConfirmar.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                BtnConfirmar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                break;

            case TipoDialogo.Aviso:
                BorderIcone.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB));
                PathIcone.Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                PathIcone.Data = Geometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
                BtnCancelar.Visibility = Visibility.Collapsed;
                BtnConfirmar.Content = "Entendido";
                BtnConfirmar.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                BtnConfirmar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                break;

            case TipoDialogo.Erro:
                BorderIcone.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
                PathIcone.Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                PathIcone.Data = Geometry.Parse("M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z");
                BtnCancelar.Visibility = Visibility.Collapsed;
                BtnConfirmar.Content = "Fechar";
                BtnConfirmar.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                BtnConfirmar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                break;

            case TipoDialogo.Confirmacao:
                BorderIcone.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
                PathIcone.Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                PathIcone.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");
                BtnCancelar.Visibility = Visibility.Visible;
                BtnConfirmar.Content = "Sim, Continuar";
                BtnConfirmar.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                BtnConfirmar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                break;

            default:
                BorderIcone.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF9, 0xFF));
                PathIcone.Fill = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
                PathIcone.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");
                BtnCancelar.Visibility = Visibility.Collapsed;
                BtnConfirmar.Content = "OK";
                BtnConfirmar.Background = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
                BtnConfirmar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
                break;
        }
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        Confirmado = false;
        Close();
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        Confirmado = false;
        Close();
    }

    private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
    {
        Confirmado = true;
        Close();
    }

    public static void Mostrar(string titulo, string mensagem, TipoDialogo tipo = TipoDialogo.Informacao, Window? proprietario = null)
    {
        var diag = new JanelaDialogo(titulo, mensagem, tipo, proprietario);
        diag.ShowDialog();
    }

    public static bool Confirmar(string titulo, string mensagem, Window? proprietario = null)
    {
        var diag = new JanelaDialogo(titulo, mensagem, TipoDialogo.Confirmacao, proprietario);
        diag.ShowDialog();
        return diag.Confirmado;
    }
}