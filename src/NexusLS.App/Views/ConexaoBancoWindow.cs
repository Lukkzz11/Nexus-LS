using System.Windows;
using System.Windows.Controls;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Sincronizacao.Operacional;

namespace NexusLS.App.Views;

/// <summary>
/// "Conectar banco de dados": escolhe a conta/projeto do Firebase. Três contas:
/// (1) o ERP PDV Life, o mesmo banco da loja (projeto e empresa já vêm do erp.json; SOMENTE LEITURA, com "Testar
/// conexão" que entra com o e-mail e a senha do ERP digitados aqui e confirma a leitura);
/// (2) o catálogo online do Nexus (projeto próprio); (3) a nuvem operacional (vendas; projeto próprio).
/// Montada em código, como as janelas de login. A chave da API do ERP nunca aparece na tela (campo oculto) e a senha
/// digitada não é guardada; a senha do catálogo vai protegida pelo Windows.
/// </summary>
public sealed class ConexaoBancoWindow : Window
{
    private readonly ConfiguracaoFirebaseStore _firebase;
    private readonly ConfiguracaoOperacionalStore _operacional;
    private readonly ConfiguracaoErpStore? _erp;
    private readonly ContaErp? _contaErp;

    private readonly ComboBox _conta = new() { MinHeight = 34, Margin = new Thickness(0, 3, 0, 14) };
    private readonly StackPanel _painelErp = new(), _painelCatalogo = new(), _painelOperacional = new();

