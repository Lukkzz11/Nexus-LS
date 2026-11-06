using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Atualizacao;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Aviso e controle de atualizações do programa. Ao abrir, consulta em silêncio: sem internet ou sem novidade, ninguém é
/// incomodado. Havendo versão nova aparece um aviso com "Atualizar agora" e "Depois"; nada é instalado sem o usuário
/// escolher. A venda em andamento é salva sozinha pelo PDV e volta depois que o programa reabre.
/// </summary>
public partial class AtualizacaoViewModel : ViewModelBase
{
    private readonly AtualizadorService _servico;
    private readonly Func<string, Task> _aplicar;
    private InfoVersao? _nova;
    private CancellationTokenSource? _cancelar;

    /// <param name="aplicar">Recebe o arquivo baixado, troca o programa e reinicia. Nos testes é trocado por uma versão falsa.</param>
    public AtualizacaoViewModel(AtualizadorService servico, Func<string, Task> aplicar)
    {
        _servico = servico;
        _aplicar = aplicar;
    }

    /// <summary>A tela define as caixas de pergunta e de texto.</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;
    public Action<string, string> MostrarNotas { get; set; } = (_, _) => { };

    public string VersaoAtualTexto => $"Versão {_servico.Atual}";

    [ObservableProperty] private bool _temNovaVersao;
    [ObservableProperty] private string _avisoTexto = "";
    [ObservableProperty] private bool _verificando;
    [ObservableProperty] private bool _baixando;
    [ObservableProperty] private double _progressoPercentual;
    [ObservableProperty] private string _progressoTexto = "";

    public bool Ocupado => Verificando || Baixando;

    partial void OnVerificandoChanged(bool value) => OnPropertyChanged(nameof(Ocupado));
    partial void OnBaixandoChanged(bool value) => OnPropertyChanged(nameof(Ocupado));

    /// <summary>Chamado ao abrir o programa: não mostra nada se não houver novidade nem se a internet falhar.</summary>
    public async Task VerificarAoAbrirAsync()
    {
        try { await VerificarAsync(manual: false); }
        catch (Exception) { /* o atualizador nunca pode atrapalhar a abertura */ }
    }

    [RelayCommand]
    private Task VerificarAgoraAsync() => ExecutarAsync(() => VerificarAsync(manual: true));

    private async Task VerificarAsync(bool manual)
    {
        if (Ocupado) return;
        Verificando = true;
        try
        {
            if (manual) Mensagem = "";
            var r = await _servico.VerificarAsync();
            switch (r.Estado)
            {
                case EstadoVerificacao.Disponivel:
                    _nova = r.Nova;
                    AvisoTexto = $"Nova versão {r.Nova!.Versao} disponível (você está na {r.Atual}).";
                    TemNovaVersao = true;
                    if (manual) MostrarSucesso(r.Mensagem);
                    break;
                case EstadoVerificacao.SemNovidade:
                    TemNovaVersao = false;
                    if (manual) MostrarSucesso($"Você já está com a versão mais nova ({r.Atual}).");
                    break;
                default:
                    if (manual) MostrarErro(r.Mensagem);
                    break;
            }
        }
        finally
        {
            Verificando = false;
        }
    }

    [RelayCommand]
    private void Depois() => TemNovaVersao = false;

    [RelayCommand]
    private void VerNovidades()
    {
        if (_nova is null) return;
        var notas = string.IsNullOrWhiteSpace(_nova.Notas) ? "Sem descrição para esta versão." : _nova.Notas;
        MostrarNotas($"O que mudou na versão {_nova.Versao}", notas);
    }

    [RelayCommand]
    private Task AtualizarAgoraAsync() => ExecutarAsync(async () =>
    {
        if (_nova is null || Ocupado) return;
        if (!Confirmar($"O Nexus vai baixar a versão {_nova.Versao}, fechar e abrir de novo.\n\n" +
                       "Uma venda em andamento fica salva e volta ao abrir. Seus dados não são apagados.\n\nAtualizar agora?"))
            return;

        Baixando = true;
        ProgressoPercentual = 0;
        ProgressoTexto = "Baixando...";
        _cancelar = new CancellationTokenSource();
        string arquivo;
        try
        {
            var progresso = new Progress<double>(p =>
            {
                ProgressoPercentual = p * 100;
                ProgressoTexto = $"Baixando... {p:P0}";
            });
            arquivo = await _servico.BaixarAsync(_nova, progresso, _cancelar.Token);
        }
        catch (RegraDeAtualizacaoException e)
        {
            Baixando = false;
            MostrarErro(e.Message);
            return;
        }
        catch (OperationCanceledException)
        {
            Baixando = false;
            ProgressoTexto = "";
            MostrarErro("Download cancelado.");
            return;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            Baixando = false;
            MostrarErro("Não foi possível baixar a atualização agora. Verifique a internet e tente de novo.");
            return;
        }

        ProgressoTexto = "Instalando...";
        try
        {
            await _aplicar(arquivo); // troca o programa e reinicia; em geral não volta daqui
        }
        catch (Exception e)
        {
            Baixando = false;
            MostrarErro("Não foi possível instalar a atualização: " + e.Message + " O programa continua na versão atual.");
        }
    });

    [RelayCommand]
    private void CancelarDownload() => _cancelar?.Cancel();
}
