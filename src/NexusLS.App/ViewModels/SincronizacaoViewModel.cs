using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Indicador da fila de sincronização no menu lateral e o relógio que a esvazia: ao abrir (recupera o que uma queda
/// deixou pela metade) e a cada poucos minutos. Nunca incomoda a venda: erro de rede só muda o texto do indicador.
/// </summary>
public partial class SincronizacaoViewModel : ViewModelBase
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(3);

    private readonly MonitorSincronizacao _monitor;
    private readonly GestorLogin _login;
    private readonly Func<string?> _operador;
    private System.Windows.Threading.DispatcherTimer? _relogio;
    private bool _iniciado;
    private bool _recusouLogin;

    public SincronizacaoViewModel(MonitorSincronizacao monitor, GestorLogin login, Func<string?>? operador = null)
    {
        _monitor = monitor;
        _login = login;
        _operador = operador ?? (() => null);
    }

    /// <summary>A janela define como mostrar a tela de login (devolve a sessão ou nulo se a pessoa desistiu).</summary>
    public Func<SessaoUsuario?> PedirLogin { get; set; } = () => null;

    [ObservableProperty] private string _texto = "Catálogo";
    [ObservableProperty] private string? _detalhe;
    [ObservableProperty] private NivelSincronizacao _nivel = NivelSincronizacao.NaoConfigurado;
    [ObservableProperty] private string _usuarioTexto = "";
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private bool _podeEntrar;
    [ObservableProperty] private bool _temSessao;
    [ObservableProperty] private bool _temErros;

    /// <summary>Chamado ao abrir a janela principal. Restaura a sessão, recupera a fila e liga o relógio.</summary>
    public async Task IniciarAsync()
    {
        if (_iniciado) return;
        _iniciado = true;
        try
        {
            try
            {
                await _login.RestaurarAsync();
            }
            catch (FalhaLoginException ex)
            {
                MostrarErro(ex.Message); // sessão offline vencida: pede para entrar de novo
            }
            AtualizarSessao();
            if (_login.Configurado && _login.Atual is null && !_recusouLogin) EntrarCore();

            await _monitor.RecuperarAoAbrirAsync();
            await _monitor.EnfileirarExistentesAsync();
            await CicloAsync();
        }
        catch (Exception ex)
        {
            MostrarErro("Sincronização: " + ex.Message);
        }

        _relogio = new System.Windows.Threading.DispatcherTimer { Interval = Intervalo };
        _relogio.Tick += async (_, _) => await CicloAsync();
        _relogio.Start();
    }

    [RelayCommand]
    private Task SincronizarAgoraAsync() => CicloAsync();

    [RelayCommand]
    private async Task ReenviarErrosAsync()
    {
        await _monitor.ReenviarComErroAsync();
        await CicloAsync();
    }

    [RelayCommand]
    private void Entrar() => EntrarCore();

    [RelayCommand]
    private async Task SairAsync()
    {
        _login.Sair();
        App.UsuarioUidAtual = null;
        AtualizarSessao();
        await CicloAsync();
    }

    private void EntrarCore()
    {
        var sessao = PedirLogin();
        _recusouLogin = sessao is null;
        AtualizarSessao();
        if (sessao is not null) _ = CicloAsync();
    }

    private async Task CicloAsync()
    {
        if (Ocupado) return;
        Ocupado = true;
        try
        {
            Aplicar(await Task.Run(() => _monitor.CicloAsync(_operador() ?? _login.Atual?.Email)));
        }
        catch (Exception ex)
        {
            Texto = "Erro na sincronização";
            Detalhe = ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void Aplicar(EstadoSincronizacao e)
    {
        Nivel = e.Nivel;
        TemErros = e.Fila.Falhos > 0;
        Texto = e.Texto;
        Detalhe = e.Detalhe;
    }

    private void AtualizarSessao()
    {
        App.UsuarioUidAtual = _login.Uid;
        TemSessao = _login.Atual is not null;
        PodeEntrar = _login.Configurado && _login.Atual is null;
        UsuarioTexto = _login.Atual is null ? "" : _login.Atual.Email + (_login.Offline ? " (offline)" : "");
    }
}
