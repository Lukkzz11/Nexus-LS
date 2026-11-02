using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Licença do programa: estado no menu lateral e ativação por arquivo. O uso não é bloqueado enquanto <see cref="LicencaService.Obrigatoria"/> for falso.</summary>
public partial class MainViewModel
{
    private LicencaService _licenca = new(LicencaService.CaminhoPadrao());

    /// <summary>A janela define como escolher o arquivo de licença (nulo = cancelou).</summary>
    public Func<string?> EscolherArquivoDeLicenca { get; set; } = () => null;

    [ObservableProperty] private string _licencaTexto = "";
    [ObservableProperty] private string _licencaMensagem = "";

    public string CodigoDestaMaquina => _licenca.CodigoDestaMaquina;

    /// <summary>Troca o serviço (testes usam uma pasta temporária) e relê o estado.</summary>
    public void UsarLicenca(LicencaService licenca)
    {
        _licenca = licenca;
        AtualizarLicenca();
    }

    public void AtualizarLicenca() => LicencaTexto = _licenca.Avaliar().Mensagem;

    [RelayCommand]
    private void AtivarLicenca()
    {
        var arquivo = EscolherArquivoDeLicenca();
        if (arquivo is null) return;
        try
        {
            var r = _licenca.Instalar(arquivo);
            LicencaTexto = r.Mensagem;
            LicencaMensagem = "Licença ativada.";
        }
        catch (RegraDeNegocioException ex)
        {
            LicencaMensagem = ex.Message;
        }
    }
}
