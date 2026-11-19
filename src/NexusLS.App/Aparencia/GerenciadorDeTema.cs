using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using NexusLS.Core.Services;
using Wpf.Ui.Appearance;

namespace NexusLS.App.Aparencia;

/// <summary>
/// Tema Claro / Escuro / Automático. As telas usam os pincéis do Tokens.xaml (StaticResource), então o tema muda
/// a COR de cada pincel no lugar: tudo que já está na tela troca na hora, sem reabrir janelas. Os controles da
/// biblioteca WPF-UI trocam pelo ApplicationThemeManager. No Automático, acompanha o Windows e reage a mudança
/// feita enquanto o programa está aberto (SystemEvents.UserPreferenceChanged).
/// </summary>
public static class GerenciadorDeTema
{
    private static readonly Color Destaque = Color.FromRgb(0x25, 0x63, 0xEB);

    /// <summary>Paleta do modo escuro: chave do Tokens.xaml → cor. As chaves que não estão aqui ficam iguais nos dois temas.</summary>
    public static readonly IReadOnlyDictionary<string, string> PaletaEscura = new Dictionary<string, string>
    {
        ["CorFundo"] = "#0F1217",
        ["CorPainel"] = "#1A1E25",
        ["CorMenu"] = "#151920",
        ["CorTexto"] = "#E8EAED",
        ["CorTextoSuave"] = "#A3ABB8",
        ["CorBorda"] = "#2C323C",
        ["CorBordaCampo"] = "#3C4450",
        ["CorBordaForte"] = "#5F6978",
        ["CorLinhaGrade"] = "#262B34",
        ["CorHover"] = "#262C36",
        ["CorCabecalho"] = "#20252E",
        ["CorPrimaria"] = "#3B7DF0",
        ["CorPrimariaHover"] = "#5B93F5",
        ["CorPrimariaPressionada"] = "#2A66D6",
        ["CorSelecao"] = "#1F3A66",
        ["CorSelecaoSuave"] = "#172A47",
        ["CorFoco"] = "#6EA2F8",
        ["CorSobrePrimariaSuave"] = "#DBEAFE",
        ["CorMenuAtivo"] = "#172A47",
        ["CorMenuAtivoTexto"] = "#8DB4FA",
        ["CorSucesso"] = "#4ADE80",
        ["CorPositivo"] = "#4ADE80",
        ["CorPerigo"] = "#D83B30",
        ["CorPerigoHover"] = "#E5574C",
        ["CorPerigoPressionado"] = "#B93128",
        ["CorNegativo"] = "#F87171",
        ["CorAlerta"] = "#4A3A10",
        ["CorAviso"] = "#FBBF24",
        ["CorAvisoFundo"] = "#3A2D12",
        ["CorAtencaoFundo"] = "#40291A",
        ["CorAtencaoBorda"] = "#F97316",
        ["CorAtencaoTexto"] = "#FDBA74",
        ["CorSobreposicao"] = "#CC000000",
        ["CorColunaFundo"] = "#12161C",
        ["CorSerieOutros"] = "#6B7280",
        // Pedidos: as cores das etapas são texto e faixa; no escuro precisam clarear para manter o contraste.
        ["CorEtapaNovo"] = "#5B93F5",
        ["CorEtapaSeparacao"] = "#F59E0B",
        ["CorEtapaSeparado"] = "#A78BFA",
        ["CorEtapaPronto"] = "#2DD4BF",
        ["CorEtapaEntregue"] = "#4ADE80",
    };

    private static readonly Dictionary<string, Color> _claras = new();
    private static ModoTema _modo = ModoTema.Automatico;
    private static bool _ouvindoWindows;

    /// <summary>Verdadeiro quando as cores escuras estão aplicadas agora.</summary>
    public static bool EscuroAtivo { get; private set; }

    public static ModoTema Modo => _modo;

    /// <summary>Avisa (na thread da tela) depois que o tema aplicado mudou.</summary>
    public static event Action<bool>? Mudou;

    /// <summary>Lê no registro se o Windows está com aplicativos no tema escuro (HKCU ... Themes\Personalize, AppsUseLightTheme).</summary>
    public static bool WindowsEstaEscuro()
    {
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return RegrasDeTema.WindowsEscuroPeloRegistro(chave?.GetValue("AppsUseLightTheme"));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>Chamado ao abrir o programa: aplica o tema guardado e passa a acompanhar o Windows.</summary>
    public static void Iniciar(ModoTema modo)
    {
        if (!_ouvindoWindows)
        {
            _ouvindoWindows = true;
            SystemEvents.UserPreferenceChanged += AoMudarPreferenciaDoWindows;
            // Janelas novas (diálogos, relatórios) já nascem com a barra de título do tema atual.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => PrepararJanela((Window)s)));
            if (Application.Current is { } app) app.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= AoMudarPreferenciaDoWindows;
        }
        Definir(modo);
    }

    /// <summary>Muda o modo (escolha em Configurações > Aparência) e aplica na hora.</summary>
    public static void Definir(ModoTema modo)
    {
        _modo = modo;
        Reaplicar();
    }

    private static void AoMudarPreferenciaDoWindows(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_modo != ModoTema.Automatico) return;
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        // O evento chega numa thread de segundo plano: a troca de cores precisa rodar na thread da tela.
        Application.Current?.Dispatcher.BeginInvoke(new Action(Reaplicar));
    }

