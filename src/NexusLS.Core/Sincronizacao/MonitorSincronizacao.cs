namespace NexusLS.Core.Sincronizacao;

public enum NivelSincronizacao
{
    /// <summary>Sem projeto Firebase configurado: a fila só acumula.</summary>
    NaoConfigurado = 1,

    /// <summary>Configurado, mas ninguém entrou (e a licença antiga não está completa): nada é enviado.</summary>
    SemLogin = 2,

    /// <summary>Fila vazia: o catálogo está em dia.</summary>
    EmDia = 3,

    /// <summary>Há itens esperando (o próximo ciclo envia).</summary>
    Pendente = 4,

    /// <summary>Não deu para falar com o Firebase; os itens continuam salvos e serão reenviados.</summary>
    SemConexao = 5,

    /// <summary>Há itens que o servidor recusou. Precisa de atenção.</summary>
    Atencao = 6
}

public record EstadoSincronizacao(NivelSincronizacao Nivel, string Texto, string? Detalhe, ResumoFila Fila);

/// <summary>
/// Roda um ciclo da fila e resume a situação em uma frase para a barra do programa. Não decide quando rodar (isso é do
/// relógio da tela) e nunca lança por causa de rede.
/// </summary>
public class MonitorSincronizacao
{
    private readonly ProcessadorFila _processador;
    private readonly Func<bool> _configurado;
    private readonly Func<bool> _podeEnviar;
    private string? _ultimaParada;

    public MonitorSincronizacao(ProcessadorFila processador, Func<bool> configurado, Func<bool> podeEnviar)
    {
        _processador = processador;
        _configurado = configurado;
        _podeEnviar = podeEnviar;
    }

    public Task<int> RecuperarAoAbrirAsync() => _processador.RecuperarInterrompidosAsync();

    /// <summary>Processa a fila (se der) e devolve o estado resultante.</summary>
    public async Task<EstadoSincronizacao> CicloAsync(string? operador = null, CancellationToken ct = default)
    {
        if (_podeEnviar())
        {
            var r = await _processador.ProcessarAsync(operador, ct);
            if (r.Enviados > 0) _ultimaParada = null; // esperando o reagendamento (NadaFeito) mantém o aviso de conexão
            if (r.Parada is not null && !r.Parada.StartsWith("Já existe")) _ultimaParada = r.Parada;
        }
        return await LerEstadoAsync();
    }

    public async Task<EstadoSincronizacao> LerEstadoAsync()
    {
        var f = await _processador.ResumoAsync();
        var aguardando = f.Pendentes + f.Enviando;

        if (!_configurado() && !_podeEnviar())
            return new(NivelSincronizacao.NaoConfigurado, "Catálogo não configurado", "Configure o Firebase na tela Catálogo online.", f);
        if (!_podeEnviar())
            return new(NivelSincronizacao.SemLogin, aguardando > 0 ? $"Sem login — {aguardando} aguardando" : "Sem login",
                "Entre com seu usuário para enviar as alterações ao catálogo.", f);
        if (f.Falhos > 0)
            return new(NivelSincronizacao.Atencao, $"{f.Falhos} com erro" + (aguardando > 0 ? $", {aguardando} aguardando" : ""),
                "O servidor recusou estes produtos. Corrija o cadastro ou veja o histórico do catálogo.", f);
        if (aguardando > 0 && _ultimaParada is not null)
            return new(NivelSincronizacao.SemConexao, $"Sem conexão — {aguardando} aguardando", _ultimaParada, f);
        if (aguardando > 0)
            return new(NivelSincronizacao.Pendente, $"{aguardando} aguardando envio", null, f);
        return new(NivelSincronizacao.EmDia, "Catálogo em dia", null, f);
    }
}
