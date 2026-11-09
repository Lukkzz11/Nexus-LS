using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NexusLS.Core.Licenciamento;
using NexusLS.Core.Services;

namespace NexusLS.Setup;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var op = OpcoesSetup.Ler(args);
        if (op.Desinstalar)
        {
            var script = Path.Combine(op.Pasta, "Desinstalar-Nexus.ps1");
            if (File.Exists(script))
            {
                Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = true });
            }
            return 0;
        }

        if (op.Silencioso)
        {
            try
            {
                new Instalador(op).InstalarAsync(new Progress<(double, string)>()).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "nexus-setup-erro.txt"), e.ToString());
                return 1;
            }
        }

        var app = new Application();
        return app.Run(new JanelaSetup(op));
    }
}

public sealed class JanelaSetup : Window
{
    private readonly OpcoesSetup _op;
    private readonly Instalador _instalador;
    private readonly string _hwid;
    private int _passoAtual = 1; // 1 = Licença, 2 = Termos, 3 = Local, 4 = Seleção de Apps, 5 = Instalação, 6 = Concluído

    // Controles do Cabeçalho e Stepper
    private readonly Border[] _etapaBadges = new Border[5];
    private readonly TextBlock[] _etapaTextos = new TextBlock[5];

    // Contêineres de cada Passo
    private readonly Grid _containerPasso1 = new();
    private readonly Grid _containerPasso2 = new();
    private readonly Grid _containerPasso3 = new();
    private readonly Grid _containerPasso4 = new();
    private readonly Grid _containerPasso5 = new();
    private readonly Grid _containerPasso6 = new();

    // Passo 1: Licença
    private readonly TextBox _txtChave;
    private readonly Button _btnColarChave;
    private readonly Button _btnValidarChave;
    private readonly Border _cardStatusLicenca;
    private readonly TextBlock _txtStatusLicenca;
    private readonly TextBlock _txtDetalhesLicenca;
    private ResultadoLicencaNuvem? _licencaValidada;

    // Passo 2: Termos
    private readonly CheckBox _chkAceitoTermos;

    // Passo 3: Local de Instalação
    private readonly TextBox _txtCaminhoPasta;
    private readonly Button _btnProcurarPasta;
    private readonly TextBlock _txtInfoDisco;

    // Passo 4: Seleção dos Aplicativos
    private readonly CheckBox _chkNexus;
    private readonly CheckBox _chkBarTender;
    private readonly CheckBox _chkAtualizador;
    private readonly CheckBox _chkAtalhos;

    // Passo 5: Progresso
    private readonly ProgressBar _barraProgresso;
    private readonly TextBlock _txtPercentualProgresso;
    private readonly TextBlock _txtMensagemProgresso;

    // Passo 6: Conclusão
    private readonly TextBlock _txtConclusaoInfo;
    private readonly Button _btnConcluirAbrirAtualizador;
    private readonly Button _btnConcluirAbrirNexus;
    private readonly Button _btnConcluirAbrirBarTender;

    // Botões Inferiores de Navegação
    private readonly Button _btnCancelar;
    private readonly Button _btnVoltar;
    private readonly Button _btnAvancar;

