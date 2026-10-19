using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Sincronizacao;

/// <summary>Andamento do envio, para a barra de progresso da tela.</summary>
public record ProgressoSincronizacao(int Processados, int Total, string Etapa);

/// <summary>O resultado do preparo: o plano para o usuário confirmar, ou o motivo de não ter como sincronizar.</summary>
public record PreparoSincronizacao(PlanoSincronizacao? Plano, string? Erro);

/// <summary>O que aconteceu, em detalhe, para a tela mostrar depois do envio.</summary>
public record ResultadoSincronizacaoCatalogo(
    SincronizacaoLog Log,
    IReadOnlyList<ProblemaProduto> Invalidos,
    IReadOnlyList<string> FalhasPorProduto);

/// <summary>
/// O "Atualizar Firestore": confere a conexão, calcula o que mudou, e envia em lotes, anotando no banco só o que
/// o servidor confirmou. Regras que valem sempre:
/// <list type="bullet">
/// <item>Nunca apaga nada no destino: produto desativado é publicado como inativo.</item>
/// <item>Nunca sobrescreve dado de venda ou de cliente (nem sai desta máquina): só o catálogo do plano.</item>
/// <item>Reenviar é seguro: o id do documento é o Guid do produto, então nunca duplica.</item>
/// <item>Uma sincronização por vez.</item>
/// <item>Falha no meio não perde o que já foi confirmado, e a próxima continua de onde parou.</item>
/// <item>Todo clique vira uma linha no log, com sucesso ou falha.</item>
/// </list>
/// </summary>
public class SincronizadorCatalogo
{
    /// <summary>O Firestore aceita até 500 operações por lote; usa-se menos para sobrar folga.</summary>
    public const int TamanhoPadraoDoLote = 400;

    private readonly SincronizacaoRepositorio _repo;
    private readonly IDestinoCatalogo _destino;
    private readonly Func<DateTime> _agora;
    private readonly int _tamanhoLote;
    private readonly SemaphoreSlim _trava = new(1, 1);

    public SincronizadorCatalogo(
        SincronizacaoRepositorio repo, IDestinoCatalogo destino, Func<DateTime>? agora = null, int tamanhoLote = TamanhoPadraoDoLote)
    {
        if (tamanhoLote < 1 || tamanhoLote > 500)
            throw new ArgumentOutOfRangeException(nameof(tamanhoLote), "O lote deve ter de 1 a 500 operações.");
        _repo = repo;
        _destino = destino;
        _agora = agora ?? (() => DateTime.Now);
        _tamanhoLote = tamanhoLote;
    }

    public bool EmAndamento => _trava.CurrentCount == 0;

    /// <summary>
    /// Primeiro passo do clique: confere a conexão e o login e calcula o plano. Não envia nada.
    /// Se não houver como sincronizar (sem internet, login recusado), devolve o motivo e registra a tentativa no log.
    /// </summary>
    public async Task<PreparoSincronizacao> PrepararAsync(string? operador = null, CancellationToken ct = default)
    {
        var inicio = _agora();
        try
        {
            await _destino.VerificarAsync(ct);
        }
        catch (FalhaDestinoException ex)
        {
            await _repo.GravarLogAsync(new SincronizacaoLog
            {
                IniciadaEm = inicio, ConcluidaEm = _agora(), Operador = operador,
                Resultado = ResultadoSincronizacao.Falhou, Mensagem = Cortar(ex.Message), Erros = 1
            });
            return new PreparoSincronizacao(null, ex.Message);
        }
        return new PreparoSincronizacao(await _repo.PlanejarAsync(), null);
    }

