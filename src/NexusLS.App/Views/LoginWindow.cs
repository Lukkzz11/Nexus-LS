using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>
/// Janela de entrada: escolhe o funcionário e digita o PIN. "Esqueci o PIN" usa o código de recuperação do administrador.
/// Montada em código (é pequena e não tem lógica de tela além de chamar o FuncionarioService).
/// </summary>
public sealed class LoginWindow : Window
{
    private readonly FuncionarioService _servico;
    private readonly ComboBox _funcionarios = new() { DisplayMemberPath = nameof(Funcionario.Nome), MinHeight = 36, Margin = new Thickness(0, 4, 0, 12) };
    private readonly PasswordBox _pin = new() { MinHeight = 36, MaxLength = 8, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _erro = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MinHeight = 20 };
    private readonly Button _entrar = new() { Content = "Entrar", IsDefault = true, Padding = new Thickness(20, 8, 20, 8), MinWidth = 110 };
    private readonly StackPanel _painelEntrada = new();
    private readonly StackPanel _painelRecuperacao = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox _codigo = new() { MinHeight = 36, Margin = new Thickness(0, 4, 0, 12), CharacterCasing = CharacterCasing.Upper };
    private readonly PasswordBox _novoPin = new() { MinHeight = 36, MaxLength = 8, Margin = new Thickness(0, 4, 0, 0) };
    private readonly bool _podeCancelar;

    /// <summary>O funcionário que entrou (nulo se a janela foi fechada sem entrar).</summary>
    public Funcionario? Entrou { get; private set; }

    public LoginWindow(FuncionarioService servico, bool podeCancelar)
    {
        _servico = servico;
        _podeCancelar = podeCancelar;
        Title = "Nexus LS — Entrar";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = false;

        if (Application.Current?.TryFindResource("BotaoPrimario") is Style primario) _entrar.Style = primario;

        var cancelar = new Button { Content = podeCancelar ? "Cancelar" : "Sair do programa", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };
        var esqueci = new Button { Content = "Esqueci o PIN", Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };

        _painelEntrada.Children.Add(new TextBlock { Text = "Quem está entrando?" });
        _painelEntrada.Children.Add(_funcionarios);
        _painelEntrada.Children.Add(new TextBlock { Text = "PIN" });
        _painelEntrada.Children.Add(_pin);
        _painelEntrada.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { _entrar, cancelar } });
        _painelEntrada.Children.Add(esqueci);

        var recuperar = new Button { Content = "Redefinir PIN", Padding = new Thickness(16, 8, 16, 8) };
        var voltar = new Button { Content = "Voltar", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };
        _painelRecuperacao.Children.Add(new TextBlock { Text = "Código de recuperação do administrador", TextWrapping = TextWrapping.Wrap });
        _painelRecuperacao.Children.Add(_codigo);
        _painelRecuperacao.Children.Add(new TextBlock { Text = "Novo PIN (4 a 8 números)" });
        _painelRecuperacao.Children.Add(_novoPin);
        _painelRecuperacao.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { recuperar, voltar } });

        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Children =
            {
                new TextBlock { Text = "Nexus LS", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) },
                _painelEntrada, _painelRecuperacao, _erro
            }
        };

        Loaded += async (_, _) =>
        {
            var lista = await _servico.ListarAsync();
            _funcionarios.ItemsSource = lista;
            _funcionarios.SelectedIndex = lista.Count > 0 ? 0 : -1;
            _pin.Focus();
        };
        _entrar.Click += async (_, _) => await EntrarAsync();
        esqueci.Click += (_, _) => Alternar(recuperacao: true);
        voltar.Click += (_, _) => Alternar(recuperacao: false);
        recuperar.Click += async (_, _) => await RecuperarAsync();
        cancelar.Click += (_, _) => { DialogResult = false; };
    }

    private void Alternar(bool recuperacao)
    {
        _erro.Text = "";
        _painelEntrada.Visibility = recuperacao ? Visibility.Collapsed : Visibility.Visible;
        _painelRecuperacao.Visibility = recuperacao ? Visibility.Visible : Visibility.Collapsed;
        (recuperacao ? (Control)_codigo : _pin).Focus();
    }

    private async Task EntrarAsync()
    {
        if (_funcionarios.SelectedItem is not Funcionario f) { _erro.Text = "Escolha quem está entrando."; return; }
        _entrar.IsEnabled = false;
        try
        {
            Entrou = await _servico.AutenticarAsync(f.Id, _pin.Password);
            DialogResult = true;
        }
        catch (RegraDeNegocioException ex)
        {
            _erro.Text = ex.Message;
            _pin.Clear();
            _pin.Focus();
        }
        finally
        {
            _entrar.IsEnabled = true;
        }
    }

    private async Task RecuperarAsync()
    {
        try
        {
            var (funcionario, novoCodigo) = await _servico.RedefinirPinComCodigoAsync(_codigo.Text, _novoPin.Password);
            MessageBox.Show(this,
                $"PIN de {funcionario.Nome} redefinido.{Environment.NewLine}{Environment.NewLine}" +
                $"Seu NOVO código de recuperação é:{Environment.NewLine}{novoCodigo}{Environment.NewLine}{Environment.NewLine}" +
                "Anote em lugar seguro. O código antigo não vale mais.",
                "PIN redefinido", MessageBoxButton.OK, MessageBoxImage.Information);
            _codigo.Clear();
            _novoPin.Clear();
            Alternar(recuperacao: false);
        }
        catch (RegraDeNegocioException ex)
        {
            _erro.Text = ex.Message;
        }
    }

    /// <summary>Mostra a janela de entrada. Devolve o funcionário que entrou, ou nulo se desistiu.</summary>
    public static Funcionario? Pedir(FuncionarioService servico, Window? dono)
    {
        var janela = new LoginWindow(servico, podeCancelar: dono is not null) { Owner = dono };
        if (dono is not null) janela.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        return janela.ShowDialog() == true ? janela.Entrou : null;
    }
}