    private static void Reaplicar()
    {
        var escuro = RegrasDeTema.UsarEscuro(_modo, _modo == ModoTema.Automatico && WindowsEstaEscuro());
        Aplicar(escuro);
    }

    /// <summary>Aplica a paleta clara ou escura. Público para os testes (que escolhem o lado sem passar pelo Windows).</summary>
    public static void Aplicar(bool escuro)
    {
        var app = Application.Current;
        if (app is null) { EscuroAtivo = escuro; return; }

        var mudou = escuro != EscuroAtivo;
        EscuroAtivo = escuro;
        TrocarPinceis(app.Resources, escuro);
        try
        {
            var tema = escuro ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(tema, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
            ApplicationAccentColorManager.Apply(Destaque, tema);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            NexusLS.Core.Diagnostico.RegistroDeLog.Info("Tema: a biblioteca de controles não aceitou a troca: " + e.Message);
        }
        foreach (Window janela in app.Windows) AplicarBarraDeTitulo(janela, escuro);
        if (mudou) Mudou?.Invoke(escuro);
    }

    /// <summary>
    /// Troca cada pincel do Tokens.xaml por um novo com a cor do tema. As telas usam DynamicResource, então acompanham
    /// a troca na hora. (Não dá para só mudar a cor do pincel antigo: os estilos congelam os pincéis que usam.)
    /// O Tokens.xaml é mesclado em mais de um dicionário, por isso a troca vale para todas as cópias.
    /// </summary>
    private static void TrocarPinceis(ResourceDictionary raiz, bool escuro)
    {
        foreach (var (chave, hexEscuro) in PaletaEscura)
        {
            if (!_claras.ContainsKey(chave))
            {
                if (Procurar(raiz, chave) is not SolidColorBrush original) continue;
                _claras[chave] = original.Color;
            }
            var cor = escuro ? (Color)ColorConverter.ConvertFromString(hexEscuro) : _claras[chave];
            var novo = new SolidColorBrush(cor);
            novo.Freeze();
            Substituir(raiz, chave, novo, new HashSet<ResourceDictionary>());
        }
    }

    private static object? Procurar(ResourceDictionary recursos, string chave)
    {
        if (recursos.Contains(chave)) return recursos[chave];
        foreach (var filho in recursos.MergedDictionaries)
            if (Procurar(filho, chave) is { } achou) return achou;
        return null;
    }

    private static void Substituir(ResourceDictionary recursos, string chave, Brush novo, HashSet<ResourceDictionary> vistos)
    {
        if (!vistos.Add(recursos)) return;
        if (recursos.Contains(chave) && recursos[chave] is SolidColorBrush) recursos[chave] = novo;
        foreach (var filho in recursos.MergedDictionaries) Substituir(filho, chave, novo, vistos);
    }


    /// <summary>
    /// Toda janela nova ganha barra de título do tema e, se ninguém definiu fundo e cor de texto (os diálogos montados
    /// no código), passa a usar os do tema: assim não sobra janela branca no meio do tema escuro.
    /// </summary>
    private static void PrepararJanela(Window janela)
    {
        if (janela.ReadLocalValue(Window.BackgroundProperty) == DependencyProperty.UnsetValue)
            janela.SetResourceReference(Window.BackgroundProperty, "CorFundo");
        if (janela.ReadLocalValue(Window.ForegroundProperty) == DependencyProperty.UnsetValue)
            janela.SetResourceReference(Window.ForegroundProperty, "CorTexto");
        AplicarBarraDeTitulo(janela, EscuroAtivo);
    }

    // ---- Barra de título escura nas janelas comuns (a FluentWindow da biblioteca já cuida da dela) ----

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamanho);

    private const int ModoEscuroImersivo = 20;

    public static void AplicarBarraDeTitulo(Window janela, bool escuro)
    {
        try
        {
            var hwnd = new WindowInteropHelper(janela).Handle;
            if (hwnd == IntPtr.Zero) return;
            var valor = escuro ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, ModoEscuroImersivo, ref valor, sizeof(int));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { /* Windows antigo: fica a barra clara */ }
    }
}
