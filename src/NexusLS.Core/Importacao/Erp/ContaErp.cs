using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>
/// Login do usuário no Firebase do ERP (o mesmo e-mail e senha que ele usa no ERP). É um login separado do login do
/// Nexus: sessão em arquivo próprio, token só para LER o ERP. A senha nunca é gravada.
/// </summary>
public sealed class ContaErp
{
    private readonly ConfiguracaoErpStore _config;
    private readonly HttpClient _http;
    private readonly IProtetorSegredo _protetor;
    private readonly string _caminhoSessao;
    private ServicoLogin? _login;
    private string? _chaveUsada;

    public ContaErp(ConfiguracaoErpStore config, HttpClient http, IProtetorSegredo protetor, string? caminhoSessao = null)
    {
        _config = config;
        _http = http;
        _protetor = protetor;
        _caminhoSessao = caminhoSessao ?? CaminhoSessaoPadrao();
    }

    public static string CaminhoSessaoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "sessao_erp.json");

    public bool Configurada => _config.Carregar().EstaCompleta;
    public bool Conectada => _login?.Atual is not null;
    public string? Email => _login?.Atual?.Email ?? new SessaoStore(_caminhoSessao).Carregar()?.Email;

    private ServicoLogin Login()
    {
        var c = _config.Carregar();
        if (!c.EstaCompleta) throw new RegraDeNegocioException("Configure o projeto do ERP antes de entrar.");
        if (_login is null || _chaveUsada != c.ApiKey)
        {
            _login = new ServicoLogin(_http, c.ApiKey, new SessaoStore(_caminhoSessao), _protetor);
            _chaveUsada = c.ApiKey;
        }
        return _login;
    }

    public Task<SessaoUsuario> EntrarAsync(string email, string senha, CancellationToken ct = default) => Login().EntrarAsync(email, senha, ct);

    /// <summary>Retoma a sessão salva (sem pedir senha). Falso = precisa entrar de novo.</summary>
    public async Task<bool> RestaurarAsync(CancellationToken ct = default)
    {
        if (!Configurada || Conectada) return Conectada;
        try { return await Login().RestaurarAsync(ct) is not null; }
        catch (FalhaLoginException) { return false; }
    }

    public void Sair()
    {
        _login?.Sair();
        new SessaoStore(_caminhoSessao).Limpar();
    }

    public IFonteErp CriarFonte() => new FirestoreErpFonte(_http, _config.Carregar(), Login());

    /// <summary>
    /// "Testar conexão": entra com o e-mail e a senha digitados (a senha não é guardada) e confirma a LEITURA lendo
    /// no máximo um produto da empresa. Não grava nada no ERP. Falhas de login, de permissão e de rede viram mensagens
    /// em português (<see cref="FalhaLoginException"/> ou <see cref="FalhaDestinoException"/>).
    /// </summary>
    public async Task<ResultadoTesteErp> TestarConexaoAsync(string email, string senha, CancellationToken ct = default)
    {
        var cfg = _config.Carregar();
        if (!cfg.EstaCompleta) throw new RegraDeNegocioException("Informe o projeto, a empresa e a chave da API do ERP e salve antes de testar.");
        var sessao = await EntrarAsync(email, senha, ct);
        var fonte = new FirestoreErpFonte(_http, cfg, Login());
        var lidos = await fonte.LerAmostraAsync("produtos", 1, ct);
        var msg = lidos > 0
            ? $"Conexão confirmada: {sessao.Email} consegue ler os produtos de {cfg.EmpresaId} no projeto {cfg.ProjectId}."
            : $"Login confirmado ({sessao.Email}) e a leitura de {cfg.EmpresaId} foi aceita, mas ainda não há produtos lá.";
        return new ResultadoTesteErp(sessao.Email, cfg.ProjectId, cfg.EmpresaId, lidos, msg);
    }
}

/// <summary>O que "Testar conexão" descobriu. Nunca inclui chave, senha nem token.</summary>
public sealed record ResultadoTesteErp(string Email, string ProjectId, string EmpresaId, int ProdutosLidos, string Mensagem);
