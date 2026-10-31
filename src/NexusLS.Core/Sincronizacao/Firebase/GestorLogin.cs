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

    private readonly Operacional.ConfiguracaoOperacionalStore? _operacional;

    public GestorLogin(ConfiguracaoFirebaseStore config, HttpClient http, SessaoStore sessaoStore, IProtetorSegredo protetor,
        Func<DateTime>? agora = null, Operacional.ConfiguracaoOperacionalStore? operacional = null)
    {
        _config = config;
        _http = http;
        _sessaoStore = sessaoStore;
        _protetor = protetor;
        _agora = agora;
        _operacional = operacional;
    }

    /// <summary>
    /// Em qual projeto os usuários entram: no operacional (projeto B), se estiver configurado; senão no do catálogo
    /// (comportamento de quem usa um projeto só). Usuários do Firebase existem dentro de UM projeto.
    /// </summary>
    public (string ProjectId, string ApiKey) ProjetoDoLogin
    {
        get
        {
            var op = _operacional?.Carregar();
            if (op is { EstaCompleta: true }) return (op.ProjectId.Trim(), op.ApiKey.Trim());
            var c = _config.Carregar();
            return (c.ProjectId.Trim(), c.ApiKey.Trim());
        }
    }

    /// <summary>O login vale também para o catálogo (os dois são o mesmo projeto).</summary>
    public bool LoginServeAoCatalogo => string.Equals(ProjetoDoLogin.ProjectId, _config.Carregar().ProjectId.Trim(), StringComparison.Ordinal);

    /// <summary>Há projeto e chave de API para o login funcionar.</summary>
    public bool Configurado
    {
        get
        {
            var (projeto, chave) = ProjetoDoLogin;
            return !string.IsNullOrWhiteSpace(projeto) && !string.IsNullOrWhiteSpace(chave);
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
        var chave = ProjetoDoLogin.ApiKey;
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
    public bool PodeEnviar => (_login.Atual is not null && _login.LoginServeAoCatalogo) || _config.Carregar().EstaCompleta;

    public Task VerificarAsync(CancellationToken ct = default) => Escolher().VerificarAsync(ct);

    public Task EnviarLoteAsync(IReadOnlyList<OperacaoCatalogo> lote, CancellationToken ct = default) =>
        Escolher().EnviarLoteAsync(lote, ct);

    public Task RegistrarAtualizacaoAsync(DateTime agora, int totalProdutosAtivos, CancellationToken ct = default) =>
        Escolher().RegistrarAtualizacaoAsync(agora, totalProdutosAtivos, ct);

    private IDestinoCatalogo Escolher()
    {
        // O token do usuário só vale no projeto onde ele tem conta; se o login é do projeto operacional, o catálogo usa o login da licença.
        if (_login.Atual is null || !_login.LoginServeAoCatalogo) return _legado;
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
