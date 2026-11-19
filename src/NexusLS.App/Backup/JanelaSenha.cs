using System.Windows;
using System.Windows.Controls;

namespace NexusLS.App.Backup;

/// <summary>Janelinha que pede a senha do backup completo (duas vezes ao criar, uma ao restaurar).</summary>
public static class JanelaSenha
{
    public static string? Pedir(string titulo, string explicacao, bool confirmar)
    {
        var senha = new PasswordBox { Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6) };
        var repete = new PasswordBox { Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6) };
        var erro = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        var painel = new StackPanel { Margin = new Thickness(20) };
        painel.Children.Add(new TextBlock { Text = explicacao, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        painel.Children.Add(new TextBlock { Text = "Senha" });
        painel.Children.Add(senha);
        if (confirmar)
        {
            painel.Children.Add(new TextBlock { Text = "Repita a senha" });
            painel.Children.Add(repete);
        }
        painel.Children.Add(erro);
        var ok = new Button { Content = "OK", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, Width = 90 };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        botoes.Children.Add(ok);
        botoes.Children.Add(cancelar);
        painel.Children.Add(botoes);

        var janela = new Window
        {
            Title = titulo, Content = painel, Width = 420, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current?.MainWindow
        };
        ok.Click += (_, _) =>
        {
            if (senha.Password.Length < NexusLS.Core.Backup.PacoteCompleto.MinimoSenha)
                erro.Text = $"Use pelo menos {NexusLS.Core.Backup.PacoteCompleto.MinimoSenha} caracteres.";
            else if (confirmar && senha.Password != repete.Password)
                erro.Text = "As senhas não são iguais.";
            else janela.DialogResult = true;
        };
        senha.Focus();
        return janela.ShowDialog() == true ? senha.Password : null;
    }
}
