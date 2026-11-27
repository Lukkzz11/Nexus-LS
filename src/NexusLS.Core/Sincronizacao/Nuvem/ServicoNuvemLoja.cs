using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Sincronizacao.Nuvem;

/// <summary>Já existe, na nuvem, uma loja com este código que parece ser OUTRO computador (banco copiado). Só segue se o usuário confirmar.</summary>
public sealed class LojaIdDuplicadoException : Exception
{
    public LojaIdDuplicadoException(string mensagem) : base(mensagem) { }
}

public sealed record ResultadoEnvioNuvem(int Dias, int Produtos, int Lancamentos, int ProdutosPendentes, DateTime EnviadoEmUtc)
{
    public string Resumo
    {
        get
        {
            var t = $"Enviado à nuvem: {Dias} dia(s), {Produtos} produto(s) no estoque e {Lancamentos} lançamento(s).";
            return ProdutosPendentes > 0 ? t + $" Faltam {ProdutosPendentes} produto(s): clique de novo para enviar o resto." : t;
        }
    }
}

/// <summary>O desempenho de uma loja no período, como veio da nuvem (ou do último dado baixado).</summary>
public sealed record ResumoLoja(string LojaId, string Nome, long FaturamentoCentavos, int Vendas, int Pecas, long TicketMedioCentavos,
    DateTime AtualizadoEmUtc, bool EstaLoja);

public sealed record LeituraLojas(IReadOnlyList<ResumoLoja> Lojas, bool DoCache, DateTime? DadosDeUtc, string? Aviso);

public sealed record ResultadoRecebimentoPrecos(int Aplicados, int Removidos, int Ignorados, int Consultados);

/// <summary>
/// "Compartilhar com a nuvem" e "Receber da nuvem" (PROPOSTA_GESTAO.md, seção 7). Só age quando a tela chama (clique ou fechamento do caixa).
/// Uma falha de rede nunca deixa nada pela metade no computador: o estado local só muda depois que a nuvem aceitou tudo, e
/// reenviar é seguro (cada documento é substituído). Permissões do programa decidem quem vê todas as lojas; a proteção real
/// entre lojas são as regras do Firestore (docs/REGRAS_FIRESTORE_POR_LOJA.md).
/// </summary>
public sealed class ServicoNuvemLoja
{
    /// <summary>Quantos documentos, no máximo, um clique envia (o resto fica para o próximo clique).</summary>
    public const int MaximoDocumentosPorEnvio = 3000;
    private const int DiasPrimeiroEnvio = 30;
    private const int DiasSempreReenviados = 7;
    private const int DiasMaximoRetroativo = 90;

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<ClienteNuvemLoja> _cliente;
    private readonly CacheNuvemLoja _cache;
    private readonly Func<(string LojaId, string Nome)> _loja;
    private readonly PrecoLojaService _precos;
    private readonly Func<bool> _ehAdministrador;
    private readonly Func<bool> _podeCompartilhar;
    private readonly Func<DateTime> _agoraUtc;
    private readonly Func<DateTime> _hoje;
    private readonly string _versao;

    public ServicoNuvemLoja(IDbContextFactory<PdvDbContext> fabrica, Func<ClienteNuvemLoja> cliente, CacheNuvemLoja cache,
        Func<(string LojaId, string Nome)> loja, PrecoLojaService precos, Func<bool> ehAdministrador, Func<bool>? podeCompartilhar = null,
        string versao = "", Func<DateTime>? agoraUtc = null, Func<DateTime>? hoje = null)
    {
        _fabrica = fabrica;
        _cliente = cliente;
        _cache = cache;
        _loja = loja;
        _precos = precos;
        _ehAdministrador = ehAdministrador;
        _podeCompartilhar = podeCompartilhar ?? (() => true);
        _versao = versao;
        _agoraUtc = agoraUtc ?? (() => DateTime.UtcNow);
        _hoje = hoje ?? (() => DateTime.Today);
    }

    public EstadoNuvemLoja Estado => _cache.Carregar();
    public bool EhAdministrador => _ehAdministrador();

    /// <summary>Guarda o código da empresa (o mesmo em todas as lojas) usado nos caminhos da nuvem.</summary>
    public void SalvarEmpresa(string empresaId)
    {
        var id = CaminhosNuvem.ValidarId(empresaId, "empresa");
        _cache.Atualizar(e => e.EmpresaId = id);
    }

