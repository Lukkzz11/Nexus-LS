using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Data;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoZoom(string Texto, int Percentual);

public record AtalhoDeTeclado(string Tecla, string Funcao);

/// <summary>Abas Aparência, Vendas (preferências deste computador) e Sistema (onde ficam os dados, licença, atalhos).</summary>
public partial class ConfiguracoesViewModel
{
    private PreferenciasStore? _preferencias;
    private Action<double>? _aoMudarZoom;
    private bool _carregandoPreferencias;

    public IReadOnlyList<OpcaoZoom> Zooms { get; } = Preferencias.ZoomsPermitidos
        .Select(z => new OpcaoZoom(z == 100 ? "100% (normal)" : $"{z}%", z)).ToList();

    public IReadOnlyList<AtalhoDeTeclado> Atalhos { get; } = new AtalhoDeTeclado[]
    {
        new("F1", "Abrir o PDV (de qualquer tela)"),
        new("F2", "PDV: ir para a busca de produto"),
        new("Enter", "PDV: escolher o produto / fechar a venda (busca vazia)"),
        new("F10", "PDV: fechar a venda"),
        new("← →", "PDV: trocar tamanho, forma de pagamento ou tipo de desconto"),
        new("↑ ↓", "PDV: quantidade, parcelas ou linha de opções"),
        new("Espaço", "PDV (busca vazia): alternar varejo e atacado"),
        new("Del", "PDV: remover o item selecionado"),
        new("Esc", "Fechar a janela aberta"),
        new("Ctrl+S", "Salvar (nas Configurações)")
    };

    [ObservableProperty] private OpcaoZoom? _zoomSelecionado;
    [ObservableProperty] private string _descontoPorPeca = "";
    [ObservableProperty] private string _mensagemPreferencias = "";
    [ObservableProperty] private bool _mensagemPreferenciasEhErro;

    public string PastaDeDados { get; private set; } = "";
    [ObservableProperty] private string _tamanhoDoBanco = "";
    [ObservableProperty] private string _licencaTexto = "";
    public bool TemPreferencias => _preferencias is not null;

    /// <summary>A tela abre a pasta no Explorer (o ViewModel não conhece processos do Windows).</summary>
    public Action<string> AbrirPasta { get; set; } = _ => { };

    /// <summary>Liga as preferências e os dados do sistema. <paramref name="aoMudarZoom"/> recebe o fator (1,0 = normal).</summary>
    public void UsarPreferencias(PreferenciasStore store, Action<double> aoMudarZoom, Func<string>? licenca = null, string? pastaDados = null)
    {
        _preferencias = store;
        _aoMudarZoom = aoMudarZoom;
        var p = store.Carregar();
        _carregandoPreferencias = true;
        ZoomSelecionado = Zooms.FirstOrDefault(z => z.Percentual == p.ZoomPercentual) ?? Zooms.First(z => z.Percentual == 100);
        DescontoPorPeca = Dinheiro.FormatarParaEdicao(p.DescontoPorPecaCentavos);
        _carregandoPreferencias = false;
        OnPropertyChanged(nameof(TemPreferencias));

        LicencaTexto = licenca?.Invoke() ?? "";
        try
        {
            var banco = CaminhoBanco.Padrao();
            PastaDeDados = pastaDados ?? Path.GetDirectoryName(banco)!;
            TamanhoDoBanco = File.Exists(banco) ? FormatarTamanho(new FileInfo(banco).Length) : "ainda não criado";
        }
        catch (IOException) { PastaDeDados = pastaDados ?? ""; }
        OnPropertyChanged(nameof(PastaDeDados));
    }

    public static string FormatarTamanho(long bytes) =>
        bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / 1024.0 / 1024.0:0.#} MB";

    /// <summary>O zoom vale na hora e fica guardado; o resto das preferências só com o botão Salvar.</summary>
    partial void OnZoomSelecionadoChanged(OpcaoZoom? value)
    {
        if (_carregandoPreferencias || value is null || _preferencias is null) return;
        try
        {
            var p = _preferencias.Carregar();
            p.ZoomPercentual = value.Percentual;
            _preferencias.Salvar(p);
            _aoMudarZoom?.Invoke(value.Percentual / 100.0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MensagemPreferencias = "Não foi possível guardar o tamanho das telas: " + ex.Message;
            MensagemPreferenciasEhErro = true;
        }
    }

    [RelayCommand]
    private void SalvarPreferencias()
    {
        if (_preferencias is null) return;
        try
        {
            if (!Dinheiro.TentarConverter(DescontoPorPeca, out var centavos))
                throw new RegraDeNegocioException("Informe o desconto por peça em reais (ex.: 2,00).");
            var p = _preferencias.Carregar();
            p.DescontoPorPecaCentavos = centavos;
            _preferencias.Salvar(p);
            DescontoPorPeca = Dinheiro.FormatarParaEdicao(centavos);
            MensagemPreferencias = "Preferências salvas.";
            MensagemPreferenciasEhErro = false;
        }
        catch (RegraDeNegocioException ex)
        {
            MensagemPreferencias = ex.Message;
            MensagemPreferenciasEhErro = true;
        }
    }

    [RelayCommand]
    private void AbrirPastaDeDados()
    {
        if (!string.IsNullOrEmpty(PastaDeDados)) AbrirPasta(PastaDeDados);
    }

    [RelayCommand]
    public void AbrirAtualizador()
    {
        try
        {
            var pastaApp = AppContext.BaseDirectory;
            var caminhos = new[]
            {
                Path.Combine(pastaApp, "atualizador.exe"),
                @"C:\NexusLS\publicar\atualizador.exe"
            };
            var exe = caminhos.FirstOrDefault(File.Exists);
            if (exe != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)!
                });
                return;
            }
            AbrirPortalWeb();
        }
        catch (Exception ex)
        {
            MensagemPreferencias = "Não foi possível abrir o atualizador: " + ex.Message;
            MensagemPreferenciasEhErro = true;
        }
    }

    [RelayCommand]
    public void AbrirPortalWeb()
    {
        try
        {
            var url = "https://github.com/Lukkzz11/Nexus-LS---Update";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MensagemPreferencias = "Não foi possível abrir o portal: " + ex.Message;
            MensagemPreferenciasEhErro = true;
        }
    }
}