    public JanelaSetup(OpcoesSetup op)
    {
        _op = op;
        _instalador = new Instalador(_op);
        _hwid = LicencaService.CodigoDaMaquina();

        Title = "Nexus LS — Assistente de Instalação e Licenciamento";
        Width = 880;
        Height = 650;
        MinWidth = 800;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)); // Slate 900
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI, Inter, Arial");

        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(85) });  // 0: Cabeçalho com Stepper
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Conteúdo do Passo
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });  // 2: Rodapé de Navegação
        Content = raiz;

        // ==================== 0. CABEÇALHO COM STEPPER ====================
        var cabecalho = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)), // Slate 800
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 12, 24, 12)
        };
        var gridCabecalho = new Grid();
        gridCabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gridCabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Título à esquerda
        var spTitulo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var spTituloLinha = new StackPanel { Orientation = Orientation.Horizontal };
        spTituloLinha.Children.Add(new TextBlock { Text = "NEXUS", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        spTituloLinha.Children.Add(new TextBlock { Text = " SETUP", FontSize = 18, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), Margin = new Thickness(4, 0, 0, 0) });
        spTitulo.Children.Add(spTituloLinha);
        spTitulo.Children.Add(new TextBlock { Text = "Assistente de Instalação e Ativação", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)) });
        Grid.SetColumn(spTitulo, 0);
        gridCabecalho.Children.Add(spTitulo);

        // Stepper Central
        var spStepper = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var titulosEtapas = new[] { "1. Licença", "2. Termos", "3. Pasta", "4. Aplicativos", "5. Instalação" };
        for (int i = 0; i < 5; i++)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(4, 0, 4, 0)
            };
            var txtEtapa = new TextBlock
            {
                Text = titulosEtapas[i],
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
            };
            badge.Child = txtEtapa;
            _etapaBadges[i] = badge;
            _etapaTextos[i] = txtEtapa;
            spStepper.Children.Add(badge);
        }
        Grid.SetColumn(spStepper, 1);
        gridCabecalho.Children.Add(spStepper);

        // HWID à direita
        var cardHwid = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        var spHwid = new StackPanel { Orientation = Orientation.Horizontal };
        spHwid.Children.Add(new TextBlock
        {
            Text = $"HWID: {_hwid}",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        var btnCopiarHwid = new Button
        {
            Content = "Copiar",
            FontSize = 9,
            Padding = new Thickness(4, 1, 4, 1),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        btnCopiarHwid.Click += (_, _) =>
        {
            try { Clipboard.SetText(_hwid); btnCopiarHwid.Content = "✓"; } catch { }
        };
        spHwid.Children.Add(btnCopiarHwid);
        cardHwid.Child = spHwid;
        Grid.SetColumn(cardHwid, 2);
        gridCabecalho.Children.Add(cardHwid);

        cabecalho.Child = gridCabecalho;
        Grid.SetRow(cabecalho, 0);
        raiz.Children.Add(cabecalho);

        // ==================== 1. CONTEÚDO CENTRAL (PASSOS 1 A 6) ====================
        var painelCentral = new Grid { Margin = new Thickness(36, 20, 36, 12) };

        // --- PASSO 1: ATIVAÇÃO E VALIDAÇÃO DA LICENÇA ---
        var spPasso1 = new StackPanel();
        spPasso1.Children.Add(CriarCabecalhoEtapa(
            "Etapa 1: Ativação da Licença Comercial",
            "Informe a chave de ativação fornecida para este estabelecimento. O instalador validará na nuvem a autorização e as vagas disponíveis de computadores."));

        var gridChave = new Grid { Margin = new Thickness(0, 16, 0, 12) };
        gridChave.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridChave.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gridChave.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtChave = new TextBox
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            BorderThickness = new Thickness(1),
            CharacterCasing = CharacterCasing.Upper,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _txtChave.TextChanged += (_, _) =>
        {
            _licencaValidada = null;
            AtualizarEstadoBotoes();
        };

        // Carrega chave padrão ou cache se existir
        if (!string.IsNullOrWhiteSpace(_op.ChaveLicenca))
        {
            _txtChave.Text = _op.ChaveLicenca;
        }
        else
        {
            var salva = _instalador.ServicoLicenca.AvaliarLocal();
            if (!string.IsNullOrWhiteSpace(salva.Chave)) _txtChave.Text = salva.Chave;
        }

        Grid.SetColumn(_txtChave, 0);
        gridChave.Children.Add(_txtChave);

        _btnColarChave = new Button
        {
            Content = "📋 Colar",
            FontSize = 13,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(10, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _btnColarChave.Click += async (_, _) =>
        {
            try
            {
                var texto = Clipboard.GetText()?.Trim();
                if (!string.IsNullOrWhiteSpace(texto))
                {
                    _txtChave.Text = texto.ToUpperInvariant();
                    await ValidarChaveAsync();
                }
            }
            catch { }
        };
        Grid.SetColumn(_btnColarChave, 1);
        gridChave.Children.Add(_btnColarChave);

        _btnValidarChave = new Button
        {
            Content = "🔍 Validar Chave",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(20, 10, 20, 10),
            Margin = new Thickness(10, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _btnValidarChave.Click += async (_, _) => await ValidarChaveAsync();
        Grid.SetColumn(_btnValidarChave, 2);
        gridChave.Children.Add(_btnValidarChave);
        spPasso1.Children.Add(gridChave);

        // Card informativo do status da licença
        _cardStatusLicenca = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var spStatusLic = new StackPanel();
        _txtStatusLicenca = new TextBlock
        {
            Text = "Insira a chave da licença comercial acima e clique em 'Validar Chave' para verificar a permissão e liberar o prosseguimento.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            TextWrapping = TextWrapping.Wrap
        };
        _txtDetalhesLicenca = new TextBlock
        {
            Text = "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        spStatusLic.Children.Add(_txtStatusLicenca);
        spStatusLic.Children.Add(_txtDetalhesLicenca);
        _cardStatusLicenca.Child = spStatusLic;
        spPasso1.Children.Add(_cardStatusLicenca);

        _containerPasso1.Children.Add(spPasso1);
        painelCentral.Children.Add(_containerPasso1);

        // --- PASSO 2: TERMOS DE USO ---
        var spPasso2 = new StackPanel();
        spPasso2.Children.Add(CriarCabecalhoEtapa(
            "Etapa 2: Termos de Uso e Licenciamento",
            "Por favor, leia atentamente os termos de licença de uso do software Nexus LS antes de continuar."));

        var bordaTermos = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 14, 0, 14),
            Height = 220
        };
        var scrollTermos = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var txtConteudoTermos = new TextBlock
        {
            Text = ObterTextoTermosDeUso(),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };
        scrollTermos.Content = txtConteudoTermos;
        bordaTermos.Child = scrollTermos;
        spPasso2.Children.Add(bordaTermos);

        _chkAceitoTermos = new CheckBox
        {
            Content = "Li e concordo com os Termos de Uso e com a Política de Licenciamento do Nexus LS",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(4, 4, 0, 0)
        };
        _chkAceitoTermos.Checked += (_, _) => AtualizarEstadoBotoes();
        _chkAceitoTermos.Unchecked += (_, _) => AtualizarEstadoBotoes();
        spPasso2.Children.Add(_chkAceitoTermos);

        _containerPasso2.Children.Add(spPasso2);
        _containerPasso2.Visibility = Visibility.Collapsed;
        painelCentral.Children.Add(_containerPasso2);

        // --- PASSO 3: LOCAL DE INSTALAÇÃO ---
        var spPasso3 = new StackPanel();
        spPasso3.Children.Add(CriarCabecalhoEtapa(
            "Etapa 3: Pasta de Instalação",
            "O instalador copiará os arquivos do Nexus LS e de seus módulos na pasta selecionada abaixo."));

        var gridPasta = new Grid { Margin = new Thickness(0, 16, 0, 12) };
        gridPasta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridPasta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtCaminhoPasta = new TextBox
        {
            Text = _op.Pasta,
            FontSize = 13,
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _txtCaminhoPasta.TextChanged += (_, _) => _op.Pasta = _txtCaminhoPasta.Text.Trim();
        Grid.SetColumn(_txtCaminhoPasta, 0);
        gridPasta.Children.Add(_txtCaminhoPasta);

        _btnProcurarPasta = new Button
        {
            Content = "📁 Procurar Pasta...",
            FontSize = 13,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(10, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _btnProcurarPasta.Click += (_, _) =>
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Selecione a pasta de instalação do Nexus",
                    InitialDirectory = Directory.Exists(_txtCaminhoPasta.Text) ? _txtCaminhoPasta.Text : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                };
                if (dialog.ShowDialog(this) == true)
                {
                    _txtCaminhoPasta.Text = dialog.FolderName;
                }
            }
            catch { }
        };
        Grid.SetColumn(_btnProcurarPasta, 1);
        gridPasta.Children.Add(_btnProcurarPasta);
        spPasso3.Children.Add(gridPasta);

        _txtInfoDisco = new TextBlock
        {
            Text = "Espaço necessário em disco: ~450 MB • Tipo de instalação: Local autônoma (não requer privilégios de administrador).",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            Margin = new Thickness(4, 6, 0, 0)
        };
        spPasso3.Children.Add(_txtInfoDisco);

        _containerPasso3.Children.Add(spPasso3);
        _containerPasso3.Visibility = Visibility.Collapsed;
        painelCentral.Children.Add(_containerPasso3);

        // --- PASSO 4: SELEÇÃO DOS APLICATIVOS ---
        var spPasso4 = new StackPanel();
        spPasso4.Children.Add(CriarCabecalhoEtapa(
            "Etapa 4: Seleção dos Aplicativos a Instalar",
            "Escolha os módulos que este computador precisa executar. Se o cliente só for usar o Nexus ou só o BarTender, basta desmarcar o módulo indesejado."));

        _chkNexus = CriarCardOpcaoApp(
            "Nexus LS — Sistema ERP & Frente de Caixa PDV",
            "Frente de caixa, controle de estoque, fechamento de vendas, financeiro e retaguarda.",
            _op.InstalarNexusLS);
        _chkNexus.Checked += (_, _) => AtualizarEstadoBotoes();
        _chkNexus.Unchecked += (_, _) => AtualizarEstadoBotoes();
        spPasso4.Children.Add(_chkNexus);

        _chkBarTender = CriarCardOpcaoApp(
            "Nexus BarTender — Impressor de Etiquetas",
            "Impressão autônoma de etiquetas térmicas, código de barras, gôndolas e logotipo da loja.",
            _op.InstalarBarTender);
        _chkBarTender.Checked += (_, _) => AtualizarEstadoBotoes();
        _chkBarTender.Unchecked += (_, _) => AtualizarEstadoBotoes();
        spPasso4.Children.Add(_chkBarTender);

        _chkAtualizador = CriarCardOpcaoApp(
            "Nexus Atualizador & Central do Cliente",
            "Launcher principal com atalhos para os apps, sincronização de licença e updates oficiais.",
            _op.InstalarAtualizador);
        _chkAtualizador.Checked += (_, _) => AtualizarEstadoBotoes();
        _chkAtualizador.Unchecked += (_, _) => AtualizarEstadoBotoes();
        spPasso4.Children.Add(_chkAtualizador);

        _chkAtalhos = new CheckBox
        {
            Content = "Criar atalhos na Área de Trabalho e no Menu Iniciar para os programas selecionados",
            IsChecked = _op.CriarAtalhosDesktop,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)),
            Margin = new Thickness(4, 10, 0, 0)
        };
        spPasso4.Children.Add(_chkAtalhos);

        _containerPasso4.Children.Add(spPasso4);
        _containerPasso4.Visibility = Visibility.Collapsed;
        painelCentral.Children.Add(_containerPasso4);

        // --- PASSO 5: INSTALAÇÃO & PROGRESSO ---
        var spPasso5 = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        spPasso5.Children.Add(new TextBlock
        {
            Text = "Instalando o Nexus no Computador...",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _txtMensagemProgresso = new TextBlock
        {
            Text = "Preparando arquivos...",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            Margin = new Thickness(0, 0, 0, 16)
        };
        spPasso5.Children.Add(_txtMensagemProgresso);

        _barraProgresso = new ProgressBar
        {
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 8)
        };
        spPasso5.Children.Add(_barraProgresso);

        _txtPercentualProgresso = new TextBlock
        {
            Text = "0%",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        spPasso5.Children.Add(_txtPercentualProgresso);

        _containerPasso5.Children.Add(spPasso5);
        _containerPasso5.Visibility = Visibility.Collapsed;
        painelCentral.Children.Add(_containerPasso5);

        // --- PASSO 6: CONCLUSÃO ---
        var spPasso6 = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        spPasso6.Children.Add(new TextBlock
        {
            Text = "🎉 Instalação Concluída com Sucesso!",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        _txtConclusaoInfo = new TextBlock
        {
            Text = "O Nexus foi instalado e o Nexus Atualizador está sendo aberto automaticamente como sua central do cliente.",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24)
        };
        spPasso6.Children.Add(_txtConclusaoInfo);

        var wpBotoesConclusao = new WrapPanel();
        _btnConcluirAbrirAtualizador = new Button
        {
            Content = "🚀 Abrir Nexus Atualizador (Central)",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(0, 0, 12, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _btnConcluirAbrirAtualizador.Click += (_, _) =>
        {
            AbrirExecutavel("atualizador.exe");
            Close();
        };
        wpBotoesConclusao.Children.Add(_btnConcluirAbrirAtualizador);

        _btnConcluirAbrirNexus = new Button
        {
            Content = "▶ Iniciar Nexus LS",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 12, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _btnConcluirAbrirNexus.Click += (_, _) =>
        {
            AbrirExecutavel("NexusLS.exe");
            Close();
        };
        wpBotoesConclusao.Children.Add(_btnConcluirAbrirNexus);

        _btnConcluirAbrirBarTender = new Button
        {
            Content = "🏷 Iniciar BarTender",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 12, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _btnConcluirAbrirBarTender.Click += (_, _) =>
        {
            AbrirExecutavel("NexusBarTender.exe");
            Close();
        };
        wpBotoesConclusao.Children.Add(_btnConcluirAbrirBarTender);

        spPasso6.Children.Add(wpBotoesConclusao);
        _containerPasso6.Children.Add(spPasso6);
        _containerPasso6.Visibility = Visibility.Collapsed;
        painelCentral.Children.Add(_containerPasso6);

        Grid.SetRow(painelCentral, 1);
        raiz.Children.Add(painelCentral);

        // ==================== 2. RODAPÉ DE NAVEGAÇÃO ====================
        var rodape = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 14, 24, 14)
        };
        var gridRodape = new Grid();
        gridRodape.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gridRodape.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridRodape.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _btnCancelar = new Button
        {
            Content = "Cancelar",
            FontSize = 13,
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _btnCancelar.Click += (_, _) => Close();
        Grid.SetColumn(_btnCancelar, 0);
        gridRodape.Children.Add(_btnCancelar);

        var spBotoesDir = new StackPanel { Orientation = Orientation.Horizontal };
        _btnVoltar = new Button
        {
            Content = "< Voltar",
            FontSize = 13,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            IsEnabled = false
        };
        _btnVoltar.Click += (_, _) => IrParaPasso(_passoAtual - 1);
        spBotoesDir.Children.Add(_btnVoltar);

        _btnAvancar = new Button
        {
            Content = "Avançar >",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(24, 8, 24, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            IsEnabled = false
        };
        _btnAvancar.Click += async (_, _) => await AoClicarAvancarAsync();
        spBotoesDir.Children.Add(_btnAvancar);

        Grid.SetColumn(spBotoesDir, 2);
        gridRodape.Children.Add(spBotoesDir);

        rodape.Child = gridRodape;
        Grid.SetRow(rodape, 2);
        raiz.Children.Add(rodape);

        // Inicializa estado e tenta validar chave inicial se já houver
        Loaded += async (_, _) =>
        {
            AtualizarIndicadorEtapas();
            if (!string.IsNullOrWhiteSpace(_txtChave.Text))
            {
                await ValidarChaveAsync(mostrarAlerta: false);
            }
        };
    }

    private static StackPanel CriarCabecalhoEtapa(string titulo, string descricao)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(new TextBlock
        {
            Text = titulo,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        });
        sp.Children.Add(new TextBlock
        {
            Text = descricao,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            TextWrapping = TextWrapping.Wrap
        });
        return sp;
    }

    private static CheckBox CriarCardOpcaoApp(string titulo, string descricao, bool selecionado)
    {
        var sp = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        sp.Children.Add(new TextBlock
        {
            Text = titulo,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        });
        sp.Children.Add(new TextBlock
        {
            Text = descricao,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 6)
        };
        var chk = new CheckBox
        {
            Content = sp,
            IsChecked = selecionado,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        border.Child = chk;
        return chk;
    }

    private void AtualizarIndicadorEtapas()
    {
        for (int i = 0; i < 5; i++)
        {
            int etapaNumero = i + 1;
            if (etapaNumero == _passoAtual)
            {
                // Etapa Atual (Azul Brilhante)
                _etapaBadges[i].Background = new SolidColorBrush(Color.FromArgb(0x44, 0x3B, 0x82, 0xF6));
                _etapaBadges[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                _etapaTextos[i].Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                _etapaTextos[i].FontWeight = FontWeights.Bold;
            }
            else if (etapaNumero < _passoAtual)
            {
                // Etapa Concluída (Verde)
                _etapaBadges[i].Background = new SolidColorBrush(Color.FromArgb(0x33, 0x16, 0xA3, 0x4A));
                _etapaBadges[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                _etapaTextos[i].Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                _etapaTextos[i].FontWeight = FontWeights.SemiBold;
            }
            else
            {
                // Etapa Futura (Cinza)
                _etapaBadges[i].Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                _etapaBadges[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
                _etapaTextos[i].Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                _etapaTextos[i].FontWeight = FontWeights.Normal;
            }
        }
    }

    private void IrParaPasso(int novoPasso)
    {
        if (novoPasso < 1 || novoPasso > 6) return;
        _passoAtual = novoPasso;

        _containerPasso1.Visibility = _passoAtual == 1 ? Visibility.Visible : Visibility.Collapsed;
        _containerPasso2.Visibility = _passoAtual == 2 ? Visibility.Visible : Visibility.Collapsed;
        _containerPasso3.Visibility = _passoAtual == 3 ? Visibility.Visible : Visibility.Collapsed;
        _containerPasso4.Visibility = _passoAtual == 4 ? Visibility.Visible : Visibility.Collapsed;
        _containerPasso5.Visibility = _passoAtual == 5 ? Visibility.Visible : Visibility.Collapsed;
        _containerPasso6.Visibility = _passoAtual == 6 ? Visibility.Visible : Visibility.Collapsed;

        AtualizarIndicadorEtapas();
        AtualizarEstadoBotoes();
    }

    private void AtualizarEstadoBotoes()
    {
        if (_passoAtual == 5) // Instalando
        {
            _btnCancelar.IsEnabled = false;
            _btnVoltar.IsEnabled = false;
            _btnAvancar.IsEnabled = false;
            _btnAvancar.Content = "Instalando...";
            return;
        }

        if (_passoAtual == 6) // Concluído
        {
            _btnCancelar.Visibility = Visibility.Collapsed;
            _btnVoltar.Visibility = Visibility.Collapsed;
            _btnAvancar.Content = "Concluir e Fechar";
            _btnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            _btnAvancar.IsEnabled = true;
            return;
        }

        _btnCancelar.Visibility = Visibility.Visible;
        _btnVoltar.Visibility = Visibility.Visible;
        _btnVoltar.IsEnabled = _passoAtual > 1;

        switch (_passoAtual)
        {
            case 1:
                _btnAvancar.Content = "Avançar >";
                _btnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                _btnAvancar.IsEnabled = _licencaValidada != null && _licencaValidada.Ativa;
                break;

            case 2:
                _btnAvancar.Content = "Avançar >";
                _btnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                _btnAvancar.IsEnabled = _chkAceitoTermos.IsChecked == true;
                break;

            case 3:
                _btnAvancar.Content = "Avançar >";
                _btnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                _btnAvancar.IsEnabled = !string.IsNullOrWhiteSpace(_txtCaminhoPasta.Text);
                break;

            case 4:
                _btnAvancar.Content = "🚀 Instalar Agora";
                _btnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                _btnAvancar.IsEnabled = _chkNexus.IsChecked == true || _chkBarTender.IsChecked == true || _chkAtualizador.IsChecked == true;
                break;
        }
    }

    private async Task<ResultadoLicencaNuvem?> ValidarChaveAsync(bool mostrarAlerta = true)
    {
        var chave = _txtChave.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(chave))
        {
            _txtStatusLicenca.Text = "⚠️ Digite ou cole a sua chave de ativação para verificar a permissão.";
            _txtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            _cardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            _txtDetalhesLicenca.Visibility = Visibility.Collapsed;
            _licencaValidada = null;
            AtualizarEstadoBotoes();
            return null;
        }

        _btnValidarChave.IsEnabled = false;
        _btnValidarChave.Content = "Consultando...";
        _txtStatusLicenca.Text = "Conectando ao servidor oficial Nexus para validar chave e vagas de computadores...";
        _txtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD));
        _cardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        _txtDetalhesLicenca.Visibility = Visibility.Collapsed;

        try
        {
            var res = await _instalador.ValidarEAtivarLicencaAsync(chave);
            if (res.Ativa)
            {
                _licencaValidada = res;
                _op.ChaveLicenca = chave;
                if (!string.IsNullOrWhiteSpace(res.Empresa)) _op.Empresa = res.Empresa;

                _cardStatusLicenca.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x16, 0xA3, 0x4A));
                _cardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                _txtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                _txtStatusLicenca.Text = $"✔ Licença Autorizada! Cliente: {res.Cliente}" + (string.IsNullOrWhiteSpace(res.Empresa) ? "" : $" ({res.Empresa})");

                var validadeStr = res.Validade?.ToString("dd/MM/yyyy") ?? "Vitalícia";
                var vagasStr = (res.LimiteDispositivos ?? 0) > 0 ? $"{res.DispositivosUsados ?? 1} de {res.LimiteDispositivos} PCs vinculados" : "Dispositivos Ilimitados";
                _txtDetalhesLicenca.Text = $"Plano: {res.Plano ?? "Comercial"} • Validade: {validadeStr} • {vagasStr}\nComputador autorizado com sucesso. Clique em 'Avançar >' para prosseguir.";
                _txtDetalhesLicenca.Visibility = Visibility.Visible;
            }
            else
            {
                _licencaValidada = null;
                _cardStatusLicenca.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xDC, 0x26, 0x26));
                _cardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                _txtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                _txtStatusLicenca.Text = $"❌ Licença Não Autorizada: {res.Mensagem}";
                _txtDetalhesLicenca.Text = $"Situação: {res.Estado} • HWID deste computador: {_hwid}";
                _txtDetalhesLicenca.Visibility = Visibility.Visible;

                if (mostrarAlerta)
                {
                    MessageBox.Show(this, $"A chave de licença não pôde ser ativada:\n\n{res.Mensagem}\n\nCódigo deste PC: {_hwid}", "Licença Recusada", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            AtualizarEstadoBotoes();
            return res;
        }
        catch (Exception ex)
        {
            _licencaValidada = null;
            _cardStatusLicenca.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xDC, 0x26, 0x26));
            _cardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            _txtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            _txtStatusLicenca.Text = $"❌ Falha de Conexão: {ex.Message}";
            _txtDetalhesLicenca.Visibility = Visibility.Collapsed;
            if (mostrarAlerta)
            {
                MessageBox.Show(this, $"Erro ao conectar no servidor de licenças:\n\n{ex.Message}", "Erro de Conexão", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            AtualizarEstadoBotoes();
            return null;
        }
        finally
        {
            _btnValidarChave.IsEnabled = true;
            _btnValidarChave.Content = "🔍 Validar Chave";
        }
    }

    private async Task AoClicarAvancarAsync()
    {
        if (_passoAtual == 1)
        {
            if (_licencaValidada == null || !_licencaValidada.Ativa)
            {
                var res = await ValidarChaveAsync(mostrarAlerta: true);
                if (res == null || !res.Ativa) return;
            }
            IrParaPasso(2);
        }
        else if (_passoAtual == 2)
        {
            if (_chkAceitoTermos.IsChecked != true) return;
            IrParaPasso(3);
        }
        else if (_passoAtual == 3)
        {
            _op.Pasta = _txtCaminhoPasta.Text.Trim();
            if (string.IsNullOrWhiteSpace(_op.Pasta)) return;
            IrParaPasso(4);
        }
        else if (_passoAtual == 4)
        {
            _op.InstalarNexusLS = _chkNexus.IsChecked == true;
            _op.InstalarBarTender = _chkBarTender.IsChecked == true;
            _op.InstalarAtualizador = _chkAtualizador.IsChecked == true;
            _op.CriarAtalhosDesktop = _chkAtalhos.IsChecked == true;

            if (!_op.InstalarNexusLS && !_op.InstalarBarTender && !_op.InstalarAtualizador)
            {
                MessageBox.Show(this, "Selecione pelo menos um módulo para instalar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_instalador.HaProgramaAberto())
            {
                MessageBox.Show(this, "Feche todos os programas do Nexus que estiverem abertos antes de continuar com a instalação.", "Programas em Execução", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IrParaPasso(5);
            await ExecutarInstalacaoAsync();
        }
        else if (_passoAtual == 6)
        {
            Close();
        }
    }

    private async Task ExecutarInstalacaoAsync()
    {
        var progresso = new Progress<(double Percentual, string Texto)>(p =>
        {
            _barraProgresso.Value = p.Percentual;
            _txtPercentualProgresso.Text = $"{p.Percentual:F0}%";
            _txtMensagemProgresso.Text = p.Texto;
        });

        try
        {
            await _instalador.InstalarAsync(progresso);

            _barraProgresso.Value = 100;
            _txtPercentualProgresso.Text = "100%";
            _txtMensagemProgresso.Text = "Instalação concluída com sucesso!";

            // Configura a visibilidade dos botões finais
            _btnConcluirAbrirNexus.Visibility = _op.InstalarNexusLS ? Visibility.Visible : Visibility.Collapsed;
            _btnConcluirAbrirBarTender.Visibility = _op.InstalarBarTender ? Visibility.Visible : Visibility.Collapsed;

            // ABRE AUTOMATICAMENTE O NEXUS ATUALIZADOR conforme solicitado!
            var caminhoAtualizador = Path.Combine(_op.Pasta, "atualizador.exe");
            if (File.Exists(caminhoAtualizador))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(caminhoAtualizador) { UseShellExecute = true });
                    _txtConclusaoInfo.Text = "O Nexus foi instalado com sucesso e o Nexus Atualizador foi iniciado automaticamente!";
                }
                catch
                {
                    _txtConclusaoInfo.Text = "O Nexus foi instalado com sucesso! Clique abaixo para abrir o Nexus Atualizador.";
                }
            }
            else
            {
                _txtConclusaoInfo.Text = "O Nexus foi instalado com sucesso!";
            }

            IrParaPasso(6);
        }
        catch (Exception ex)
        {
            _barraProgresso.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            _txtMensagemProgresso.Text = $"Erro durante a instalação: {ex.Message}";
            _txtMensagemProgresso.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));

            MessageBox.Show(this, $"Ocorreu uma falha durante a instalação:\n\n{ex.Message}", "Falha na Instalação", MessageBoxButton.OK, MessageBoxImage.Error);

            IrParaPasso(4); // Permite tentar de novo
        }
    }

    private void AbrirExecutavel(string nomeExe)
    {
        var caminho = Path.Combine(_op.Pasta, nomeExe);
        if (File.Exists(caminho))
        {
            Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
        }
    }

    private static string ObterTextoTermosDeUso()
    {
        return
            "CONTRATO DE LICENÇA DE USO DE SOFTWARE — NEXUS LS\n\n" +
            "1. CONCESSÃO DA LICENÇA: A Nexus LS concede ao cliente uma licença comercial, intransferível e não exclusiva para utilizar o software Nexus LS e seus módulos correspondentes (Frente de Caixa PDV, BarTender e Atualizador) de acordo com o plano contratado.\n\n" +
            "2. CONTROLE DE DISPOSITIVOS E HWID: O uso é vinculado ao identificador de hardware (HWID) de cada computador autorizado. O número de terminais operando simultaneamente não poderá exceder o limite estabelecido no plano da licença.\n\n" +
            "3. PROTEÇÃO DE DADOS: O software processa e armazena os dados de operações e estoque com criptografia e segurança, atendendo às diretrizes da LGPD (Lei Geral de Proteção de Dados).\n\n" +
            "4. ATUALIZAÇÕES E SUPORTE: O cliente tem direito a atualizações de segurança e correções durante o período de vigência da licença comercial ativa.\n\n" +
            "5. RESPONSABILIDADE: O usuário é responsável pela guarda de suas senhas, cópias de segurança (backups) e pela regularidade fiscal de suas operações comerciais.";
    }
}