    private ClienteNuvemLoja Cliente()
    {
        try { return _cliente(); }
        catch (FalhaDestinoException ex) { throw FalhaNuvemException.De(ex); }
    }

    private async Task<T> Protegido<T>(Func<Task<T>> acao)
    {
        try { return await acao(); }
        catch (FalhaDestinoException ex) { throw FalhaNuvemException.De(ex); }
    }

    // ===================================================================== compartilhar
    public Task<ResultadoEnvioNuvem> CompartilharAsync(bool ignorarConflito = false, Action<string>? progresso = null, CancellationToken ct = default) =>
        Protegido(() =>
        {
            if (!_podeCompartilhar()) throw new RegraDeNegocioException("Você não tem permissão para compartilhar os dados da loja com a nuvem.");
            return CompartilharInternoAsync(ignorarConflito, progresso ?? (_ => { }), ct);
        });

    /// <summary>
    /// Envio automático do fechamento do caixa: manda o que mudou (vendas, quantidades, produtos). É uma regra da loja, por
    /// isso não depende da permissão de quem está no caixa. Nulo = a nuvem não está configurada (sem código da empresa).
    /// Nunca passa por cima do aviso de loja duplicada.
    /// </summary>
    public async Task<ResultadoEnvioNuvem?> EnviarNoFechamentoAsync(CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(_cache.Carregar().EmpresaId)
            ? null
            : await Protegido(() => CompartilharInternoAsync(false, _ => { }, ct));

    private async Task<ResultadoEnvioNuvem> CompartilharInternoAsync(bool ignorarConflito, Action<string> progresso, CancellationToken ct)
    {
        var cliente = Cliente();
        var (lojaIdBruto, nomeBruto) = _loja();
        var lojaId = CaminhosNuvem.ValidarId(lojaIdBruto, "lojaId");
        var empresa = cliente.EmpresaId;
        var estado = _cache.Carregar();

        progresso("Conferindo esta loja na nuvem...");
        var existente = await cliente.LerAsync(CaminhosNuvem.Loja(empresa, lojaId), ct);
        if (existente is not null && !ignorarConflito) ConferirOutroComputador(existente, estado, nomeBruto);

        progresso("Lendo os dados desta loja...");
        var agora = _agoraUtc();
        var hoje = _hoje().Date;
        var inicio = estado.UltimoEnvioUtc is null
            ? hoje.AddDays(-(DiasPrimeiroEnvio - 1))
            : Min(hoje.AddDays(-(DiasSempreReenviados - 1)), estado.UltimoEnvioUtc.Value.ToLocalTime().Date.AddDays(-1));
        if (inicio < hoje.AddDays(-DiasMaximoRetroativo)) inicio = hoje.AddDays(-DiasMaximoRetroativo);
        var fimExclusivo = hoje.AddDays(1);

        List<DocumentoNuvem> dias = new(), lancamentos = new(), estoque = new();
        var novosHashes = new Dictionary<string, string>();
        var pendentes = 0;
        await using (var db = await _fabrica.CreateDbContextAsync(ct))
        {
            var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens)
                .Where(v => v.DataHora >= inicio && v.DataHora < fimExclusivo).ToListAsync(ct);
            var devolucoes = await db.Devolucoes.AsNoTracking().Where(d => d.DataHora >= inicio && d.DataHora < fimExclusivo).ToListAsync(ct);
            var produtos = await db.Produtos.AsNoTracking().Include(p => p.Tamanhos).ToListAsync(ct);

            foreach (var dia in vendas.Select(v => v.DataHora.Date).Concat(devolucoes.Select(d => d.DataHora.Date)).Distinct().OrderBy(d => d))
            {
                var resumo = MontadorPacoteLoja.ResumoDoDia(dia, vendas, produtos, devolucoes, agora);
                dias.Add(new DocumentoNuvem(CaminhosNuvem.Dia(empresa, lojaId, dia), SerializadorPacoteLoja.ParaCampos(resumo)));
            }

            var lancs = await db.LancamentosEmpresa.AsNoTracking().Include(l => l.Anexos)
                .Where(l => l.Data >= inicio || l.AlteradoEm >= inicio).ToListAsync(ct);
            foreach (var l in lancs)
            {
                var doc = MontadorPacoteLoja.Lancamento(l, agora);
                lancamentos.Add(new DocumentoNuvem(CaminhosNuvem.Lancamento(empresa, lojaId, doc.Id), SerializadorPacoteLoja.ParaCampos(doc)));
            }

            var vagas = Math.Max(0, MaximoDocumentosPorEnvio - dias.Count - lancamentos.Count - 1);
            foreach (var e in MontadorPacoteLoja.Estoque(produtos, agora))
            {
                var campos = SerializadorPacoteLoja.ParaCampos(e);
                var hash = ImpressaoDigital(campos);
                if (estado.HashesEstoque.TryGetValue(e.ProdutoGuid, out var antigo) && antigo == hash) continue; // não mudou desde o último envio
                if (estoque.Count >= vagas) { pendentes++; continue; }
                estoque.Add(new DocumentoNuvem(CaminhosNuvem.Estoque(empresa, lojaId, e.ProdutoGuid), campos));
                novosHashes[e.ProdutoGuid] = hash;
            }
        }

        var lojaDoc = MontadorPacoteLoja.Loja(lojaId, nomeBruto, _versao, agora);
        var todos = dias.Concat(lancamentos).Concat(estoque).ToList();
        var total = todos.Count + 1;
        progresso($"Enviando {total} documento(s) à nuvem...");
        var enviados = await cliente.GravarAsync(todos, (feitos, _) => progresso($"Enviando... {feitos} de {total}"), ct);
        // O documento da loja vai por último: "atualizado em" só muda quando o resto chegou.
        await cliente.GravarAsync(new[] { new DocumentoNuvem(CaminhosNuvem.Loja(empresa, lojaId), SerializadorPacoteLoja.ParaCampos(lojaDoc)) }, null, ct);
        progresso($"Enviado: {enviados + 1} de {total}.");

        _cache.Atualizar(e =>
        {
            e.UltimoEnvioUtc = agora;
            e.UltimoDocLojaUtc = lojaDoc.AtualizadoEmUtc;
            foreach (var h in novosHashes) e.HashesEstoque[h.Key] = h.Value;
        });
        return new ResultadoEnvioNuvem(dias.Count, estoque.Count, lancamentos.Count, pendentes, agora);
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static void ConferirOutroComputador(DocumentoLido existente, EstadoNuvemLoja estado, string nomeLocal)
    {
        LojaNuvem naNuvem;
        try { naNuvem = SerializadorPacoteLoja.LojaDeCampos(existente.Campos); }
        catch (FormatException) { return; }
        var diferente = estado.UltimoDocLojaUtc is DateTime meu
            ? Math.Abs((naNuvem.AtualizadoEmUtc - meu).TotalMilliseconds) > 1
            : !string.Equals(naNuvem.Nome.Trim(), (nomeLocal ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        if (diferente)
            throw new LojaIdDuplicadoException(
                $"Já existe na nuvem uma loja com este código (\"{naNuvem.Nome}\", enviada em {naNuvem.AtualizadoEmUtc.ToLocalTime():dd/MM/yyyy HH:mm}) que não foi enviada por este computador. " +
                "Isso acontece quando o banco foi copiado de outro computador: as duas lojas gravariam no mesmo lugar. Se este computador é mesmo essa loja, envie mesmo assim.");
    }

    internal static string ImpressaoDigital(JsonObject campos)
    {
        var c = (JsonObject)campos.DeepClone();
        c.Remove("atualizadoEm");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.ToJsonString())));
    }

