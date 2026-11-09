using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>
/// Manda ao ERP, sozinho, o que mudou no banco do Nexus. Roda a cada tick do relógio de quem o usa: se nada foi gravado desde o
/// último envio, não faz nada; senão chama o <see cref="EnviadorErp"/>, que só envia o que realmente mudou (compara o conteúdo).
/// Só age com "Permitir gravar no ERP" ligado, com a conexão preenchida e com a sessão do ERP aberta; sem internet, tenta no próximo tick.
/// Cada instalação usa a conexão da própria empresa (erp.json), então o Firebase de uma loja nunca recebe dados de outra.
/// </summary>
public sealed class EnvioErpAutomatico
{
    private readonly Func<ConfiguracaoErp> _config;
    private readonly Func<CancellationToken, Task<EnviadorErp?>> _criarEnviador;
    private readonly Func<long> _alteracoes;
    private readonly SemaphoreSlim _trava = new(1, 1);
    private long _enviadoAte = -1;

    public EnvioErpAutomatico(Func<ConfiguracaoErp> config, Func<CancellationToken, Task<EnviadorErp?>> criarEnviador, Func<long> alteracoes)
    {
        _config = config;
        _criarEnviador = criarEnviador;
        _alteracoes = alteracoes;
    }

    /// <summary>A última mensagem do envio automático (para mostrar na tela de conexão). Nula antes do primeiro envio.</summary>
    public string? Ultima { get; private set; }

    /// <summary>Envia agora se houver o que enviar. Devolve o resultado, ou nulo se não havia nada a fazer ou não foi possível.</summary>
    public async Task<ResultadoEnvioErp?> TentarAsync(CancellationToken ct = default)
    {
        if (!await _trava.WaitAsync(0, ct)) return null; // já há um envio em andamento
        try
        {
            var marca = _alteracoes();
            if (marca == _enviadoAte) return null;
            var cfg = _config();
            if (!cfg.GravacaoAtivada || !cfg.EstaCompleta) return null;

            var enviador = await _criarEnviador(ct);
            if (enviador is null) { Ultima = "Envio automático ao ERP parado: entre na conta do ERP na Conexão com a nuvem."; return null; }

            var r = await enviador.EnviarAsync(ct);
            Ultima = r.Resumo;
            if (r.Falhas.Count == 0) _enviadoAte = marca; // com falhas, o próximo tick tenta de novo
            return r;
        }
        catch (Exception ex) when (ex is RegraDeNegocioException or FalhaDestinoException or Sincronizacao.Firebase.FalhaLoginException)
        {
            Ultima = "Envio automático ao ERP: " + ex.Message;
            return null;
        }
        finally { _trava.Release(); }
    }
}
