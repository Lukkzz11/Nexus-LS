using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
