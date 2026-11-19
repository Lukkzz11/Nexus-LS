using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Sincronizacao.Nuvem;

/// <summary>
/// Monta o <see cref="ClienteNuvemLoja"/> com as MESMAS credenciais do catálogo online (projeto, chave da API, e-mail e senha de
/// <see cref="ConfiguracaoFirebaseStore"/>), como faz <see cref="DestinoFirestoreConfiguravel"/>, sem alterá-lo. O código da
/// empresa vem do estado da nuvem (<see cref="CacheNuvemLoja"/>). Mantém o login na memória enquanto nada mudar.
/// </summary>
public sealed class FabricaClienteNuvemLoja
{
    private readonly ConfiguracaoFirebaseStore _firebase;
    private readonly CacheNuvemLoja _cache;
    private readonly HttpClient _http;
    private readonly object _trava = new();
    private string? _assinatura;
    private ClienteNuvemLoja? _atual;

    public FabricaClienteNuvemLoja(ConfiguracaoFirebaseStore firebase, CacheNuvemLoja cache, HttpClient http)
    {
        _firebase = firebase;
        _cache = cache;
        _http = http;
    }

    public ClienteNuvemLoja Criar()
    {
        var cfg = _firebase.Carregar();
        if (string.IsNullOrWhiteSpace(cfg.ProjectId) || string.IsNullOrWhiteSpace(cfg.ApiKey) || string.IsNullOrWhiteSpace(cfg.Email) || string.IsNullOrEmpty(cfg.SenhaProtegida))
            throw new FalhaNuvemException("O Firebase ainda não foi configurado. Preencha o projeto, a chave da API, o e-mail e a senha na tela Catálogo online.", TipoFalhaNuvem.Login);
        var empresa = _cache.Carregar().EmpresaId;
        if (string.IsNullOrWhiteSpace(empresa))
            throw new FalhaNuvemException("Informe o código da empresa na nuvem (campo \"Empresa na nuvem\" em Gestão da empresa > Lojas).", TipoFalhaNuvem.Dados);

        var assinatura = $"{cfg.ProjectId}|{cfg.ApiKey}|{cfg.Email}|{cfg.SenhaProtegida}|{empresa}";
        lock (_trava)
        {
            if (_atual is null || assinatura != _assinatura)
            {
                var auth = new FirebaseAuthRest(_http, cfg.ApiKey, cfg.Email, _firebase.ObterSenha, () => DateTime.UtcNow);
                _atual = new ClienteNuvemLoja(_http, cfg.ProjectId, empresa, auth);
                _assinatura = assinatura;
            }
            return _atual;
        }
    }
}
