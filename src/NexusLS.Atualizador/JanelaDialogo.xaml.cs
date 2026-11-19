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
                BorderIcone.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
                PathIcone.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xB8));
                PathIcone.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 16h-2v-2h2v2zm1.07-7.75l-.9.92C12.45 11.9 12 12.5 12 14h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H7c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.04-.42 1.99-1.07 2.75z");
                BtnCancelar.Visibility = Visibility.Visible;
                BtnConfirmar.Content = "Continuar";
                BtnConfirmar.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xB8));
                BtnConfirmar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xB8));
                break;

            default:
                BorderIcone.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
                PathIcone.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xB8));
                PathIcone.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");
                BtnCancelar.Visibility = Visibility.Collapsed;
                BtnConfirmar.Content = "OK";
                BtnConfirmar.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xB8));
                BtnConfirmar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xB8));
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

    public static bool Confirmar(string titulo, string mensagem, Window? proprietario = null, string textoConfirmar = "Continuar", string textoCancelar = "Cancelar")
    {
        var diag = new JanelaDialogo(titulo, mensagem, TipoDialogo.Confirmacao, proprietario);
        if (!string.IsNullOrWhiteSpace(textoConfirmar)) diag.BtnConfirmar.Content = textoConfirmar;
        if (!string.IsNullOrWhiteSpace(textoCancelar)) diag.BtnCancelar.Content = textoCancelar;
        diag.ShowDialog();
        return diag.Confirmado;
    }
}