    private readonly TextBox _projeto = Campo(), _chave = Campo(), _email = Campo(), _bucket = Campo();
    private readonly PasswordBox _senha = Senha();
    private readonly TextBox _opProjeto = Campo(), _opChave = Campo(), _opLoja = Campo();
    private readonly TextBox _erpProjeto = Campo(), _erpEmpresa = Campo(), _erpEmail = Campo();
    private readonly PasswordBox _erpChave = Senha(), _erpSenha = Senha();
    private readonly TextBlock _erpChaveEstado = new() { FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, -6, 0, 10), TextWrapping = TextWrapping.Wrap };
    private readonly Button _testar = new() { Content = "Testar conexão", Padding = new Thickness(16, 8, 16, 8), MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _erpAviso = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), MinHeight = 20 };
    private readonly Button _colar = new() { Content = "Colar configuração do Firebase", Padding = new Thickness(14, 7, 14, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
    private readonly TextBlock _aviso = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), MinHeight = 20 };

    public ConexaoBancoWindow(ConfiguracaoFirebaseStore firebase, ConfiguracaoOperacionalStore operacional,
        ConfiguracaoErpStore? erp = null, ContaErp? contaErp = null)
    {
        _firebase = firebase;
        _operacional = operacional;
        _erp = erp ?? PadraoDoPrograma(s => s.ErpConfig);
        _contaErp = contaErp ?? PadraoDoPrograma(s => s.ContaErp);
        Title = "Nexus — Conexão com a nuvem (Firebase)";
        Width = 560;
        MaxHeight = 800;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var f = firebase.Carregar();
        _projeto.Text = f.ProjectId; _chave.Text = f.ApiKey; _email.Text = f.Email; _bucket.Text = f.Bucket;
        var o = operacional.Carregar();
        _opProjeto.Text = o.ProjectId; _opChave.Text = o.ApiKey; _opLoja.Text = o.LojaId;
        var e = _erp?.Carregar() ?? new ConfiguracaoErp();
        var erpConfigurado = e.EstaCompleta;
        _erpProjeto.Text = e.ProjectId; _erpEmpresa.Text = e.EmpresaId;
        _erpEmail.Text = _contaErp?.Email ?? "";
        _erpChaveEstado.Text = string.IsNullOrWhiteSpace(e.ApiKey)
            ? "Chave da API ainda não informada."
            : "Chave da API salva neste computador (oculta). Deixe em branco para manter.";

        foreach (var item in new[]
        {
            "Importar do ERP PDV Life (somente leitura)",
            "Catálogo online (projeto próprio da loja)",
            "Nuvem das vendas (projeto próprio da loja)"
        }) _conta.Items.Add(item);
        _conta.SelectedIndex = erpConfigurado || (string.IsNullOrWhiteSpace(f.ProjectId) && string.IsNullOrWhiteSpace(o.ProjectId)) ? 0 : 1;
        _conta.SelectionChanged += (_, _) => MostrarPainel();

        var salvar = new Button { Content = "Salvar", IsDefault = true, Padding = new Thickness(20, 8, 20, 8), MinWidth = 110 };
        if (Application.Current?.TryFindResource("BotaoPrimario") is Style primario) { salvar.Style = primario; _testar.Style = primario; }
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };

        Preencher(_painelErp,
            Texto("O Nexus usa o mesmo Firebase do ERP PDV Life, só para LER (importar clientes, produtos, estoque e vendas). Nada é gravado no ERP."),
            Rotulo("ID do projeto do ERP"), _erpProjeto,
            Rotulo("Empresa do ERP"), _erpEmpresa,
            Rotulo("Chave da API (Web API Key) do ERP"), _erpChave, _erpChaveEstado,
            Secao("Testar com o seu login do ERP"),
            Texto("Digite o e-mail e a senha com que você entra no ERP. A senha é usada só para este teste e não é guardada."),
            Rotulo("E-mail do ERP"), _erpEmail,
            Rotulo("Senha do ERP"), _erpSenha,
            _testar, _erpAviso);
        Preencher(_painelCatalogo,
            Texto("Projeto Firebase PRÓPRIO do catálogo online do Nexus. Não use o projeto do ERP aqui."),
            Rotulo("ID do projeto (ex.: minha-loja-1a2b3)"), _projeto,
            Rotulo("Chave da API (Web API Key)"), _chave,
            Rotulo("E-mail do login"), _email,
            Rotulo(f.SenhaProtegida is null ? "Senha" : "Senha (deixe em branco para manter a salva)"), _senha,
            Rotulo("Bucket do Storage (opcional)"), _bucket);
        Preencher(_painelOperacional,
            Texto("Projeto Firebase PRÓPRIO da nuvem operacional (vendas). Deixe vazio para não usar."),
            Rotulo("ID do projeto"), _opProjeto,
            Rotulo("Chave da API"), _opChave,
            Rotulo("Código da loja"), _opLoja);

        var painel = new StackPanel
        {
            Margin = new Thickness(26, 22, 26, 22),
            Children =
            {
                Titulo("Conexão com a nuvem"),
                Texto("Cada loja usa o seu próprio projeto Firebase. Escolha a conexão, cole a configuração do projeto (a que o console do Firebase mostra) ou digite os campos. Os dados ficam só neste computador."),
                Rotulo("Qual conexão configurar"), _conta,
                _colar,
                _painelErp, _painelCatalogo, _painelOperacional,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { salvar, cancelar } },
                _aviso
            }
        };
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = painel };
        MostrarPainel();
        salvar.Click += (_, _) => Salvar();
        _colar.Click += (_, _) => ColarConfiguracao();
        _testar.Click += async (_, _) => await TestarAsync();
        cancelar.Click += (_, _) => DialogResult = false;
    }

    private static T? PadraoDoPrograma<T>(Func<ContainerDeServicos, T> escolher) where T : class
    {
        try { return escolher(App.Servicos); }
        catch (Exception ex) when (ex is InvalidOperationException or TypeInitializationException or System.IO.IOException) { return null; }
    }

    private void MostrarPainel()
    {
        _painelErp.Visibility = _conta.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        _painelCatalogo.Visibility = _conta.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _painelOperacional.Visibility = _conta.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Grava o projeto e a empresa do ERP. A chave só muda se uma nova for digitada; o resto do erp.json é mantido.</summary>
    private bool SalvarErp()
    {
        if (_erp is null) return false;
        if (string.IsNullOrWhiteSpace(_erpProjeto.Text) && string.IsNullOrWhiteSpace(_erpChave.Password)) return false;
        var atual = _erp.Carregar();
        atual.ProjectId = _erpProjeto.Text;
        atual.EmpresaId = string.IsNullOrWhiteSpace(_erpEmpresa.Text) ? atual.EmpresaId : _erpEmpresa.Text;
        if (!string.IsNullOrWhiteSpace(_erpChave.Password)) atual.ApiKey = _erpChave.Password;
        _erp.Salvar(atual);
        _erpChave.Clear();
        _erpChaveEstado.Text = "Chave da API salva neste computador (oculta). Deixe em branco para manter.";
        return true;
    }

    private async Task TestarAsync()
    {
        if (_contaErp is null) { Mensagem(_erpAviso, "O teste não está disponível aqui.", erro: true); return; }
        _testar.IsEnabled = false;
        Mensagem(_erpAviso, "Testando (entra no ERP e lê um produto)...", erro: false);
        try
        {
            SalvarErp();
            var r = await _contaErp.TestarConexaoAsync(_erpEmail.Text, _erpSenha.Password);
            _erpSenha.Clear();
            Mensagem(_erpAviso, r.Mensagem, erro: false);
        }
        catch (Exception ex) when (ex is RegraDeNegocioException or FalhaLoginException or FalhaDestinoException)
        {
            Mensagem(_erpAviso, ex.Message, erro: true);
        }
        finally { _testar.IsEnabled = true; }
    }

    /// <summary>Lê o firebaseConfig da área de transferência e preenche os campos da conexão escolhida (nada é gravado ainda).</summary>
    private void ColarConfiguracao()
    {
        string? texto = null;
        try { texto = Clipboard.GetText(); } catch (System.Runtime.InteropServices.ExternalException) { }
        var c = ConfiguracaoColada.Ler(texto);
        if (!c.TemAlgo)
        {
            Mensagem(_aviso, "Não achei a configuração do Firebase na área de transferência. Copie o trecho \"firebaseConfig\" do console e tente de novo.", erro: true);
            return;
        }

        var (projeto, chave) = _conta.SelectedIndex switch { 0 => (_erpProjeto, null), 1 => (_projeto, _chave), _ => (_opProjeto, _opChave) };
        if (!string.IsNullOrWhiteSpace(c.ProjectId)) projeto.Text = c.ProjectId;
        if (chave is not null && !string.IsNullOrWhiteSpace(c.ApiKey)) chave.Text = c.ApiKey;
        else if (_conta.SelectedIndex == 0 && !string.IsNullOrWhiteSpace(c.ApiKey)) _erpChave.Password = c.ApiKey;
        if (_conta.SelectedIndex == 1 && !string.IsNullOrWhiteSpace(c.Bucket)) _bucket.Text = c.Bucket;
        Mensagem(_aviso, $"Configuração colada (projeto {c.ProjectId}). Confira os campos e clique em Salvar.", erro: false);
    }

    private void Salvar()
    {
        try
        {
            SalvarErp();
            if (!string.IsNullOrWhiteSpace(_projeto.Text) || !string.IsNullOrWhiteSpace(_chave.Text))
                _firebase.Salvar(new ConfiguracaoFirebase { ProjectId = _projeto.Text, ApiKey = _chave.Text, Email = _email.Text, Bucket = _bucket.Text }, _senha.Password);
            if (!string.IsNullOrWhiteSpace(_opProjeto.Text) || !string.IsNullOrWhiteSpace(_opChave.Text))
                _operacional.Salvar(new ConfiguracaoOperacional { ProjectId = _opProjeto.Text, ApiKey = _opChave.Text, LojaId = _opLoja.Text });
            DialogResult = true;
        }
        catch (RegraDeNegocioException ex)
        {
            Mensagem(_aviso, ex.Message, erro: true);
        }
    }

    private static void Mensagem(TextBlock alvo, string texto, bool erro)
    {
        alvo.Foreground = erro ? System.Windows.Media.Brushes.Firebrick : System.Windows.Media.Brushes.SeaGreen;
        alvo.Text = texto;
    }

    private static void Preencher(StackPanel painel, params UIElement[] itens)
    {
        foreach (var i in itens) painel.Children.Add(i);
    }

    private static TextBox Campo() => new() { MinHeight = 34, Margin = new Thickness(0, 3, 0, 10) };
    private static PasswordBox Senha() => new() { MinHeight = 34, Margin = new Thickness(0, 3, 0, 10) };
    private static TextBlock Rotulo(string t) => new() { Text = t, FontSize = 12, Opacity = 0.75 };
    private static TextBlock Titulo(string t) => new() { Text = t, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private static TextBlock Texto(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private static TextBlock Secao(string t) => new() { Text = t, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) };
}
