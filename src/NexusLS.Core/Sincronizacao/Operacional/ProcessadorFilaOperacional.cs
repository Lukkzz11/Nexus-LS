using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Sincronizacao.Operacional;

/// <summary>
/// Esvazia a fila das entidades operacionais (hoje vendas) para o projeto operacional. Mesmas garantias do envio do
/// catálogo: só marca "Enviado" depois da confirmação, reagenda com espera crescente se a conexão cair, recusa só o
/// documento problemático, e entidade alterada durante o envio continua pendente. Reenviar é seguro: o documento tem o
/// Guid como id e é sempre montado do estado ATUAL do SQLite.
/// </summary>
public class ProcessadorFilaOperacional
{
    public const int TamanhoPadraoDoLote = 200;

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly IMontadorDocumentos _montador;
    private readonly IDestinoOperacional _destino;
    private readonly Func<DateTime> _agora;
    private readonly int _tamanhoLote;
    private readonly string[] _entidades;
    private readonly SemaphoreSlim _trava = new(1, 1);

    public ProcessadorFilaOperacional(
        IDbContextFactory<PdvDbContext> fabrica, IMontadorDocumentos montador, IDestinoOperacional destino,
        Func<DateTime>? agora = null, int tamanhoLote = TamanhoPadraoDoLote)
    {
        if (tamanhoLote < 1 || tamanhoLote > 500)
            throw new ArgumentOutOfRangeException(nameof(tamanhoLote), "O lote deve ter de 1 a 500 documentos.");
        _fabrica = fabrica;
        _montador = montador;
        _destino = destino;
        _agora = agora ?? (() => DateTime.Now);
        _tamanhoLote = tamanhoLote;
        _entidades = montador.Entidades.ToArray();
    }

    private IQueryable<ItemFilaSincronizacao> Fila(PdvDbContext db) => db.FilaSincronizacao.Where(f => _entidades.Contains(f.Entidade));

