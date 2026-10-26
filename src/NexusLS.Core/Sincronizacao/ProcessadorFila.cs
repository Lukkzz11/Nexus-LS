using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Sincronizacao;

public record ResultadoFila(int Enviados, int SemMudanca, int Recusados, int Reagendados, string? Parada)
{
    public bool NadaFeito => Enviados + SemMudanca + Recusados + Reagendados == 0 && Parada is null;
}

public record ResumoFila(int Pendentes, int Enviando, int Enviados, int Falhos);

/// <summary>
/// Esvazia a fila de saída: lê o produto ATUAL no SQLite, envia ao catálogo e só depois de o servidor confirmar marca
/// como enviado. Falha de conexão reagenda com espera crescente; dado recusado fica como "Falhou" com o motivo;
/// queda no meio do envio é desfeita por <see cref="RecuperarInterrompidosAsync"/>. Reenviar é sempre seguro porque o
/// documento remoto tem o id do produto (sobrescreve) e o hash evita reenviar o que não mudou.
/// </summary>
public class ProcessadorFila
{
    public const int TamanhoPadraoDoLote = 200;
    private static readonly TimeSpan EsperaBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(30);

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly SincronizacaoRepositorio _repo;
    private readonly IDestinoCatalogo _destino;
    private readonly Func<DateTime> _agora;
    private readonly int _tamanhoLote;
    private readonly SemaphoreSlim _trava = new(1, 1);

    public ProcessadorFila(
        IDbContextFactory<PdvDbContext> fabrica, SincronizacaoRepositorio repo, IDestinoCatalogo destino,
        Func<DateTime>? agora = null, int tamanhoLote = TamanhoPadraoDoLote)
    {
        if (tamanhoLote < 1 || tamanhoLote > 500)
            throw new ArgumentOutOfRangeException(nameof(tamanhoLote), "O lote deve ter de 1 a 500 operações.");
        _fabrica = fabrica;
        _repo = repo;
        _destino = destino;
        _agora = agora ?? (() => DateTime.Now);
        _tamanhoLote = tamanhoLote;
    }

    /// <summary>
    /// Este processador envia ao CATÁLOGO, que só recebe produtos. As demais entidades (vendas, clientes...) também entram
    /// na fila, mas ficam esperando o destino delas (Firebase operacional): não são enviadas, contadas nem tocadas aqui.
    /// </summary>
    private static readonly string[] EntidadesDoCatalogo = { NomesEntidade.Produto };

    private static IQueryable<ItemFilaSincronizacao> Fila(PdvDbContext db) =>
        db.FilaSincronizacao.Where(f => EntidadesDoCatalogo.Contains(f.Entidade));

    public static TimeSpan Espera(int tentativas)
    {
        var potencia = Math.Min(Math.Max(tentativas, 1) - 1, 10);
        var espera = TimeSpan.FromTicks(EsperaBase.Ticks * (1L << potencia));
        return espera > EsperaMaxima ? EsperaMaxima : espera;
    }

