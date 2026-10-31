using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Sincronizacao.Operacional;

/// <summary>
/// O destino operacional que o programa usa: a cada chamada confere a configuração salva e o usuário logado, e monta o
/// destino do Firestore (cacheado enquanto nada mudar). Sem configuração ou sem usuário, avisa em português e a fila
/// espera: nada se perde.
/// </summary>
public sealed class DestinoOperacionalConfiguravel : IDestinoOperacional
{
    private readonly ConfiguracaoOperacionalStore _config;
    private readonly GestorLogin _login;
    private readonly HttpClient _http;
    private readonly object _trava = new();

    private string? _assinatura;
    private FirestoreOperacionalDestino? _atual;

    public DestinoOperacionalConfiguravel(ConfiguracaoOperacionalStore config, GestorLogin login, HttpClient http)
    {
        _config = config;
        _login = login;
        _http = http;
    }

    /// <summary>Dá para enviar agora: projeto operacional configurado e usuário logado.</summary>
    public bool PodeEnviar => _config.Carregar().EstaCompleta && _login.Atual is not null;

    public Task VerificarAsync(CancellationToken ct = default) => Destino().VerificarAsync(ct);

    public Task EnviarLoteAsync(IReadOnlyList<DocumentoOperacional> lote, CancellationToken ct = default) =>
        Destino().EnviarLoteAsync(lote, ct);

    private FirestoreOperacionalDestino Destino()
    {
        var cfg = _config.Carregar();
        if (!cfg.EstaCompleta)
            throw new FalhaDestinoException("O Firebase operacional ainda não foi configurado (projeto, chave da API e nome da loja).");
        if (_login.Atual is null)
            throw new FalhaDestinoException("Entre com seu usuário para enviar as vendas à nuvem.");

        var assinatura = $"{cfg.ProjectId}|{cfg.LojaId}";
        lock (_trava)
        {
            if (_atual is null || assinatura != _assinatura)
            {
                _atual = new FirestoreOperacionalDestino(_http, cfg.ProjectId.Trim(), cfg.LojaId.Trim(), _login);
                _assinatura = assinatura;
            }
            return _atual;
        }
    }
}