    /// <summary>
    /// Segundo passo (depois do usuário confirmar o plano): envia em lotes. Nunca lança erro de rede:
    /// o que der errado vira o resultado do log, que a tela mostra.
    /// </summary>
    public async Task<ResultadoSincronizacaoCatalogo> ExecutarAsync(
        PlanoSincronizacao plano, string? operador = null, IProgress<ProgressoSincronizacao>? progresso = null,
        CancellationToken ct = default)
    {
        if (!await _trava.WaitAsync(0, ct))
            throw new RegraDeNegocioException("Já existe uma sincronização em andamento. Aguarde terminar.");

        var log = new SincronizacaoLog { IniciadaEm = _agora(), Operador = operador, Inalterados = plano.Inalterados, Ignorados = plano.Invalidos.Count };
        var falhas = new List<string>();
        string? motivoDaParada = null;
        try
        {
            var operacoes = plano.Novos.Select(i => new OperacaoCatalogo(i, TipoOperacao.Gravar))
                .Concat(plano.Alterados.Select(i => new OperacaoCatalogo(i, TipoOperacao.Gravar)))
                .Concat(plano.Desativar.Select(i => new OperacaoCatalogo(i, TipoOperacao.Desativar)))
                .ToList();
            var novos = plano.Novos.Select(i => i.Produto.Guid).ToHashSet();

            var processados = 0;
            progresso?.Report(new ProgressoSincronizacao(0, operacoes.Count, "Enviando..."));

            foreach (var lote in operacoes.Chunk(_tamanhoLote))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await _destino.EnviarLoteAsync(lote, ct);
                    await Confirmar(lote, novos, log);
                }
                catch (FalhaDestinoException ex) when (ex.Gravidade == GravidadeFalha.DadosRecusados)
                {
                    // O servidor não aceitou o lote (nada foi gravado): tenta um por um para achar o produto problemático.
                    foreach (var op in lote)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            await _destino.EnviarLoteAsync(new[] { op }, ct);
                            await Confirmar(new[] { op }, novos, log);
                        }
                        catch (FalhaDestinoException ex2) when (ex2.Gravidade == GravidadeFalha.DadosRecusados)
                        {
                            log.Erros++;
                            falhas.Add($"{op.Item.Produto.Nome}: {ex2.Message}");
                        }
                    }
                }
                processados += lote.Length;
                progresso?.Report(new ProgressoSincronizacao(processados, operacoes.Count, "Enviando..."));
            }

            var enviados = log.Criados + log.Alterados + log.Desativados;
            if (enviados > 0)
            {
                try
                {
                    await _destino.RegistrarAtualizacaoAsync(_agora(), plano.Novos.Count + plano.Alterados.Count + plano.Inalterados, ct);
                }
                catch (FalhaDestinoException ex)
                {
                    falhas.Add("Não foi possível anotar a data da atualização: " + ex.Message); // o catálogo em si foi enviado
                }
            }
        }
        catch (FalhaDestinoException ex)
        {
            motivoDaParada = ex.Message; // Interrompe: o que foi confirmado antes continua valendo
        }
        catch (OperationCanceledException)
        {
            motivoDaParada = "Cancelada pelo usuário.";
        }
        catch (Exception ex)
        {
            // Qualquer outro problema (por exemplo, o banco local) também vira resultado no log, nunca um programa fechando.
            motivoDaParada = "Erro inesperado: " + ex.Message;
        }
        finally
        {
            try
            {
                var total = plano.TotalAEnviar;
                var feitos = log.Criados + log.Alterados + log.Desativados;
                if (motivoDaParada is not null) log.Erros += Math.Max(1, total - feitos - log.Erros);

                log.Resultado = ResolverResultado(total, feitos, log.Erros, plano.Invalidos.Count);
                log.Mensagem = Cortar(MontarMensagem(log, motivoDaParada, falhas));
                log.ConcluidaEm = _agora();
                await _repo.GravarLogAsync(log);
            }
            finally
            {
                _trava.Release();
            }
        }

        return new ResultadoSincronizacaoCatalogo(log, plano.Invalidos, falhas);
    }

    private async Task Confirmar(IReadOnlyList<OperacaoCatalogo> lote, HashSet<Guid> novos, SincronizacaoLog log)
    {
        // O servidor confirmou: só agora anota no banco local (se o programa fechasse aqui, o pior caso é reenviar, e reenviar é seguro).
        await _repo.RegistrarEnviadosAsync(lote.Select(o => o.Item));
        foreach (var op in lote)
        {
            if (op.Tipo == TipoOperacao.Desativar) log.Desativados++;
            else if (novos.Contains(op.Item.Produto.Guid)) log.Criados++;
            else log.Alterados++;
        }
    }

    private static ResultadoSincronizacao ResolverResultado(int totalAEnviar, int enviados, int erros, int invalidos)
    {
        if (totalAEnviar == 0) return invalidos > 0 ? ResultadoSincronizacao.Parcial : ResultadoSincronizacao.SemMudancas;
        if (enviados == 0) return ResultadoSincronizacao.Falhou;
        return erros > 0 || invalidos > 0 ? ResultadoSincronizacao.Parcial : ResultadoSincronizacao.Sucesso;
    }

    private static string MontarMensagem(SincronizacaoLog log, string? motivoDaParada, List<string> falhas)
    {
        var partes = new List<string>();
        var enviados = log.Criados + log.Alterados + log.Desativados;
        if (log.Resultado == ResultadoSincronizacao.SemMudancas)
            partes.Add("Nada a enviar: o catálogo já está atualizado.");
        else if (enviados > 0)
            partes.Add($"{enviados} produto(s) enviado(s): {log.Criados} novo(s), {log.Alterados} alterado(s), {log.Desativados} desativado(s).");
        if (log.Inalterados > 0 && enviados > 0) partes.Add($"{log.Inalterados} sem mudança.");
        if (log.Ignorados > 0) partes.Add($"{log.Ignorados} ignorado(s) por dados inválidos.");
        if (motivoDaParada is not null) partes.Add("Interrompida: " + motivoDaParada);
        if (falhas.Count > 0) partes.Add($"{falhas.Count} com erro: {falhas[0]}" + (falhas.Count > 1 ? " (e outros)" : ""));
        return string.Join(" ", partes);
    }

    private static string Cortar(string texto) => texto.Length <= 1000 ? texto : texto[..997] + "...";
}
