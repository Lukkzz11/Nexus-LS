using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;

namespace NexusLS.BarTender.ViewModels;

/// <summary>
/// Predefinição de impressão (impressora, papel e ajuste de posição) guardada neste computador,
/// para o operador não configurar tudo de novo a cada impressão.
/// </summary>
public partial class PreparacaoImpressaoViewModel
{
    private const int DescerMaximoMm = 30;

    private string? _caminhoPreferencias;
    private PredefinicaoImpressao? _predefinicao;
    private bool _impressoraDaPredefinicaoAplicada;

    /// <summary>Quantos milímetros empurrar o desenho para baixo, quando a impressora imprime alto demais.</summary>
    [ObservableProperty]
    private int _descerMm;

    /// <summary>Liga a memória da tela. Sem isto (testes), nada é lido nem gravado em disco.</summary>
    public void UsarPreferencias(string caminho)
    {
        _caminhoPreferencias = caminho;
        _predefinicao = PreferenciasBarTender.Carregar(caminho).Impressao;
        if (_predefinicao is not null) DescerMm = Math.Clamp(_predefinicao.DescerMm, 0, DescerMaximoMm);
    }

    partial void OnDescerMmChanged(int value) => EnvioConcluido = false;

    [RelayCommand]
    private void DescerMais() => DescerMm = Math.Min(DescerMaximoMm, DescerMm + 1);

    [RelayCommand]
    private void DescerMenos() => DescerMm = Math.Max(0, DescerMm - 1);

    [RelayCommand]
    private void SalvarPredefinicao()
    {
        if (GuardarPredefinicao())
            MensagemStatus = "Predefinição salva: impressora, papel e ajuste serão usados na próxima vez.";
    }

    private bool GuardarPredefinicao()
    {
        if (_caminhoPreferencias is null) return false;

        _predefinicao = new PredefinicaoImpressao(ImpressoraSelecionada, MidiaAtual with { Nome = NomeMidia }, DescerMm);
        var preferencias = PreferenciasBarTender.Carregar(_caminhoPreferencias);
        preferencias.Impressao = _predefinicao;
        preferencias.Salvar(_caminhoPreferencias);
        return true;
    }

    /// <summary>Volta a impressora e o papel salvos. Falso quando não há papel salvo.</summary>
    private bool AplicarPredefinicao()
    {
        if (_predefinicao is null) return false;

        // A impressora só é trocada na primeira vez: depois vale o que o operador escolher na tela.
        if (!_impressoraDaPredefinicaoAplicada && _predefinicao.Impressora is { } nome && ImpressorasInstaladas.Contains(nome))
        {
            ImpressoraSelecionada = nome;
            _impressoraDaPredefinicaoAplicada = true;
        }

        if (_predefinicao.Midia is not { } salva) return false;

        var midia = MidiasDisponiveis.FirstOrDefault(m => MedidasIguais(m, salva));
        if (midia is null)
        {
            MidiasDisponiveis.Insert(0, salva);
            midia = salva;
        }

        MidiaSelecionada = midia;
        CarregarMidia(midia);
        return true;
    }

    /// <summary>
    /// O que vai de fato para a impressora. Com ajuste, o desenho encolhe o mesmo tanto que desce,
    /// para o rodapé não ser cortado no fim da etiqueta.
    /// </summary>
    private (PerfilMidia Midia, ModeloEtiquetaDesign Modelo) ParaEnvio()
    {
        var midia = MidiaAtual;
        var modelo = ModeloParaMidia!;
        var descer = Math.Min(DescerMm, Math.Max(0, midia.AlturaEtiquetaMm - 10));
        if (descer <= 0 || !midia.EhRolo || ModeloOriginal is null) return (midia, modelo);

        var altura = midia.AlturaEtiquetaMm - descer;
        return (
            midia with { AlturaEtiquetaMm = altura, MargemSuperiorMm = midia.MargemSuperiorMm + descer },
            ModeloMidiaService.Ajustar(ModeloOriginal, midia.LarguraEtiquetaMm, altura).Modelo);
    }
}