    /// <summary>Ao abrir o programa: o que ficou "Enviando" por uma queda volta a Pendente (e será reenviado sem duplicar).</summary>
    public async Task<int> RecuperarInterrompidosAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var presos = await Fila(db).Where(f => f.Estado == EstadoFila.Enviando).ToListAsync();
        foreach (var f in presos) { f.Estado = EstadoFila.Pendente; f.ProximaTentativa = null; f.AtualizadoEm = _agora(); }
        await db.SaveChangesAsync();
        return presos.Count;
    }

    public async Task<ResumoFila> ResumoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var contagem = await Fila(db).AsNoTracking().GroupBy(f => f.Estado).Select(g => new { Estado = g.Key, N = g.Count() }).ToListAsync();
        int Qtd(EstadoFila e) => contagem.FirstOrDefault(c => c.Estado == e)?.N ?? 0;
        return new ResumoFila(Qtd(EstadoFila.Pendente), Qtd(EstadoFila.Enviando), Qtd(EstadoFila.Enviado), Qtd(EstadoFila.Falhou));
    }

    /// <summary>Devolve à fila o que foi recusado, para tentar de novo.</summary>
    public async Task<int> ReenfileirarFalhasAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var falhos = await Fila(db).Where(f => f.Estado == EstadoFila.Falhou).ToListAsync();
        foreach (var f in falhos) { f.Versao++; f.Estado = EstadoFila.Pendente; f.Tentativas = 0; f.ProximaTentativa = null; f.AtualizadoEm = _agora(); }
        await db.SaveChangesAsync();
        return falhos.Count;
    }

    /// <summary>
    /// Põe na fila as entidades que ainda não têm linha (as criadas antes do envio operacional existir). Só as que o
    /// montador conhece. Seguro repetir.
    /// </summary>
    public async Task<int> EnfileirarExistentesAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var agora = _agora();
        var criadas = 0;

        async Task Enfileirar<T>(string entidade) where T : class, IEntidadeSincronizavel
        {
            if (!_entidades.Contains(entidade)) return;
            var faltam = await db.Set<T>().AsNoTracking()
                .Where(e => !db.FilaSincronizacao.Any(f => f.Entidade == entidade && f.EntidadeGuid == EF.Property<Guid>(e, "Guid")))
                .Select(e => EF.Property<Guid>(e, "Guid")).ToListAsync();
            foreach (var g in faltam)
                db.FilaSincronizacao.Add(new ItemFilaSincronizacao { Entidade = entidade, EntidadeGuid = g, CriadoEm = agora, AtualizadoEm = agora });
            criadas += faltam.Count;
        }

        await Enfileirar<Venda>(NomesEntidade.Venda);
        await Enfileirar<TurnoCaixa>(NomesEntidade.TurnoCaixa);
        await Enfileirar<MovimentoCaixa>(NomesEntidade.MovimentoCaixa);
        await Enfileirar<FechamentoCaixa>(NomesEntidade.FechamentoCaixa);
        await Enfileirar<PagamentoCliente>(NomesEntidade.PagamentoCliente);
        await Enfileirar<Lancamento>(NomesEntidade.Lancamento);
        await Enfileirar<ContaFinanceira>(NomesEntidade.ContaFinanceira);
        await Enfileirar<TransferenciaConta>(NomesEntidade.TransferenciaConta);
        await Enfileirar<LiquidacaoCartao>(NomesEntidade.LiquidacaoCartao);
        await Enfileirar<ChequeRecebido>(NomesEntidade.ChequeRecebido);
        await Enfileirar<Cliente>(NomesEntidade.Cliente);
        await Enfileirar<Fornecedor>(NomesEntidade.Fornecedor);
        await Enfileirar<Compra>(NomesEntidade.Compra);
        await Enfileirar<Pedido>(NomesEntidade.Pedido);
        await Enfileirar<MateriaPrima>(NomesEntidade.MateriaPrima);
        await Enfileirar<OrdemProducao>(NomesEntidade.OrdemProducao);
        await Enfileirar<PerdaProducao>(NomesEntidade.PerdaProducao);

        await db.SaveChangesAsync();
        return criadas;
    }
    /// <summary>Um ciclo. Nunca lança por falha de rede.</summary>
    public async Task<ResultadoFila> ProcessarAsync(CancellationToken ct = default)
    {
        if (!await _trava.WaitAsync(0, ct)) return new ResultadoFila(0, 0, 0, 0, "Já existe um envio em andamento.");
        int enviados = 0, recusados = 0, reagendados = 0;
        string? parada = null;
        try
        {
            var reservados = await ReservarAsync();
            if (reservados.Count == 0) return new ResultadoFila(0, 0, 0, 0, null);

            try
            {
                await _destino.VerificarAsync(ct);
            }
            catch (FalhaDestinoException ex)
            {
                return new ResultadoFila(0, 0, 0, await DevolverAsync(reservados.Keys, ex.Message), ex.Message);
            }

            // Monta tudo por entidade; o que não existe mais no SQLite é recusado com motivo.
            var documentos = new List<(string Entidade, DocumentoOperacional Doc)>();
            foreach (var grupo in reservados.Keys.GroupBy(k => k.Entidade))
            {
                var montados = await _montador.MontarAsync(grupo.Key, grupo.Select(k => k.Guid).ToList());
                documentos.AddRange(montados.Documentos.Select(d => (grupo.Key, d)));
                foreach (var g in montados.Ausentes)
                {
                    await MarcarFalhaAsync((grupo.Key, g), reservados[(grupo.Key, g)], "registro não existe mais no banco local");
                    recusados++;
                }
            }

            var restantes = new HashSet<(string, Guid)>(documentos.Select(d => (d.Entidade, d.Doc.Guid)));
            try
            {
                foreach (var lote in documentos.Chunk(_tamanhoLote))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await _destino.EnviarLoteAsync(lote.Select(l => l.Doc).ToList(), ct);
                        await ConfirmarAsync(lote.Select(l => (l.Entidade, l.Doc.Guid)).ToList(), reservados);
                        foreach (var l in lote) restantes.Remove((l.Entidade, l.Doc.Guid));
                        enviados += lote.Length;
                    }
                    catch (FalhaDestinoException ex) when (ex.Gravidade == GravidadeFalha.DadosRecusados)
                    {
                        foreach (var l in lote) // o servidor rejeitou o lote: acha o documento culpado
                        {
                            var chave = (l.Entidade, l.Doc.Guid);
                            try
                            {
                                await _destino.EnviarLoteAsync(new[] { l.Doc }, ct);
                                await ConfirmarAsync(new[] { chave }, reservados);
                                enviados++;
                            }
                            catch (FalhaDestinoException ex2) when (ex2.Gravidade == GravidadeFalha.DadosRecusados)
                            {
                                await MarcarFalhaAsync(chave, reservados[chave], ex2.Message);
                                recusados++;
                            }
                            restantes.Remove(chave);
                        }
                    }
                }
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
            return new ResultadoFila(enviados, 0, recusados, reagendados, parada);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Erro inesperado (ex.: banco local): o que estava "Enviando" volta na próxima recuperação.
            return new ResultadoFila(enviados, 0, recusados, reagendados, "Erro inesperado: " + ex.Message);
        }
        finally
        {
            _trava.Release();
        }
    }

    // ---- passos da fila ----

    private async Task<Dictionary<(string Entidade, Guid Guid), int>> ReservarAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var agora = _agora();
        var itens = await Fila(db)
            .Where(f => f.Estado == EstadoFila.Pendente && (f.ProximaTentativa == null || f.ProximaTentativa <= agora))
            .OrderBy(f => f.Id).ToListAsync();
        foreach (var f in itens) { f.Estado = EstadoFila.Enviando; f.AtualizadoEm = agora; }
        await db.SaveChangesAsync();
        return itens.ToDictionary(f => (f.Entidade, f.EntidadeGuid), f => f.Versao);
    }

    private async Task ConfirmarAsync(IReadOnlyCollection<(string Entidade, Guid Guid)> chaves, Dictionary<(string Entidade, Guid Guid), int> versoes)
    {
        var guids = chaves.Select(c => c.Guid).ToList();
        await using var db = await _fabrica.CreateDbContextAsync();
        var linhas = await Fila(db).Where(f => guids.Contains(f.EntidadeGuid)).ToListAsync();
        var agora = _agora();
        foreach (var f in linhas.Where(f => chaves.Contains((f.Entidade, f.EntidadeGuid))))
        {
            if (f.Versao != versoes[(f.Entidade, f.EntidadeGuid)]) { f.Estado = EstadoFila.Pendente; continue; } // mudou durante o envio
            f.Estado = EstadoFila.Enviado;
            f.EnviadoEm = agora;
            f.Tentativas = 0;
            f.ProximaTentativa = null;
            f.UltimoErro = null;
            f.AtualizadoEm = agora;
        }
        await db.SaveChangesAsync();
    }

    private async Task MarcarFalhaAsync((string Entidade, Guid Guid) chave, int versaoVista, string motivo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await Fila(db).FirstAsync(x => x.Entidade == chave.Entidade && x.EntidadeGuid == chave.Guid);
        if (f.Versao != versaoVista) f.Estado = EstadoFila.Pendente;
        else { f.Estado = EstadoFila.Falhou; f.UltimoErro = Cortar(motivo); }
        f.AtualizadoEm = _agora();
        await db.SaveChangesAsync();
    }

    private async Task<int> DevolverAsync(IEnumerable<(string Entidade, Guid Guid)> chaves, string erro, bool contarTentativa = true)
    {
        var lista = chaves.ToList();
        if (lista.Count == 0) return 0;
        var guids = lista.Select(c => c.Guid).ToList();
        await using var db = await _fabrica.CreateDbContextAsync();
        var linhas = (await Fila(db).Where(f => guids.Contains(f.EntidadeGuid) && f.Estado == EstadoFila.Enviando).ToListAsync())
            .Where(f => lista.Contains((f.Entidade, f.EntidadeGuid))).ToList();
        var agora = _agora();
        foreach (var f in linhas)
        {
            if (contarTentativa) f.Tentativas++;
            f.Estado = EstadoFila.Pendente;
            f.ProximaTentativa = contarTentativa ? agora + ProcessadorFila.Espera(f.Tentativas) : null;
            f.UltimoErro = Cortar(erro);
            f.AtualizadoEm = agora;
        }
        await db.SaveChangesAsync();
        return linhas.Count;
    }

    private static string Cortar(string t) => t.Length <= 480 ? t : t[..477] + "...";
}