    // ===================================================================== ler lojas (comparativo)
    /// <summary>
    /// Lê da nuvem o desempenho no período. Administrador: todas as lojas. Usuário de loja: só a própria. Sem internet (ou sem
    /// permissão na nuvem), devolve o último dado baixado, com a data, e um aviso em português.
    /// </summary>
    public async Task<LeituraLojas> LerLojasAsync(DateTime de, DateTime ate, CancellationToken ct = default)
    {
        de = de.Date; ate = ate.Date;
        var admin = _ehAdministrador();
        var propria = _loja().LojaId;
        try
        {
            var cliente = Cliente();
            var empresa = cliente.EmpresaId;
            var lojas = new List<LojaNuvem>();
            if (admin)
            {
                foreach (var d in await cliente.ListarAsync($"empresas/{empresa}/lojas", ct))
                {
                    try { lojas.Add(SerializadorPacoteLoja.LojaDeCampos(d.Campos) with { LojaId = CaminhosNuvem.ValidarId(d.Id, "lojaId") }); }
                    catch (Exception ex) when (ex is FormatException or RegraDeNegocioException) { /* documento fora do padrão: ignora */ }
                }
            }
            else
            {
                var d = await cliente.LerAsync(CaminhosNuvem.Loja(empresa, CaminhosNuvem.ValidarId(propria, "lojaId")), ct);
                if (d is not null) lojas.Add(SerializadorPacoteLoja.LojaDeCampos(d.Campos) with { LojaId = propria });
            }

            var novas = new List<LojaLida>();
            foreach (var l in lojas)
            {
                var dias = new List<DiaLido>();
                foreach (var doc in await cliente.ConsultarDiasAsync(l.LojaId, de, ate, ct))
                {
                    try
                    {
                        var r = SerializadorPacoteLoja.DiaDeCampos(doc.Campos);
                        dias.Add(new DiaLido { Dia = r.Id, FaturamentoCentavos = r.FaturamentoCentavos, NumeroDeVendas = r.NumeroDeVendas, Pecas = r.Pecas });
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException) { }
                }
                novas.Add(new LojaLida { LojaId = l.LojaId, Nome = l.Nome, VersaoNexus = l.VersaoNexus, AtualizadoEmUtc = l.AtualizadoEmUtc, Dias = dias });
            }

            var agora = _agoraUtc();
            _cache.Atualizar(e =>
            {
                var antigas = e.Lojas.ToDictionary(x => x.LojaId, StringComparer.Ordinal);
                var resultado = new List<LojaLida>();
                foreach (var n in novas)
                {
                    var dias = antigas.TryGetValue(n.LojaId, out var velha) ? velha.Dias.Where(x => !NoPeriodo(x.Dia, de, ate)).ToList() : new List<DiaLido>();
                    dias.AddRange(n.Dias);
                    n.Dias = dias;
                    resultado.Add(n);
                }
                // Não-administrador só leu a própria loja: as outras que já estavam guardadas continuam como estavam.
                if (!admin) resultado.AddRange(e.Lojas.Where(x => novas.All(n => n.LojaId != x.LojaId)));
                e.Lojas = resultado;
                e.UltimaLeituraUtc = agora;
            });
            return Montar(novas, de, ate, propria, doCache: false, agora, null);
        }
        catch (Exception ex) when (ex is FalhaNuvemException or FalhaDestinoException)
        {
            var mensagem = ex is FalhaDestinoException fd ? FalhaNuvemException.De(fd).Message : ex.Message;
            var estado = _cache.Carregar();
            var guardadas = admin ? estado.Lojas : estado.Lojas.Where(l => l.LojaId == propria).ToList();
            var aviso = estado.UltimaLeituraUtc is DateTime quando
                ? $"{mensagem} Mostrando o último dado baixado, de {quando.ToLocalTime():dd/MM/yyyy HH:mm}."
                : $"{mensagem} Ainda não há nenhum dado baixado da nuvem.";
            return Montar(guardadas, de, ate, propria, doCache: true, estado.UltimaLeituraUtc, aviso);
        }
    }

