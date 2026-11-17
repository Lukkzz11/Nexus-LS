using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Página "Segurança": bloqueio por inatividade e visibilidade do botão Gestão.</summary>
public partial class ConfiguracoesViewModel
{
    private ConfiguracaoSegurancaStore? _seguranca;

    public bool TemSeguranca => _seguranca is not null;

    [ObservableProperty] private string _minutosInatividadeTexto = "0";
    [ObservableProperty] private bool _esconderGestaoSemAcesso;
    [ObservableProperty] private string _mensagemSeguranca = "";
    [ObservableProperty] private bool _mensagemSegurancaEhErro;

    public string LimiteDePinTexto =>
        $"Depois de {FuncionarioService.MaxTentativas} PINs errados seguidos, o funcionário fica bloqueado por {(int)FuncionarioService.TempoDeBloqueio.TotalSeconds} segundos (mesmo se o programa for fechado e aberto). Cada entrada e cada erro ficam registrados em Atividades.";

    public void UsarSeguranca(ConfiguracaoSegurancaStore store)
    {
        _seguranca = store;
        var c = store.Carregar();
        MinutosInatividadeTexto = c.MinutosInatividade.ToString();
        EsconderGestaoSemAcesso = c.EsconderGestaoSemAcesso;
        OnPropertyChanged(nameof(TemSeguranca));
    }

    [RelayCommand]
    private void SalvarSeguranca()
    {
        if (_seguranca is null) return;
        try
        {
            if (!int.TryParse(MinutosInatividadeTexto.Trim(), out var minutos))
                throw new RegraDeNegocioException("Digite os minutos só com números (0 desliga o bloqueio).");
            _seguranca.Salvar(new ConfiguracaoSeguranca { MinutosInatividade = minutos, EsconderGestaoSemAcesso = EsconderGestaoSemAcesso });
            Sessao.Notificar();   // o menu confere de novo se mostra o botão Gestão
            MensagemSegurancaEhErro = false;
            MensagemSeguranca = minutos == 0 ? "Salvo. O bloqueio por inatividade está desligado." : $"Salvo. O programa pede o PIN depois de {minutos} minuto(s) parado.";
        }
        catch (RegraDeNegocioException ex)
        {
            MensagemSegurancaEhErro = true;
            MensagemSeguranca = ex.Message;
        }
    }
}