    /// <summary>Chamar ao abrir o programa: o que ficou "Enviando" por uma queda volta a Pendente (e será reenviado, sem duplicar).</summary>
    public async Task<int> RecuperarInterrompidosAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var presos = await Fila(db).Where(f => f.Estado == EstadoFila.Enviando).ToListAsync();
        foreach (var f in presos)
        {
            f.Estado = EstadoFila.Pendente;
            f.ProximaTentativa = null;
            f.AtualizadoEm = _agora();
        }
        await db.SaveChangesAsync();
        return presos.Count;
    }

    public async Task<ResumoFila> ResumoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var contagem = await Fila(db).AsNoTracking().GroupBy(f => f.Estado)
            .Select(g => new { Estado = g.Key, N = g.Count() }).ToListAsync();
        int Qtd(EstadoFila e) => contagem.FirstOrDefault(c => c.Estado == e)?.N ?? 0;
        return new ResumoFila(Qtd(EstadoFila.Pendente), Qtd(EstadoFila.Enviando), Qtd(EstadoFila.Enviado), Qtd(EstadoFila.Falhou));
    }

    /// <summary>
    /// Põe na fila os produtos que ainda não têm linha nela (os cadastrados antes da fila existir). O hash de
    /// <see cref="SincronizacaoRepositorio"/> evita reenviar o que o catálogo já tem. Seguro repetir.
    /// </summary>
    public async Task<int> EnfileirarExistentesAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var faltam = await db.Produtos.AsNoTracking()
            .Where(p => !db.FilaSincronizacao.Any(f => f.Entidade == NomesEntidade.Produto && f.EntidadeGuid == p.Guid))
            .Select(p => p.Guid).ToListAsync();
        var agora = _agora();
        foreach (var g in faltam)
            db.FilaSincronizacao.Add(new ItemFilaSincronizacao { Entidade = NomesEntidade.Produto, EntidadeGuid = g, CriadoEm = agora, AtualizadoEm = agora });
        await db.SaveChangesAsync();
        return faltam.Count;
    }

    /// <summary>Devolve à fila os itens recusados (ex.: depois de o operador corrigir o cadastro ou o servidor).</summary>
    public async Task<int> ReenfileirarFalhasAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var falhos = await Fila(db).Where(f => f.Estado == EstadoFila.Falhou).ToListAsync();
        foreach (var f in falhos)
        {
            f.Versao++;
            f.Estado = EstadoFila.Pendente;
            f.Tentativas = 0;
            f.ProximaTentativa = null;
            f.AtualizadoEm = _agora();
        }
        await db.SaveChangesAsync();
        return falhos.Count;
    }

    /// <summary>Processa um ciclo: tudo o que está pendente e já pode ser tentado. Nunca lança por falha de rede.</summary>
    public async Task<ResultadoFila> ProcessarAsync(string? operador = null, CancellationToken ct = default)
    {
        if (!await _trava.WaitAsync(0, ct)) return new ResultadoFila(0, 0, 0, 0, "Já existe um envio em andamento.");
        var inicio = _agora();
        int enviados = 0, semMudanca = 0, recusados = 0, reagendados = 0;
        string? parada = null;
        try
        {
            var versoes = await ReservarAsync();
            if (versoes.Count == 0) return new ResultadoFila(0, 0, 0, 0, null);

            try
            {
                await _destino.VerificarAsync(ct);
            }
            catch (FalhaDestinoException ex)
            {
                reagendados = await DevolverAsync(versoes.Keys, ex.Message);
                return await Concluir(inicio, operador, new ResultadoFila(0, 0, 0, reagendados, ex.Message), 0, 0);
            }

            var plano = await PlanejarAsync(versoes.Keys.ToList());
            var concluidos = new List<Guid>(plano.SemMudanca);
            semMudanca = plano.SemMudanca.Count;
            foreach (var (guid, motivo) in plano.Recusados)
            {
                await MarcarFalhaAsync(guid, versoes[guid], motivo);
                recusados++;
            }
            await ConfirmarAsync(concluidos, versoes, Array.Empty<ItemPlano>());

            var operacoes = plano.Operacoes;
            var criados = 0;
            var restantes = new HashSet<Guid>(operacoes.Select(o => o.Item.Produto.Guid));
            try
            {
                foreach (var lote in operacoes.Chunk(_tamanhoLote))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await _destino.EnviarLoteAsync(lote, ct);
                        await ConfirmarAsync(lote.Select(o => o.Item.Produto.Guid), versoes, lote.Select(o => o.Item));
                        foreach (var o in lote) restantes.Remove(o.Item.Produto.Guid);
                        enviados += lote.Length;
                    }
                    catch (FalhaDestinoException ex) when (ex.Gravidade == GravidadeFalha.DadosRecusados)
                    {
                        foreach (var op in lote) // o servidor rejeitou o lote inteiro: acha quem é o culpado
                        {
                            var g = op.Item.Produto.Guid;
                            try
                            {
                                await _destino.EnviarLoteAsync(new[] { op }, ct);
                                await ConfirmarAsync(new[] { g }, versoes, new[] { op.Item });
                                enviados++;
                            }
                            catch (FalhaDestinoException ex2) when (ex2.Gravidade == GravidadeFalha.DadosRecusados)
                            {
                                await MarcarFalhaAsync(g, versoes[g], ex2.Message);
                                recusados++;
                            }
                            restantes.Remove(g);
                        }
                    }
                }
                criados = enviados;
                if (enviados > 0) await AnotarAtualizacaoAsync(ct);
            }
            catch (FalhaDestinoException ex)
            {
                parada = ex.Message;
                reagendados = await DevolverAsync(restantes, ex.Message);
            }
            catch (OperationCanceledException)
            {
                parada = "Cancelado.";
                reagendados = await DevolverAsync(restantes, "Cancelado.", contarTentativa: false);
            }
            return await Concluir(inicio, operador, new ResultadoFila(enviados, semMudanca, recusados, reagendados, parada), criados, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Erro inesperado (ex.: banco local): o que estava "Enviando" volta para a fila na próxima recuperação.
            return new ResultadoFila(enviados, semMudanca, recusados, reagendados, "Erro inesperado: " + ex.Message);
        }
        finally
        {
            _trava.Release();
        }
    }

    // ---- passos ----

    /// <summary>Pega os itens prontos, marca "Enviando" e devolve guid -> versão vista (para saber se mudou durante o envio).</summary>
    private async Task<Dictionary<Guid, int>> ReservarAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var agora = _agora();
        var itens = await Fila(db)
            .Where(f => f.Estado == EstadoFila.Pendente && (f.ProximaTentativa == null || f.ProximaTentativa <= agora))
            .OrderBy(f => f.Id).ToListAsync();
        foreach (var f in itens)
        {
            f.Estado = EstadoFila.Enviando;
            f.AtualizadoEm = agora;
        }
        await db.SaveChangesAsync();
        return itens.ToDictionary(f => f.EntidadeGuid, f => f.Versao);
    }

    private record Planejado(List<Guid> SemMudanca, List<(Guid, string)> Recusados, List<OperacaoCatalogo> Operacoes);

    private async Task<Planejado> PlanejarAsync(List<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Tamanhos)
            .Where(p => guids.Contains(p.Guid)).ToListAsync();
        var estado = await _repo.ObterEstadoAsync();
        var plano = RegrasSincronizacao.Planejar(produtos.Select(RegrasSincronizacao.Montar), estado);

        var operacoes = plano.Novos.Concat(plano.Alterados).Select(i => new OperacaoCatalogo(i, TipoOperacao.Gravar))
            .Concat(plano.Desativar.Select(i => new OperacaoCatalogo(i, TipoOperacao.Desativar))).ToList();
        var recusados = plano.Invalidos.Select(i => (i.Guid, i.Motivo)).ToList();
        foreach (var g in guids.Where(g => produtos.All(p => p.Guid != g)))
            recusados.Add((g, "produto não existe mais no banco local"));

        var tratados = operacoes.Select(o => o.Item.Produto.Guid).Concat(recusados.Select(r => r.Item1)).ToHashSet();
        return new Planejado(guids.Where(g => !tratados.Contains(g)).ToList(), recusados, operacoes);
    }

    /// <summary>O servidor confirmou: anota o hash enviado e encerra o item, a menos que o produto tenha mudado no meio.</summary>
    private async Task ConfirmarAsync(IEnumerable<Guid> guids, Dictionary<Guid, int> versoes, IEnumerable<ItemPlano> itens)
    {
        var lista = guids.ToList();
        if (lista.Count == 0) return;
        await _repo.RegistrarEnviadosAsync(itens);

        await using var db = await _fabrica.CreateDbContextAsync();
        var linhas = await Fila(db).Where(f => lista.Contains(f.EntidadeGuid)).ToListAsync();
        var agora = _agora();
        foreach (var f in linhas)
        {
            if (f.Versao != versoes[f.EntidadeGuid]) { f.Estado = EstadoFila.Pendente; continue; } // mudou durante o envio
            f.Estado = EstadoFila.Enviado;
            f.EnviadoEm = agora;
            f.Tentativas = 0;
            f.ProximaTentativa = null;
            f.UltimoErro = null;
            f.AtualizadoEm = agora;
        }
        await db.SaveChangesAsync();
    }

    private async Task MarcarFalhaAsync(Guid guid, int versaoVista, string motivo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await Fila(db).FirstAsync(x => x.EntidadeGuid == guid);
        if (f.Versao != versaoVista) { f.Estado = EstadoFila.Pendente; }
        else { f.Estado = EstadoFila.Falhou; f.UltimoErro = Cortar(motivo); }
        f.AtualizadoEm = _agora();
        await db.SaveChangesAsync();
    }

    /// <summary>Falha de conexão/servidor: volta para Pendente com espera crescente.</summary>
    private async Task<int> DevolverAsync(IEnumerable<Guid> guids, string erro, bool contarTentativa = true)
    {
        var lista = guids.ToList();
        if (lista.Count == 0) return 0;
        await using var db = await _fabrica.CreateDbContextAsync();
        var linhas = await Fila(db).Where(f => lista.Contains(f.EntidadeGuid) && f.Estado == EstadoFila.Enviando).ToListAsync();
        var agora = _agora();
        foreach (var f in linhas)
        {
            if (contarTentativa) f.Tentativas++;
            f.Estado = EstadoFila.Pendente;
            f.ProximaTentativa = contarTentativa ? agora + Espera(f.Tentativas) : null;
            f.UltimoErro = Cortar(erro);
            f.AtualizadoEm = agora;
        }
        await db.SaveChangesAsync();
        return linhas.Count;
    }

    private async Task AnotarAtualizacaoAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await _fabrica.CreateDbContextAsync(ct);
            var ativos = await db.Produtos.CountAsync(p => p.Ativo, ct);
            await _destino.RegistrarAtualizacaoAsync(_agora(), ativos, ct);
        }
        catch (FalhaDestinoException) { } // o catálogo em si foi enviado; a data se acerta no próximo ciclo
    }

    private async Task<ResultadoFila> Concluir(DateTime inicio, string? operador, ResultadoFila r, int criados, int desativados)
    {
        var resultado = r.Enviados > 0
            ? (r.Parada is null && r.Recusados == 0 ? ResultadoSincronizacao.Sucesso : ResultadoSincronizacao.Parcial)
            : (r.Reagendados > 0 || r.Recusados > 0 ? ResultadoSincronizacao.Falhou : ResultadoSincronizacao.SemMudancas);
        var msg = $"Fila: {r.Enviados} enviado(s), {r.SemMudanca} sem mudança, {r.Recusados} recusado(s), {r.Reagendados} reagendado(s)."
                  + (r.Parada is null ? "" : " Parou: " + r.Parada);
        await _repo.GravarLogAsync(new SincronizacaoLog
        {
            IniciadaEm = inicio, ConcluidaEm = _agora(), Operador = operador is { Length: > 80 } ? operador[..80] : operador,
            Criados = criados, Desativados = desativados, Inalterados = r.SemMudanca,
            Erros = r.Recusados + r.Reagendados, Resultado = resultado, Mensagem = Cortar(msg)
        });
        return r;
    }

    private static string Cortar(string t) => t.Length <= 480 ? t : t[..477] + "...";
}