    private static bool NoPeriodo(string dia, DateTime de, DateTime ate) =>
        string.CompareOrdinal(dia, de.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0
        && string.CompareOrdinal(dia, ate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) <= 0;

    private static LeituraLojas Montar(IEnumerable<LojaLida> lojas, DateTime de, DateTime ate, string propria, bool doCache, DateTime? dadosDe, string? aviso)
    {
        var linhas = lojas.OrderBy(l => l.Nome, StringComparer.CurrentCultureIgnoreCase).Select(l =>
        {
            var dias = l.Dias.Where(d => NoPeriodo(d.Dia, de, ate)).ToList();
            var fat = dias.Sum(d => d.FaturamentoCentavos);
            var vendas = dias.Sum(d => d.NumeroDeVendas);
            return new ResumoLoja(l.LojaId, l.Nome, fat, vendas, dias.Sum(d => d.Pecas), vendas > 0 ? fat / vendas : 0, l.AtualizadoEmUtc, l.LojaId == propria);
        }).ToList();
        return new LeituraLojas(linhas, doCache, dadosDe, aviso);
    }

    // ===================================================================== preços por loja
    /// <summary>Baixa os preços DESTA loja e aplica no PDV daqui. Produto e custo continuam únicos. Falha de rede não muda nada.</summary>
    public Task<ResultadoRecebimentoPrecos> ReceberPrecosAsync(Action<string>? progresso = null, CancellationToken ct = default) =>
        Protegido(async () =>
        {
            var cliente = Cliente();
            var lojaId = CaminhosNuvem.ValidarId(_loja().LojaId, "lojaId");
            progresso?.Invoke("Lendo os preços desta loja na nuvem...");
            List<Guid> guids;
            await using (var db = await _fabrica.CreateDbContextAsync(ct))
                guids = await db.Produtos.AsNoTracking().Select(p => p.Guid).ToListAsync(ct);

            var docs = await cliente.LerVariosAsync(guids.Select(g => CaminhosNuvem.Preco(cliente.EmpresaId, g.ToString("D"), lojaId)), ct);
            var validos = new List<PrecoLojaNuvem>();
            var ignorados = 0;
            foreach (var d in docs)
            {
                try
                {
                    var p = SerializadorPacoteLoja.PrecoDeCampos(d.Campos);
                    var produto = d.Caminho.Split('/')[3];   // empresas/{e}/precos/{produto}/lojas/{loja}
                    if (!string.Equals(p.LojaId, lojaId, StringComparison.Ordinal) && p.LojaId.Length > 0) { ignorados++; continue; }
                    validos.Add(p with { ProdutoGuid = produto, LojaId = lojaId });
                }
                catch (FormatException) { ignorados++; }
            }
            progresso?.Invoke("Aplicando os preços no PDV...");
            var r = await _precos.SubstituirAsync(validos, guids, ct);
            _cache.Atualizar(e => e.UltimoRecebimentoPrecosUtc = _agoraUtc());
            return new ResultadoRecebimentoPrecos(r.Aplicados, r.Removidos, r.Ignorados + ignorados, guids.Count);
        });

    /// <summary>Administrador: os preços já definidos de um produto, por loja (lojaId → preço).</summary>
    public Task<Dictionary<string, PrecoLojaNuvem>> LerPrecosDoProdutoAsync(Guid produto, IEnumerable<string> lojaIds, CancellationToken ct = default) =>
        Protegido(async () =>
        {
            ExigirAdministrador();
            var cliente = Cliente();
            var ids = lojaIds.Select(i => CaminhosNuvem.ValidarId(i, "lojaId")).Distinct().ToList();
            var docs = await cliente.LerVariosAsync(ids.Select(l => CaminhosNuvem.Preco(cliente.EmpresaId, produto.ToString("D"), l)), ct);
            var mapa = new Dictionary<string, PrecoLojaNuvem>();
            foreach (var d in docs)
            {
                try
                {
                    var loja = d.Caminho.Split('/')[5];
                    mapa[loja] = SerializadorPacoteLoja.PrecoDeCampos(d.Campos) with { ProdutoGuid = produto.ToString("D"), LojaId = loja };
                }
                catch (FormatException) { }
            }
            return mapa;
        });

    /// <summary>Administrador: define o preço de um produto para uma loja (precos/{produto}/lojas/{loja}). Valor zero ou negativo é recusado.</summary>
    public Task<PrecoLojaNuvem> DefinirPrecoAsync(Guid produto, string lojaId, long varejoCentavos, long? atacadoCentavos, string definidoPor, CancellationToken ct = default) =>
        Protegido(async () =>
        {
            ExigirAdministrador();
            if (varejoCentavos <= 0) throw new RegraDeNegocioException("O preço de varejo da loja deve ser maior que zero.");
            if (atacadoCentavos is <= 0) atacadoCentavos = null;
            if (atacadoCentavos is long a && a > varejoCentavos) throw new RegraDeNegocioException("O preço de atacado não pode ser maior que o de varejo.");
            var cliente = Cliente();
            var preco = new PrecoLojaNuvem(produto.ToString("D"), CaminhosNuvem.ValidarId(lojaId, "lojaId"), varejoCentavos, atacadoCentavos,
                (definidoPor ?? "").Trim(), _agoraUtc());
            await cliente.GravarAsync(new[] { new DocumentoNuvem(CaminhosNuvem.Preco(cliente.EmpresaId, preco.ProdutoGuid, preco.LojaId), SerializadorPacoteLoja.ParaCampos(preco)) }, null, ct);
            return preco;
        });

    private void ExigirAdministrador()
    {
        if (!_ehAdministrador()) throw new RegraDeNegocioException("Só o administrador pode definir preços por loja.");
    }
}
