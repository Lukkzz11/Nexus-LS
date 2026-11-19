using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Sincronizacao.Nuvem;

namespace NexusLS.App.ViewModels;

/// <summary>"Receber preços da nuvem" nas Configurações (Esta loja): aplica aqui o preço que o administrador definiu para a loja deste computador.</summary>
public partial class ConfiguracoesViewModel
{
    private ServicoNuvemLoja? _nuvem;
    private Func<string, bool>? _autorizarPrecos;
    private Func<bool>? _podeEstoquePrecos;

    public bool TemNuvem => _nuvem is not null;

    [ObservableProperty] private bool _ocupadoPrecosNuvem;
    [ObservableProperty] private string _mensagemPrecosNuvem = "";
    [ObservableProperty] private bool _mensagemPrecosNuvemEhErro;

    public void UsarNuvem(ServicoNuvemLoja nuvem, Func<string, bool>? autorizar = null, Func<bool>? podeEstoque = null)
    {
        _nuvem = nuvem;
        _autorizarPrecos = autorizar;
        _podeEstoquePrecos = podeEstoque;
        OnPropertyChanged(nameof(TemNuvem));
    }

    [RelayCommand]
    private async Task ReceberPrecosNuvemAsync()
    {
        if (_nuvem is null || OcupadoPrecosNuvem) return;
        OcupadoPrecosNuvem = true;
        try
        {
            var (texto, erro) = await NuvemApp.ReceberPrecosAsync(_nuvem, _autorizarPrecos, _podeEstoquePrecos);
            MensagemPrecosNuvemEhErro = erro;
            MensagemPrecosNuvem = texto;
        }
        finally { OcupadoPrecosNuvem = false; }
    }
}
