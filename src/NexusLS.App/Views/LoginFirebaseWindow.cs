using System.Windows;
using System.Windows.Controls;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.App.Views;

/// <summary>
/// Entrada do usuário no Firebase (e-mail e senha). "Continuar sem entrar" mantém o programa funcionando: as
/// alterações ficam na fila e são enviadas quando alguém entrar. Montada em código, como a janela de PIN.
/// </summary>
public sealed class LoginFirebaseWindow : Window
{
    private readonly GestorLogin _login;
    private readonly TextBox _email = new() { MinHeight = 36, Margin = new Thickness(0, 4, 0, 12) };
    private readonly PasswordBox _senha = new() { MinHeight = 36, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _erro = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MinHeight = 20 };
    private readonly Button _entrar = new() { Content = "Entrar", IsDefault = true, Padding = new Thickness(20, 8, 20, 8), MinWidth = 110 };

    public SessaoUsuario? Entrou { get; private set; }

    public LoginFirebaseWindow(GestorLogin login)
    {
        _login = login;
        Title = "Nexus LS — Entrar na conta";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("BotaoPrimario") is Style primario) _entrar.Style = primario;

        var depois = new Button { Content = "Continuar sem entrar", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };
        _email.Text = login.EmailSugerido;
        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Children =
            {
                new TextBlock { Text = "Conta do Nexus LS", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) },
                new TextBlock { Text = "Entre para enviar o catálogo à nuvem. As vendas e o estoque continuam funcionando sem internet.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) },
                new TextBlock { Text = "E-mail" }, _email,
                new TextBlock { Text = "Senha" }, _senha,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { _entrar, depois } },
                _erro
            }
        };
        Loaded += (_, _) => (string.IsNullOrWhiteSpace(_email.Text) ? (Control)_email : _senha).Focus();
        _entrar.Click += async (_, _) => await EntrarAsync();
        depois.Click += (_, _) => DialogResult = false;
    }

    private async Task EntrarAsync()
    {
        _entrar.IsEnabled = false;
        _erro.Text = "Entrando...";
        try
        {
            Entrou = await _login.EntrarAsync(_email.Text, _senha.Password);
            DialogResult = true;
        }
        catch (FalhaLoginException ex)
        {
            _erro.Text = ex.Message;
            _senha.Clear();
            _senha.Focus();
        }
        finally
        {
            _entrar.IsEnabled = true;
        }
    }

    /// <summary>Mostra a janela. Devolve a sessão, ou nulo se a pessoa preferiu continuar sem entrar.</summary>
    public static SessaoUsuario? Pedir(GestorLogin login, Window? dono)
    {
        var janela = new LoginFirebaseWindow(login) { Owner = dono };
        return janela.ShowDialog() == true ? janela.Entrou : null;
    }
}
