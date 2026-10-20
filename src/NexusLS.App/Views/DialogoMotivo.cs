using System.Windows;
using System.Windows.Controls;

namespace NexusLS.App.Views;

/// <summary>Janelinha com uma pergunta e uma caixa de texto para o motivo. Devolve nulo se o operador voltar.</summary>
public static class DialogoMotivo
{
    public static string? Pedir(Window? dono, string titulo, string pergunta)
    {
        var motivo = new TextBox { MinHeight = 34, Margin = new Thickness(0, 10, 0, 0) };
        var erro = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "Confirmar", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var voltar = new Button { Content = "Voltar", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        var janela = new Window
        {
            Title = titulo, Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = dono, ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = pergunta, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold },
                    motivo, erro,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, voltar } }
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
