using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Fiscal;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Página "Fiscal": só a preparação para a NFC-e (nada é emitido). CNPJ, inscrição e cidade vêm da página Loja.</summary>
public partial class ConfiguracoesViewModel
{
    public IReadOnlyList<string> RegimesFiscais { get; } = PreparoFiscal.Regimes.Values.ToList();
    public IReadOnlyList<string> AmbientesFiscais { get; } = new[] { "Homologação (teste)", "Produção" };

    [ObservableProperty] private string? _regimeFiscal;
    [ObservableProperty] private string _ambienteFiscal = "Homologação (teste)";
    [ObservableProperty] private string _serieFiscal = "1";
    [ObservableProperty] private string _cscIdFiscal = "";
    [ObservableProperty] private string _ncmPadraoFiscal = "";
    [ObservableProperty] private string _cfopPadraoFiscal = "";
    [ObservableProperty] private string _mensagemFiscal = "";
    [ObservableProperty] private bool _mensagemFiscalEhErro;
    public ObservableCollection<string> ChecklistFiscal { get; } = new();

    // ----- Conferência de uma nota de teste (só monta o XML para olhar; NÃO emite, NÃO assina, NÃO envia) -----
    private ConferenciaNfce? _conferenciaNfce;
    private ResultadoNfce? _ultimaConferencia;
    public ObservableCollection<string> ResultadoNotaTeste { get; } = new();
    [ObservableProperty] private bool _podeSalvarXmlConferencia;

    /// <summary>Abre o diálogo de salvar: recebe o nome sugerido e devolve o caminho escolhido (nulo = cancelou). Troque nos testes.</summary>
    public Func<string, string?> EscolherCaminhoXmlConferencia { get; set; } = _ => null;

    public void UsarConferenciaNfce(ConferenciaNfce conferencia) => _conferenciaNfce = conferencia;
    public bool TemConferenciaNfce => _conferenciaNfce is not null;

    [RelayCommand]
    private async Task ConferirNotaTesteAsync()
    {
        if (_conferenciaNfce is null || _seguranca is null) return;
        ResultadoNotaTeste.Clear();
        PodeSalvarXmlConferencia = false;
        // Confere com o que está SALVO na página Fiscal e na Loja.
        var r = await _conferenciaNfce.ConferirUltimaVendaAsync(_seguranca.Carregar());
        _ultimaConferencia = r;
        ResultadoNotaTeste.Add("CONFERÊNCIA — não é uma nota fiscal. Nada foi emitido, assinado nem enviado.");
        if (r.Bloqueio is not null)
        {
            ResultadoNotaTeste.Add("✖  " + r.Bloqueio);
            return;
        }
        ResultadoNotaTeste.Add($"Venda conferida: a última gravada (número de conferência {r.Numero}).");
        if (r.Chave is not null) ResultadoNotaTeste.Add("Chave de acesso de teste: " + r.Chave);
        if (r.Pendencias.Count == 0) ResultadoNotaTeste.Add("✔  Os cadastros estão completos para montar a nota.");
        else
        {
            ResultadoNotaTeste.Add("O que falta nos cadastros:");
            foreach (var p in r.Pendencias) ResultadoNotaTeste.Add("✖  " + p);
        }
        ResultadoNotaTeste.Add("O que ainda não existe no Nexus para emitir de verdade:");
        foreach (var a in r.Avisos) ResultadoNotaTeste.Add("•  " + a);
        PodeSalvarXmlConferencia = r.PodeGerarXml;
    }

    [RelayCommand]
    private void SalvarXmlConferencia()
    {
        if (_ultimaConferencia?.Xml is null) return;
        try
        {
            var caminho = EscolherCaminhoXmlConferencia(ConferenciaNfce.NomeArquivo(_ultimaConferencia));
            if (string.IsNullOrWhiteSpace(caminho)) return;
            System.IO.File.WriteAllText(caminho, _ultimaConferencia.Xml, new System.Text.UTF8Encoding(false));
            MensagemFiscalEhErro = false;
            MensagemFiscal = "XML de conferência salvo (SEM VALIDADE FISCAL).";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MensagemFiscalEhErro = true;
            MensagemFiscal = "Não consegui salvar o arquivo: " + ex.Message;
        }
    }

    public void UsarFiscal()
    {
        if (_seguranca is null) return;
        var c = _seguranca.Carregar();
        RegimeFiscal = c.FiscalRegime is not null && PreparoFiscal.Regimes.TryGetValue(c.FiscalRegime, out var r) ? r : null;
        AmbienteFiscal = c.FiscalAmbiente == PreparoFiscal.Producao ? "Produção" : "Homologação (teste)";
        SerieFiscal = c.FiscalSerie.ToString();
        CscIdFiscal = c.FiscalCscId ?? "";
        NcmPadraoFiscal = c.FiscalNcmPadrao ?? "";
        CfopPadraoFiscal = c.FiscalCfopPadrao ?? "";
        AtualizarChecklistFiscal();
    }

    partial void OnRegimeFiscalChanged(string? value) => AtualizarChecklistFiscal();
    partial void OnSerieFiscalChanged(string value) => AtualizarChecklistFiscal();
    partial void OnCscIdFiscalChanged(string value) => AtualizarChecklistFiscal();

    private static string? ChaveRegime(string? nome) =>
        PreparoFiscal.Regimes.Where(p => p.Value == nome).Select(p => p.Key).FirstOrDefault();

    [RelayCommand]
    public void AtualizarChecklistFiscal()
    {
        ChecklistFiscal.Clear();
        int.TryParse(SerieFiscal, out var serie);
        foreach (var i in PreparoFiscal.Verificar(Documento, InscricaoEstadual, CidadeUf, ChaveRegime(RegimeFiscal), serie, CscIdFiscal))
            ChecklistFiscal.Add((i.Ok ? "✔  " : "✖  ") + i.Texto);
    }

    [RelayCommand]
    private void SalvarFiscal() => SalvarRegras(
        c =>
        {
            if (!int.TryParse(SerieFiscal, out var serie)) throw new RegraDeNegocioException("A série da nota deve ser um número.");
            c.FiscalRegime = ChaveRegime(RegimeFiscal);
            c.FiscalAmbiente = AmbienteFiscal == "Produção" ? PreparoFiscal.Producao : PreparoFiscal.Homologacao;
            c.FiscalSerie = serie;
            c.FiscalCscId = string.IsNullOrWhiteSpace(CscIdFiscal) ? null : CscIdFiscal.Trim();
            c.FiscalNcmPadrao = string.IsNullOrWhiteSpace(NcmPadraoFiscal) ? null : NcmPadraoFiscal.Trim();
            c.FiscalCfopPadrao = string.IsNullOrWhiteSpace(CfopPadraoFiscal) ? null : CfopPadraoFiscal.Trim();
        },
        (m, erro) => { MensagemFiscalEhErro = erro; MensagemFiscal = m; },
        () => "Salvo. Ainda não há emissão de nota: isto deixa o cadastro pronto para quando ela existir.");
}
