using NexusLS.Core.Services;

namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>
/// O destino que a tela usa: a cada chamada confere a configuração salva e, se ela mudou (outro projeto, outro
/// e-mail, outra senha), monta um destino novo com login novo. Assim dá para trocar de projeto sem reiniciar o
/// programa, e o token continua na memória enquanto nada muda. Sem configuração completa, avisa em português.
/// </summary>
public sealed class DestinoFirestoreConfiguravel : IDestinoCatalogo
{
    private readonly ConfiguracaoFirebaseStore _store;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _agora;
    private readonly object _trava = new();

    private string? _assinatura;
    private FirestoreRestDestino? _atual;

    public DestinoFirestoreConfiguravel(ConfiguracaoFirebaseStore store, HttpClient http, Func<DateTime>? agora = null)
    {
        _store = store;
        _http = http;
        _agora = agora ?? (() => DateTime.Now);
    }

    public Task VerificarAsync(CancellationToken ct = default) => Destino().VerificarAsync(ct);

    public Task EnviarLoteAsync(IReadOnlyList<OperacaoCatalogo> lote, CancellationToken ct = default) =>
        Destino().EnviarLoteAsync(lote, ct);

    public Task RegistrarAtualizacaoAsync(DateTime agora, int totalProdutosAtivos, CancellationToken ct = default) =>
        Destino().RegistrarAtualizacaoAsync(agora, totalProdutosAtivos, ct);

    private FirestoreRestDestino Destino()
    {
        var cfg = _store.Carregar();
        if (string.IsNullOrWhiteSpace(cfg.ProjectId) || string.IsNullOrWhiteSpace(cfg.ApiKey) || string.IsNullOrWhiteSpace(cfg.Email))
            throw new FalhaDestinoException("O Firebase ainda não foi configurado. Preencha o projeto, a chave da API, o e-mail e a senha na tela Catálogo online.");
        if (string.IsNullOrEmpty(cfg.SenhaProtegida))
            throw new FalhaDestinoException("Falta a senha do login do Firebase. Informe-a na tela Catálogo online.");

        var assinatura = $"{cfg.ProjectId}|{cfg.ApiKey}|{cfg.Email}|{cfg.SenhaProtegida}";
        lock (_trava)
        {
            if (_atual is null || assinatura != _assinatura)
            {
                var auth = new FirebaseAuthRest(_http, cfg.ApiKey, cfg.Email, _store.ObterSenha, () => DateTime.UtcNow);
                _atual = new FirestoreRestDestino(_http, cfg.ProjectId, auth, _agora);
                _assinatura = assinatura;
            }
            return _atual;
        }
    }
}
