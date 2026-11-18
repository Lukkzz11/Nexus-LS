using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace NexusLS.App.Views;

public partial class ConfiguracoesView : UserControl
{
    public ConfiguracoesView()
    {
        InitializeComponent();
        MontarMenu();
        Ir("loja");

        // O PasswordBox não aceita binding (de propósito): passa o segredo ao ViewModel por aqui e limpa a caixa quando ele zera.
        CaixaSegredoGoogle.PasswordChanged += (_, _) =>
        {
            if (DataContext is ConfiguracoesViewModel vm && vm.GoogleSegredoNovo != CaixaSegredoGoogle.Password)
                vm.GoogleSegredoNovo = CaixaSegredoGoogle.Password;
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged antigo) antigo.PropertyChanged -= AoMudarPropriedade;
            if (e.NewValue is INotifyPropertyChanged novo) novo.PropertyChanged += AoMudarPropriedade;
        };
    }

    // ---- Menu lateral ----

    /// <summary>Uma entrada do menu. <paramref name="Pagina"/> é o nome do painel que já existe; sem ele, vira uma página "em breve".</summary>
    private sealed record Entrada(string Chave, string Nome, SymbolRegular Icone, string? Pagina, string Descricao = "", string[]? Itens = null);

    private static readonly (string Grupo, Entrada[] Entradas)[] Grupos =
    {
        ("LOJA", new[]
        {
            new Entrada("loja", "Loja e cupom", SymbolRegular.Building24, "PgLoja"),
            new Entrada("aparencia", "Aparência", SymbolRegular.Color24, "PgAparencia"),
            new Entrada("etiquetas", "Etiquetas", SymbolRegular.Tag24, "PgEtiquetas"),
        }),
        ("VENDAS E CAIXA", new[]
        {
            new Entrada("vendas", "Vendas", SymbolRegular.Cart24, "PgVendas"),
            new Entrada("parametros", "Parâmetros", SymbolRegular.Options24, "PgParametros"),
            new Entrada("caixa", "Caixa", SymbolRegular.Money24, "PgCaixa"),
            new Entrada("crediario", "Crediário e clientes", SymbolRegular.People24, "PgCrediario"),
            new Entrada("comissoes", "Comissões", SymbolRegular.Calculator24, "PgComissoes"),
            new Entrada("estoque", "Estoque e produtos", SymbolRegular.Box24, "PgEstoque"),
        }),
        ("PAGAMENTOS E FISCAL", new[]
        {
            new Entrada("pagamentos", "Pagamentos (Pix)", SymbolRegular.Payment24, null),
            new Entrada("fiscal", "Fiscal", SymbolRegular.DocumentText24, null, "Preparação para a nota fiscal (NFC-e).",
                new[] { "Regime tributário, inscrição estadual e série", "Certificado digital", "Emissão de NFC-e direto do PDV" }),
        }),
        ("EQUIPE E SEGURANÇA", new[]
        {
            new Entrada("seguranca", "Segurança", SymbolRegular.LockClosed24, "PgSeguranca"),
        }),
        ("DADOS E NUVEM", new[]
        {
            new Entrada("backup", "Backup e nuvem", SymbolRegular.CloudArrowUp24, "PgBackup"),
            new Entrada("integracoes", "Integrações", SymbolRegular.PlugConnected24, "PgIntegracoes"),
            new Entrada("comunicacao", "Comunicação", SymbolRegular.Chat24, null, "Mensagens prontas para o cliente.",
                new[] { "Texto padrão de recibo, cobrança e aniversário (WhatsApp e e-mail)" }),
            new Entrada("dados", "Apagar dados", SymbolRegular.ShieldCheckmark24, "PgDados"),
        }),
        ("SISTEMA", new[]
        {
            new Entrada("sistema", "Sistema e licença", SymbolRegular.Settings24, "PgSistema"),
            new Entrada("atalhos", "Atalhos do teclado", SymbolRegular.Keyboard24, "PgAtalhos"),
            new Entrada("avisos", "Notificações", SymbolRegular.Alert24, "PgNotificacoes"),
            new Entrada("lojas", "Várias lojas", SymbolRegular.BuildingMultiple24, null, "Quando houver mais de uma unidade.",
                new[] { "Escolher a loja ativa", "Relatórios consolidados" }),
        }),
    };

    private readonly Dictionary<string, System.Windows.Controls.Button> _botoesMenu = new();

    private IEnumerable<FrameworkElement> Paginas() =>
        new FrameworkElement[] { PgLoja, PgAparencia, PgVendas, PgParametros, PgBackup, PgSistema, PgAtalhos, PgDados, PgSeguranca, PgCaixa, PgCrediario, PgComissoes, PgEstoque, PgNotificacoes, PgEtiquetas, PgIntegracoes, PgBreve };

    private void MontarMenu()
    {
        foreach (var (grupo, entradas) in Grupos)
        {
            MenuConfig.Children.Add(new System.Windows.Controls.TextBlock { Text = grupo, Style = (Style)FindResource("SecaoMenu"), Margin = new Thickness(18, 12, 12, 4) });
            foreach (var e in entradas)
            {
                var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
                conteudo.Children.Add(new SymbolIcon(e.Icone) { FontSize = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                conteudo.Children.Add(new System.Windows.Controls.TextBlock { Text = e.Nome, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
                var botao = new System.Windows.Controls.Button { Content = conteudo, Tag = e.Chave, Style = (Style)FindResource("BotaoMenu"), Padding = new Thickness(14, 7, 8, 7) };
                System.Windows.Automation.AutomationProperties.SetName(botao, e.Nome);
                botao.Click += (_, _) => Ir(e.Chave);
                _botoesMenu[e.Chave] = botao;
                MenuConfig.Children.Add(botao);
            }
        }
    }

    /// <summary>Abre uma página das configurações pelo nome (por exemplo "backup").</summary>
    public void Ir(string chave)
    {
        var entrada = Grupos.SelectMany(g => g.Entradas).FirstOrDefault(x => x.Chave == chave);
        if (entrada is null) return;

        if (chave == "pagamentos")
        {
            AbrirConfigPagamento(this, new RoutedEventArgs());   // abre a janela própria e mantém a página atual
            return;
        }

        foreach (var p in Paginas()) p.Visibility = Visibility.Collapsed;
        if (entrada.Pagina is not null)
        {
            ((FrameworkElement)FindName(entrada.Pagina)).Visibility = Visibility.Visible;
        }
        else
        {
            BreveTitulo.Text = entrada.Nome;
            BreveDescricao.Text = entrada.Descricao;
            BreveItens.ItemsSource = entrada.Itens ?? Array.Empty<string>();
            PgBreve.Visibility = Visibility.Visible;
        }

        foreach (var (k, b) in _botoesMenu)
        {
            var ativo = k == chave;
            b.Background = ativo ? (System.Windows.Media.Brush)FindResource("CorMenuAtivo") : System.Windows.Media.Brushes.Transparent;
            b.Foreground = ativo ? (System.Windows.Media.Brush)FindResource("CorPrimaria") : (System.Windows.Media.Brush)FindResource("CorTexto");
            b.FontWeight = ativo ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // Máscaras enquanto digita (CNPJ/CPF, telefone e CEP), como no ERP.
    private static void Mascarar(object sender, Func<string?, string> formatar)
    {
        if (sender is not TextBox caixa) return;
        var formatado = formatar(caixa.Text);
        if (formatado == caixa.Text) return;
        caixa.Text = formatado;
        caixa.CaretIndex = formatado.Length;
    }

    private void Documento_TextChanged(object sender, TextChangedEventArgs e) => Mascarar(sender, NexusLS.Core.Services.ValidadorDocumento.FormatarDigitando);
    private void Telefone_TextChanged(object sender, TextChangedEventArgs e) => Mascarar(sender, NexusLS.Core.Services.ValidadorDocumento.FormatarTelefoneDigitando);
    private void Cep_TextChanged(object sender, TextChangedEventArgs e) => Mascarar(sender, NexusLS.Core.Services.ValidadorDocumento.FormatarCepDigitando);

    /// <summary>Abre o editor do comprovante numa janela própria (usa os mesmos dados da tela, então a prévia acompanha).</summary>
    private void EditarComprovante_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ConfiguracoesViewModel vm) return;
        new ComprovanteWindow { DataContext = vm, Owner = System.Windows.Window.GetWindow(this) }.ShowDialog();
    }

    private void AoMudarPropriedade(object? remetente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfiguracoesViewModel.GoogleSegredoNovo) && remetente is ConfiguracoesViewModel vm
            && CaixaSegredoGoogle.Password != vm.GoogleSegredoNovo)
            CaixaSegredoGoogle.Password = vm.GoogleSegredoNovo;
    }

    private void AbrirConfigPagamento(object sender, System.Windows.RoutedEventArgs e) =>
        new ConfiguracaoPagamentosWindow(App.Servicos.PagamentosConfig, App.Servicos.PixRepo, App.Servicos.Pix)
        { Owner = System.Windows.Window.GetWindow(this) }.ShowDialog();
}
