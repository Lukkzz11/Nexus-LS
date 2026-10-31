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

    private readonly Operacional.ProcessadorFilaOperacional? _operacional;
    private readonly Func<bool> _operacionalPodeEnviar;
    private string? _ultimaParadaOperacional;

    /// <param name="operacional">Envio das vendas e demais entidades ao projeto operacional (opcional: sem ele, só o catálogo).</param>
    public MonitorSincronizacao(
        ProcessadorFila processador, Func<bool> configurado, Func<bool> podeEnviar,
        Operacional.ProcessadorFilaOperacional? operacional = null, Func<bool>? operacionalPodeEnviar = null)
    {
        _processador = processador;
        _configurado = configurado;
        _podeEnviar = podeEnviar;
        _operacional = operacional;
        _operacionalPodeEnviar = operacionalPodeEnviar ?? (() => false);
    }

    public async Task<int> RecuperarAoAbrirAsync() =>
        await _processador.RecuperarInterrompidosAsync() + (_operacional is null ? 0 : await _operacional.RecuperarInterrompidosAsync());

    /// <summary>Inclui na fila os produtos (e vendas) antigos, para a nuvem ficar completa sem apertar nada.</summary>
    public async Task<int> EnfileirarExistentesAsync() =>
        await _processador.EnfileirarExistentesAsync() + (_operacional is null ? 0 : await _operacional.EnfileirarExistentesAsync());

    /// <summary>Devolve à fila o que o servidor recusou, para tentar de novo.</summary>
    public async Task<int> ReenviarComErroAsync() =>
        await _processador.ReenfileirarFalhasAsync() + (_operacional is null ? 0 : await _operacional.ReenfileirarFalhasAsync());

    /// <summary>Processa as filas (as que puderem) e devolve o estado resultante.</summary>
    public async Task<EstadoSincronizacao> CicloAsync(string? operador = null, CancellationToken ct = default)
    {
        if (_podeEnviar())
        {
            var r = await _processador.ProcessarAsync(operador, ct);
            if (r.Enviados > 0) _ultimaParada = null; // esperando o reagendamento (NadaFeito) mantém o aviso de conexão
            if (r.Parada is not null && !r.Parada.StartsWith("Já existe")) _ultimaParada = r.Parada;
        }
        if (_operacional is not null && _operacionalPodeEnviar())
        {
            var r = await _operacional.ProcessarAsync(ct);
            if (r.Enviados > 0) _ultimaParadaOperacional = null;
            if (r.Parada is not null && !r.Parada.StartsWith("Já existe")) _ultimaParadaOperacional = r.Parada;
        }
        return await LerEstadoAsync();
    }

    private static ResumoFila Soma(ResumoFila a, ResumoFila b) =>
        new(a.Pendentes + b.Pendentes, a.Enviando + b.Enviando, a.Enviados + b.Enviados, a.Falhos + b.Falhos);

    public async Task<EstadoSincronizacao> LerEstadoAsync()
    {
        var zero = new ResumoFila(0, 0, 0, 0);
        var catalogo = await _processador.ResumoAsync();
        var operacional = _operacional is null ? zero : await _operacional.ResumoAsync();
        var catalogoPode = _podeEnviar();
        var operacionalPode = _operacional is not null && _operacionalPodeEnviar();

        // Conta só o que dá para enviar agora; sem nenhum destino pronto, mostra tudo o que está esperando.
        var f = catalogoPode || operacionalPode
            ? Soma(catalogoPode ? catalogo : zero, operacionalPode ? operacional : zero)
            : Soma(catalogo, operacional);
        var aguardando = f.Pendentes + f.Enviando;
        var ultimaParada = _ultimaParada ?? _ultimaParadaOperacional;

        if (!_configurado() && !catalogoPode && !operacionalPode)
            return new(NivelSincronizacao.NaoConfigurado, "Catálogo não configurado", "Configure o Firebase na tela Catálogo online.", f);
        if (!catalogoPode && !operacionalPode)
            return new(NivelSincronizacao.SemLogin, aguardando > 0 ? $"Sem login — {aguardando} aguardando" : "Sem login",
                "Entre com seu usuário para enviar as alterações à nuvem.", f);
        if (f.Falhos > 0)
            return new(NivelSincronizacao.Atencao, $"{f.Falhos} com erro" + (aguardando > 0 ? $", {aguardando} aguardando" : ""),
                "O servidor recusou estes registros. Corrija o cadastro ou veja o histórico.", f);
        if (aguardando > 0 && ultimaParada is not null)
            return new(NivelSincronizacao.SemConexao, $"Sem conexão — {aguardando} aguardando", ultimaParada, f);
        if (aguardando > 0)
            return new(NivelSincronizacao.Pendente, $"{aguardando} aguardando envio", null, f);
        return new(NivelSincronizacao.EmDia, operacionalPode ? "Nuvem em dia" : "Catálogo em dia", null, f);
    }
}