using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Página "Comunicação": o texto da cobrança por WhatsApp, com prévia de como o cliente vai ler.</summary>
public partial class ConfiguracoesViewModel
{
    [ObservableProperty] private string _mensagemCobrancaTexto = MensagensCliente.CobrancaPadrao;
    [ObservableProperty] private string _previaCobranca = "";
    [ObservableProperty] private string _mensagemComunicacao = "";
    [ObservableProperty] private bool _mensagemComunicacaoEhErro;

    public string CamposDisponiveis => "Campos que você pode usar: " + string.Join("  ", MensagensCliente.Campos);

    public void UsarComunicacao()
    {
        if (_seguranca is null) return;
        var c = _seguranca.Carregar();
        MensagemCobrancaTexto = string.IsNullOrWhiteSpace(c.MensagemCobranca) ? MensagensCliente.CobrancaPadrao : c.MensagemCobranca;
        AtualizarPreviaCobranca();
    }

    partial void OnMensagemCobrancaTextoChanged(string value) => AtualizarPreviaCobranca();

    private void AtualizarPreviaCobranca() => PreviaCobranca =
        MensagensCliente.Montar(MensagemCobrancaTexto, "Maria Souza", "Sua Loja", 25000, 12000, new DateTime(2026, 11, 10));

    [RelayCommand]
    private void RestaurarMensagemCobranca() => MensagemCobrancaTexto = MensagensCliente.CobrancaPadrao;

    [RelayCommand]
    private void SalvarComunicacao() => SalvarRegras(
        c => c.MensagemCobranca = string.IsNullOrWhiteSpace(MensagemCobrancaTexto) || MensagemCobrancaTexto.Trim() == MensagensCliente.CobrancaPadrao
            ? null : MensagemCobrancaTexto.Trim(),
        (m, erro) => { MensagemComunicacaoEhErro = erro; MensagemComunicacao = m; },
        () => "Salvo. O botão de cobrança na ficha do cliente já usa esse texto.");
}
