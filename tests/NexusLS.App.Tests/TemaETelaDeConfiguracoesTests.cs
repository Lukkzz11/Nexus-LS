using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NexusLS.App.Aparencia;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Tema Claro / Escuro / Automático e a janela própria das Configurações.</summary>
public sealed class TemaETelaDeConfiguracoesTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "pref-tema-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        Capturador.Rodar(() => GerenciadorDeTema.Aplicar(false)); // não deixa o tema escuro vazar para os outros testes
        _banco.Dispose();
        if (File.Exists(_arquivo)) File.Delete(_arquivo);
    }

    private ConfiguracoesViewModel CriarVm()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        return new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
    }

    // ---- Resolver Automático -> claro/escuro (sem WPF) ----

    [Theory]
    [InlineData(ModoTema.Claro, false, false)]
    [InlineData(ModoTema.Claro, true, false)]
    [InlineData(ModoTema.Escuro, false, true)]
    [InlineData(ModoTema.Escuro, true, true)]
    [InlineData(ModoTema.Automatico, false, false)]
    [InlineData(ModoTema.Automatico, true, true)]
    public void Resolver_modo_em_claro_ou_escuro(ModoTema modo, bool windowsEscuro, bool esperadoEscuro) =>
        Assert.Equal(esperadoEscuro, RegrasDeTema.UsarEscuro(modo, windowsEscuro));

    [Theory]
    [InlineData(0, true)]      // AppsUseLightTheme = 0 -> aplicativos escuros
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(null, false)]  // chave ausente: claro, o padrão do Windows
    [InlineData("0", false)]   // tipo inesperado: claro
    public void Valor_do_registro_do_windows_vira_escuro_ou_claro(object? valor, bool esperadoEscuro) =>
        Assert.Equal(esperadoEscuro, RegrasDeTema.WindowsEscuroPeloRegistro(valor));

    [Theory]
    [InlineData("Claro", ModoTema.Claro)]
    [InlineData("escuro", ModoTema.Escuro)]
    [InlineData("Automatico", ModoTema.Automatico)]
    [InlineData("", ModoTema.Automatico)]
    [InlineData(null, ModoTema.Automatico)]
    [InlineData("roxo", ModoTema.Automatico)]
    [InlineData("7", ModoTema.Automatico)]
    public void Texto_guardado_vira_modo_e_o_que_nao_se_conhece_e_automatico(string? texto, ModoTema esperado) =>
        Assert.Equal(esperado, RegrasDeTema.Ler(texto));

    // ---- Persistência e ViewModel ----

    [Fact]
    public void Tema_novo_comeca_automatico_e_fica_guardado_no_arquivo_de_preferencias()
    {
        var store = new PreferenciasStore(_arquivo);
        Assert.Equal("Automatico", store.Carregar().Tema);

        var p = store.Carregar();
        p.Tema = "Escuro";
        store.Salvar(p);
        Assert.Equal("Escuro", new PreferenciasStore(_arquivo).Carregar().Tema);
        Assert.Contains("\"Tema\": \"Escuro\"", File.ReadAllText(_arquivo));
    }

    [Fact]
    public void Tema_invalido_no_arquivo_volta_para_automatico()
    {
        File.WriteAllText(_arquivo, "{ \"ZoomPercentual\": 110, \"Tema\": \"arco-iris\" }");
        var p = new PreferenciasStore(_arquivo).Carregar();
        Assert.Equal("Automatico", p.Tema);
        Assert.Equal(110, p.ZoomPercentual);
    }

    [Fact]
    public void Preferencia_antiga_sem_tema_abre_automatico()
    {
        File.WriteAllText(_arquivo, "{ \"ZoomPercentual\": 125 }");
        Assert.Equal(ModoTema.Automatico, RegrasDeTema.Ler(new PreferenciasStore(_arquivo).Carregar().Tema));
    }

    [Fact]
    public void Escolher_o_tema_em_Aparencia_aplica_na_hora_e_guarda_sem_mexer_no_zoom()
    {
        var vm = CriarVm();
        var store = new PreferenciasStore(_arquivo);
        var p0 = store.Carregar(); p0.ZoomPercentual = 110; store.Salvar(p0);
        var aplicados = new List<ModoTema>();
        vm.AoMudarTema = aplicados.Add;
        vm.UsarPreferencias(store, _ => { }, null, Path.GetTempPath());

        Assert.Equal(new[] { "Automático (segue o Windows)", "Claro", "Escuro" }, vm.Temas.Select(t => t.Texto));
        Assert.Equal(ModoTema.Automatico, vm.TemaSelecionado!.Modo);
        Assert.Empty(aplicados); // carregar não dispara a troca

        vm.TemaSelecionado = vm.Temas.First(t => t.Modo == ModoTema.Escuro);
        Assert.Equal(new[] { ModoTema.Escuro }, aplicados);
        var guardado = store.Carregar();
        Assert.Equal("Escuro", guardado.Tema);
        Assert.Equal(110, guardado.ZoomPercentual);

        var outraTela = CriarVm();
        outraTela.UsarPreferencias(new PreferenciasStore(_arquivo), _ => { }, null, Path.GetTempPath());
        Assert.Equal(ModoTema.Escuro, outraTela.TemaSelecionado!.Modo); // reabrir mostra a escolha guardada
    }

    // ---- Paleta ----

    private static string Tokens()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusLS.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "NexusLS.App", "Resources", "Tokens.xaml"));
    }

    private static Dictionary<string, string> CoresDoTokens() =>
        Regex.Matches(Tokens(), @"<SolidColorBrush x:Key=""(?<k>\w+)"" Color=""(?<c>[^""]+)""")
            .ToDictionary(m => m.Groups["k"].Value, m => m.Groups["c"].Value);

    private static Color Cor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static double Luminancia(Color c)
    {
        static double Canal(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);
    }

    private static double Contraste(string a, string b)
    {
        var (la, lb) = (Luminancia(Cor(a)), Luminancia(Cor(b)));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void Toda_cor_da_paleta_escura_existe_no_Tokens_xaml()
    {
        var tokens = CoresDoTokens();
        foreach (var chave in GerenciadorDeTema.PaletaEscura.Keys)
            Assert.True(tokens.ContainsKey(chave), $"{chave} não existe em Resources/Tokens.xaml");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Texto_e_fundos_tem_contraste_legivel_nos_dois_temas(bool escuro)
    {
        var tokens = CoresDoTokens();
        string C(string k) => escuro && GerenciadorDeTema.PaletaEscura.TryGetValue(k, out var d) ? d : tokens[k];

        // texto de corpo (mínimo 4,5:1 das recomendações de acessibilidade)
        foreach (var fundo in new[] { "CorFundo", "CorPainel", "CorMenu", "CorHover", "CorCabecalho", "CorSelecao", "CorMenuAtivo" })
            Assert.True(Contraste(C("CorTexto"), C(fundo)) >= 7, $"CorTexto sobre {fundo}: {Contraste(C("CorTexto"), C(fundo)):0.0}");
        foreach (var fundo in new[] { "CorFundo", "CorPainel", "CorMenu", "CorCabecalho" })
            Assert.True(Contraste(C("CorTextoSuave"), C(fundo)) >= 4.5, $"CorTextoSuave sobre {fundo}: {Contraste(C("CorTextoSuave"), C(fundo)):0.0}");
        // texto colorido sobre painel
        foreach (var texto in new[] { "CorPositivo", "CorNegativo", "CorAviso", "CorSucesso" })
            Assert.True(Contraste(C(texto), C("CorPainel")) >= 4.5, $"{texto} sobre CorPainel: {Contraste(C(texto), C("CorPainel")):0.0}");
        Assert.True(Contraste(C("CorPrimaria"), C("CorPainel")) >= 4.0, "azul sobre painel");
        Assert.True(Contraste(C("CorMenuAtivoTexto"), C("CorMenuAtivo")) >= 4.5, "item ativo do menu");
        Assert.True(Contraste(C("CorTexto"), C("CorAlerta")) >= 7, "texto sobre aviso amarelo");
        Assert.True(Contraste(C("CorAtencaoTexto"), C("CorAtencaoFundo")) >= 4.5, "texto de atenção");
        // botões: texto branco sobre o azul e sobre o vermelho
        Assert.True(Contraste(C("CorSobrePrimaria"), C("CorPrimaria")) >= 3.8, "branco sobre azul");
        Assert.True(Contraste(C("CorSobrePrimaria"), C("CorPerigo")) >= 4.0, "branco sobre perigo");
    }

    [Fact]
    public void Aplicar_escuro_muda_as_cores_na_tela_e_claro_devolve_as_originais()
    {
        var claroOriginal = Cor(CoresDoTokens()["CorFundo"]);
        Capturador.Rodar(() =>
        {
            GerenciadorDeTema.Aplicar(true);
            Assert.True(GerenciadorDeTema.EscuroAtivo);
            Assert.Equal(Cor("#0F1217"), ((SolidColorBrush)Application.Current.Resources["CorFundo"]).Color);
            Assert.Equal(Cor("#E8EAED"), ((SolidColorBrush)Application.Current.Resources["CorTexto"]).Color);

            GerenciadorDeTema.Aplicar(false);
            Assert.False(GerenciadorDeTema.EscuroAtivo);
            Assert.Equal(claroOriginal, ((SolidColorBrush)Application.Current.Resources["CorFundo"]).Color);
        });
    }

    [Fact]
    public void Um_estilo_ja_criado_acompanha_a_troca_de_tema()
    {
        // O Menu usa StaticResource dentro de estilos: precisa mudar junto, sem reabrir a tela.
        Capturador.Rodar(() =>
        {
            var botao = new System.Windows.Controls.Button { Style = (Style)Application.Current.Resources["BotaoMenu"] };
            var painel = new System.Windows.Controls.Border { Style = (Style)Application.Current.Resources["PainelLateral"] };
            var janela = new Window { Content = new System.Windows.Controls.StackPanel { Children = { botao, painel } }, Left = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            janela.Show();
            try
            {
                GerenciadorDeTema.Aplicar(true);
                Assert.Equal(Cor("#151920"), ((SolidColorBrush)painel.Background).Color);
                Assert.Equal(Cor("#E8EAED"), ((SolidColorBrush)botao.Foreground).Color);
            }
            finally { janela.Close(); }
        });
    }

    // ---- Janela própria das Configurações ----

    [Fact]
    public void Configuracoes_abrem_em_janela_unica_redimensionavel_e_esc_fecha()
    {
        ConfiguracoesWindow? primeira = null, segunda = null;
        Capturador.Rodar(() =>
        {
            primeira = ConfiguracoesWindow.AbrirCom(CriarVm, null, 1.0);
            segunda = ConfiguracoesWindow.AbrirCom(() => throw new InvalidOperationException("não devia criar outra"), null, 1.0);
            try
            {
                Assert.Same(primeira, segunda);
                Assert.Same(primeira, ConfiguracoesWindow.Aberta);
                Assert.Equal(ResizeMode.CanResize, primeira.ResizeMode);
                Assert.Equal(WindowStartupLocation.CenterOwner, primeira.WindowStartupLocation);
                Assert.IsType<ConfiguracoesViewModel>(primeira.DataContext);
                Assert.Single(Application.Current.Windows.OfType<ConfiguracoesWindow>());

                var origem = PresentationSource.FromVisual(primeira)!;
                var esc = new KeyEventArgs(Keyboard.PrimaryDevice, origem, 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent };
                primeira.RaiseEvent(esc);
                Assert.Null(ConfiguracoesWindow.Aberta);
                Assert.False(primeira.IsVisible);
            }
            finally { primeira?.Close(); }
        });
    }

    [Fact]
    public void Zoom_das_configuracoes_vale_na_janela_e_pode_abrir_de_novo_depois_de_fechar()
    {
        Capturador.Rodar(() =>
        {
            var janela = ConfiguracoesWindow.AbrirCom(CriarVm, null, 1.25);
            try
            {
                var escala = Assert.IsType<ScaleTransform>(((FrameworkElement)janela.Content).LayoutTransform);
                Assert.Equal(1.25, escala.ScaleX);
                janela.AplicarZoom(1.0);
                Assert.Same(Transform.Identity, ((FrameworkElement)janela.Content).LayoutTransform);
            }
            finally { janela.Close(); }
            var nova = ConfiguracoesWindow.AbrirCom(CriarVm, null, 1.0);
            try { Assert.NotSame(janela, nova); }
            finally { nova.Close(); }
        });
    }
}
