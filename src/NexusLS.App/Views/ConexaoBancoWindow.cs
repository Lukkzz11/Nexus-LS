using System.Windows;
using System.Windows.Controls;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Sincronizacao.Operacional;

namespace NexusLS.App.Views;

/// <summary>
/// "Conectar banco de dados": escolhe a conta do Firebase usada pelo programa. Duas partes: o catálogo online
/// (projeto, chave, e-mail e senha do login) e a nuvem operacional (vendas; projeto, chave e loja). Montada em código,
/// como as janelas de login. Só a chave pública da API é guardada em claro; a senha vai protegida pelo Windows.
/// </summary>
public sealed class ConexaoBancoWindow : Window
{
    private readonly ConfiguracaoFirebaseStore _firebase;
    private readonly ConfiguracaoOperacionalStore _operacional;
    private readonly TextBox _projeto = Campo(), _chave = Campo(), _email = Campo(), _bucket = Campo();
    private readonly PasswordBox _senha = new() { MinHeight = 34, Margin = new Thickness(0, 3, 0, 10) };
    private readonly TextBox _opProjeto = Campo(), _opChave = Campo(), _opLoja = Campo();
    private readonly TextBlock _aviso = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), MinHeight = 20 };

    public ConexaoBancoWindow(ConfiguracaoFirebaseStore firebase, ConfiguracaoOperacionalStore operacional)
    {
        _firebase = firebase;
        _operacional = operacional;
        Title = "Nexus LS — Conectar banco de dados";
        Width = 500;
        MaxHeight = 760;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var f = firebase.Carregar();
        _projeto.Text = f.ProjectId; _chave.Text = f.ApiKey; _email.Text = f.Email; _bucket.Text = f.Bucket;
        var o = operacional.Carregar();
        _opProjeto.Text = o.ProjectId; _opChave.Text = o.ApiKey; _opLoja.Text = o.LojaId;

        var salvar = new Button { Content = "Salvar", IsDefault = true, Padding = new Thickness(20, 8, 20, 8), MinWidth = 110 };
        if (Application.Current?.TryFindResource("BotaoPrimario") is Style primario) salvar.Style = primario;
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };

        var painel = new StackPanel
        {
            Margin = new Thickness(26, 22, 26, 22),
            Children =
            {
                Titulo("Conectar banco de dados"),
                Texto("Informe a conta do Firebase que o programa deve usar. Os dados ficam só neste computador."),
                Secao("Catálogo online (Firebase)"),
                Rotulo("ID do projeto (ex.: minha-loja-1a2b3)"), _projeto,
                Rotulo("Chave da API (Web API Key)"), _chave,
                Rotulo("E-mail do login"), _email,
                Rotulo(f.SenhaProtegida is null ? "Senha" : "Senha (deixe em branco para manter a salva)"), _senha,
                Rotulo("Bucket do Storage (opcional)"), _bucket,
                Secao("Nuvem operacional (vendas)"),
                Rotulo("ID do projeto (deixe vazio para não usar)"), _opProjeto,
                Rotulo("Chave da API"), _opChave,
                Rotulo("Código da loja"), _opLoja,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { salvar, cancelar } },
                _aviso
            }
        };
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = painel };
        salvar.Click += (_, _) => Salvar();
        cancelar.Click += (_, _) => DialogResult = false;
    }

    private void Salvar()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_projeto.Text) || !string.IsNullOrWhiteSpace(_chave.Text))
                _firebase.Salvar(new ConfiguracaoFirebase { ProjectId = _projeto.Text, ApiKey = _chave.Text, Email = _email.Text, Bucket = _bucket.Text }, _senha.Password);
            if (!string.IsNullOrWhiteSpace(_opProjeto.Text) || !string.IsNullOrWhiteSpace(_opChave.Text))
                _operacional.Salvar(new ConfiguracaoOperacional { ProjectId = _opProjeto.Text, ApiKey = _opChave.Text, LojaId = _opLoja.Text });
            DialogResult = true;
        }
        catch (RegraDeNegocioException ex)
        {
            _aviso.Foreground = System.Windows.Media.Brushes.Firebrick;
            _aviso.Text = ex.Message;
        }
    }

    private static TextBox Campo() => new() { MinHeight = 34, Margin = new Thickness(0, 3, 0, 10) };
    private static TextBlock Rotulo(string t) => new() { Text = t, FontSize = 12, Opacity = 0.75 };
    private static TextBlock Titulo(string t) => new() { Text = t, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private static TextBlock Texto(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private static TextBlock Secao(string t) => new() { Text = t, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) };
}
