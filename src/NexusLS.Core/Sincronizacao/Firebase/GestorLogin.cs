namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>
/// O login do usuário que o programa usa. Monta o <see cref="ServicoLogin"/> com a chave da API da configuração salva
/// (se a chave mudar, recomeça) e entrega o token ao envio. Quem chama não precisa saber de projeto, chave nem arquivo.
/// </summary>
public sealed class GestorLogin : IProvedorToken
{
    private readonly ConfiguracaoFirebaseStore _config;
    private readonly HttpClient _http;
    private readonly SessaoStore _sessaoStore;
    private readonly IProtetorSegredo _protetor;
    private readonly Func<DateTime>? _agora;
    private readonly object _trava = new();

    private ServicoLogin? _servico;
    private string? _chave;

    public GestorLogin(ConfiguracaoFirebaseStore config, HttpClient http, SessaoStore sessaoStore, IProtetorSegredo protetor, Func<DateTime>? agora = null)
    {
        _config = config;
        _http = http;
        _sessaoStore = sessaoStore;
        _protetor = protetor;
        _agora = agora;
    }

    /// <summary>Há projeto e chave de API para o login funcionar.</summary>
    public bool Configurado
    {
        get
        {
            var c = _config.Carregar();
            return !string.IsNullOrWhiteSpace(c.ProjectId) && !string.IsNullOrWhiteSpace(c.ApiKey);
        }
    }

    /// <summary>E-mail da última configuração, para já vir preenchido na tela de login.</summary>
    public string EmailSugerido => _config.Carregar().Email;

    public SessaoUsuario? Atual => _servico?.Atual;
    public bool Offline => _servico?.Offline ?? false;
    public string? Uid => Atual?.Uid;

    public Task<SessaoUsuario?> RestaurarAsync(CancellationToken ct = default) =>
        Configurado ? Servico().RestaurarAsync(ct) : Task.FromResult<SessaoUsuario?>(null);

    public Task<SessaoUsuario> EntrarAsync(string email, string senha, CancellationToken ct = default)
    {
        if (!Configurado)
            throw new FalhaLoginException("O Firebase ainda não foi configurado. Preencha o projeto e a chave da API na tela Catálogo online.", TipoFalhaLogin.Servidor);
        return Servico().EntrarAsync(email, senha, ct);
    }

    public void Sair() => _servico?.Sair();

    public Task<string> ObterAsync(CancellationToken ct = default) =>
        _servico is null ? throw new FalhaDestinoException("Entre com seu usuário para sincronizar com o Firebase.") : _servico.ObterAsync(ct);

    public void Invalidar() => _servico?.Invalidar();

    private ServicoLogin Servico()
    {
        var chave = _config.Carregar().ApiKey.Trim();
        lock (_trava)
        {
            if (_servico is null || chave != _chave)
            {
                _servico = new ServicoLogin(_http, chave, _sessaoStore, _protetor, _agora);
                _chave = chave;
            }
            return _servico;
        }
    }
}

/// <summary>
/// Destino da fila: com usuário logado, grava com o token DELE; sem usuário, usa o destino antigo (login da licença),
/// para quem já configurou o catálogo assim continuar funcionando.
/// </summary>
public sealed class DestinoFilaFirestore : IDestinoCatalogo
{
    private readonly GestorLogin _login;
    private readonly ConfiguracaoFirebaseStore _config;
    private readonly HttpClient _http;
    private readonly IDestinoCatalogo _legado;
    private readonly Func<DateTime>? _agora;
    private readonly object _trava = new();

    private string? _projeto;
    private FirestoreRestDestino? _doUsuario;

    public DestinoFilaFirestore(GestorLogin login, ConfiguracaoFirebaseStore config, HttpClient http, IDestinoCatalogo legado, Func<DateTime>? agora = null)
    {
        _login = login;
        _config = config;
        _http = http;
        _legado = legado;
        _agora = agora;
    }

    /// <summary>Dá para enviar agora: há usuário logado ou a licença antiga está completa.</summary>
    public bool PodeEnviar => _login.Atual is not null || _config.Carregar().EstaCompleta;

    public Task VerificarAsync(CancellationToken ct = default) => Escolher().VerificarAsync(ct);

    public Task EnviarLoteAsync(IReadOnlyList<OperacaoCatalogo> lote, CancellationToken ct = default) =>
        Escolher().EnviarLoteAsync(lote, ct);

    public Task RegistrarAtualizacaoAsync(DateTime agora, int totalProdutosAtivos, CancellationToken ct = default) =>
        Escolher().RegistrarAtualizacaoAsync(agora, totalProdutosAtivos, ct);

    private IDestinoCatalogo Escolher()
    {
        if (_login.Atual is null) return _legado;
        var projeto = _config.Carregar().ProjectId.Trim();
        lock (_trava)
        {
            if (_doUsuario is null || projeto != _projeto)
            {
                _doUsuario = new FirestoreRestDestino(_http, projeto, _login, _agora);
                _projeto = projeto;
            }
            return _doUsuario;
        }
    }
}
