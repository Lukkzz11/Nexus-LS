using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel
{
    private DispatcherTimer? _vigiaDoBanco;
    private DateTime? _gravacaoNaCarga;

    /// <summary>O Nexus gravou no banco depois da última carga: o catálogo e o estoque na tela podem estar velhos.</summary>
    [ObservableProperty]
    private bool _dadosDesatualizados;

    /// <summary>"Dados de 14:32" (hora da última carga), para o operador saber o quão fresco está o que vê.</summary>
    [ObservableProperty]
    private string _frescorTexto = "";

    /// <summary>Chamado depois de cada carga bem-sucedida: guarda o "carimbo" do banco e zera o aviso.</summary>
    private void MarcarDadosCarregados()
    {
        _gravacaoNaCarga = _dbService.UltimaGravacaoUtc();
        DadosDesatualizados = false;
        FrescorTexto = $"Dados de {DateTime.Now:HH:mm}";
    }

    /// <summary>
    /// Confere a cada 15 s se o banco foi gravado por outro programa (o Nexus). Só avisa: recarregar sozinho
    /// zeraria o tamanho e a quantidade que o operador está escolhendo nas linhas do catálogo.
    /// </summary>
    private void IniciarVigiaDoBanco()
    {
        if (_vigiaDoBanco is not null) return;

        _vigiaDoBanco = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _vigiaDoBanco.Tick += (_, _) => VerificarSeBancoMudou();
        _vigiaDoBanco.Start();
    }

    private void VerificarSeBancoMudou()
    {
        if (EstaCarregando || !BancoConectado) return;

        var agora = _dbService.UltimaGravacaoUtc();
        if (agora is not null && _gravacaoNaCarga is not null && agora > _gravacaoNaCarga)
            DadosDesatualizados = true;
    }
}
