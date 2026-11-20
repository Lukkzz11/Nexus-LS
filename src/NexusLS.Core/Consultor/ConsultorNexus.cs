using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Consultor;

/// <summary>
/// Consultor Nexus: sugestões de vendas, finanças e estoque calculadas só por REGRAS, em cima dos dados locais (sem internet,
/// sem IA, sem instalar nada). Pensado para computador simples: poucas consultas só de leitura e já agrupadas, tudo fora da
/// thread da tela (<see cref="Task.Run(Func{Task})"/>), uma leitura por vez e os dados ficam em cache por poucos minutos.
/// As contas ficam em <see cref="RegrasDoConsultor"/> (regras puras e testáveis); aqui só se junta, ordena e esconde o que foi dispensado.
/// </summary>
public sealed class ConsultorNexus
{
    /// <summary>Quantos cartões aparecem de uma vez.</summary>
    public const int MaximoDeCartoes = 5;

    /// <summary>Por quanto tempo os números lidos do banco valem (o botão "Atualizar" ignora o cache).</summary>
    public static readonly TimeSpan ValidadePadrao = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly DispensasConsultorStore? _dispensas;
    private readonly Func<DateTime> _agora;
    private readonly IReadOnlyList<IRegraConsultor> _regras;
    private readonly TimeSpan _validade;
    private readonly SemaphoreSlim _umPorVez = new(1, 1);

    private (DateTime Quando, int Areas, DadosConsultor Dados)? _cache;

    public ConsultorNexus(IDbContextFactory<PdvDbContext> fabrica, DispensasConsultorStore? dispensas = null, Func<DateTime>? agora = null,
        IReadOnlyList<IRegraConsultor>? regras = null, TimeSpan? validade = null)
    {
        _fabrica = fabrica;
        _dispensas = dispensas;
        _agora = agora ?? (() => DateTime.Now);
        _regras = regras ?? RegrasDoConsultor.Padrao();
        _validade = validade ?? ValidadePadrao;
    }

    /// <summary>Quantas leituras ao banco já foram feitas (para os testes conferirem o cache).</summary>
    public int Leituras { get; private set; }

    /// <summary>
    /// Os cartões de agora, no máximo <see cref="MaximoDeCartoes"/>. <paramref name="podeVer"/> diz que assuntos a pessoa pode ver
    /// (nulo = todos); o que ela não pode ver nem é lido do banco. <paramref name="atualizar"/> ignora o cache.
    /// </summary>
    public async Task<IReadOnlyList<Sugestao>> ObterAsync(Func<AreaConsultor, bool>? podeVer = null, bool atualizar = false, CancellationToken ct = default)
    {
        podeVer ??= _ => true;
        var areas = Areas(podeVer);
        await _umPorVez.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var agora = _agora();
            DadosConsultor dados;
            if (!atualizar && _cache is { } c && c.Areas == areas && agora - c.Quando < _validade && agora >= c.Quando)
                dados = c.Dados;
            else
            {
                // Task.Run: o SQLite lê de forma síncrona por baixo; assim a tela nunca espera por ele.
                dados = await Task.Run(() => CarregarAsync(agora, podeVer, ct), ct).ConfigureAwait(false);
                _cache = (agora, areas, dados);
                Leituras++;
            }
            return Avaliar(dados, _regras, podeVer, _dispensas?.Ativas(agora));
        }
        finally { _umPorVez.Release(); }
    }

    /// <summary>Esconde o cartão por 7 dias (guardado em JSON neste computador).</summary>
    public void Dispensar(string id) => _dispensas?.Dispensar(id, _agora());

    /// <summary>Quantos cartões estão dispensados agora.</summary>
    public int QuantasDispensadas => _dispensas?.Ativas(_agora()).Count ?? 0;

    /// <summary>Traz de volta todos os cartões dispensados.</summary>
    public void RestaurarDispensadas() => _dispensas?.RestaurarTodas();

    /// <summary>Esquece o cache: a próxima chamada lê o banco de novo.</summary>
    public void LimparCache() => _cache = null;

    /// <summary>
    /// A parte pura: roda as regras permitidas, tira as dispensadas, ordena (mais grave primeiro; empate pela ordem das regras)
    /// e corta em <see cref="MaximoDeCartoes"/>. Uma regra que quebrar não derruba as outras.
    /// </summary>
    public static IReadOnlyList<Sugestao> Avaliar(DadosConsultor dados, IEnumerable<IRegraConsultor> regras,
        Func<AreaConsultor, bool>? podeVer = null, IReadOnlySet<string>? dispensadas = null)
    {
        var lista = new List<(Sugestao S, int Ordem)>();
        var ordem = 0;
        foreach (var regra in regras)
        {
            var posicao = ordem++;
            if (podeVer is not null && !podeVer(regra.Area)) continue;
            if (dispensadas?.Contains(regra.Id) == true) continue;
            Sugestao? s;
            try { s = regra.Avaliar(dados); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArithmeticException or NullReferenceException) { continue; }
            if (s is null) continue;
            if (podeVer is not null && !podeVer(s.Area)) continue;
            lista.Add((s, posicao));
        }
        return lista.OrderByDescending(x => x.S.Gravidade).ThenBy(x => x.Ordem).Take(MaximoDeCartoes).Select(x => x.S).ToList();
    }

    private static int Areas(Func<AreaConsultor, bool> podeVer) =>
        Enum.GetValues<AreaConsultor>().Where(podeVer).Aggregate(0, (m, a) => m | (1 << (int)a));

    // ------------------------------------------------------------------ leitura do banco

    private async Task<DadosConsultor> CarregarAsync(DateTime agora, Func<AreaConsultor, bool> podeVer, CancellationToken ct)
    {
        var hoje = agora.Date;
        var dados = new DadosConsultor(hoje);
        await using var db = await _fabrica.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (podeVer(AreaConsultor.Vendas))
        {
            // Vendas dos 56 dias fechados (sem hoje, que ainda está pela metade): uma consulta pequena, três colunas.
            var inicio = hoje.AddDays(-56);
            var vendas = await db.Vendas.AsNoTracking()
                .Where(v => !v.Cancelada && v.DataHora >= inicio && v.DataHora < hoje)
                .Select(v => new { v.DataHora, v.TotalCentavos, v.FormaPagamento, v.SubtotalCentavos, v.DescontoCentavos })
                .ToListAsync(ct).ConfigureAwait(false);
            var porDia = Enumerable.Range(0, 56).Select(i => inicio.AddDays(i)).Select(dia =>
            {
                var doDia = vendas.Where(v => v.DataHora.Date == dia).ToList();
                return new VendasDoDia(dia, doDia.Count, doDia.Sum(v => v.TotalCentavos));
            }).ToList();
            var ini30 = hoje.AddDays(-30);
            var formas = vendas.Where(v => v.DataHora >= ini30).GroupBy(v => v.FormaPagamento)
                .Select(g => new VendasPorForma(g.Key, g.Count(), g.Sum(v => v.TotalCentavos))).ToList();
            var primeira = await db.Vendas.AsNoTracking().Where(v => !v.Cancelada).OrderBy(v => v.DataHora)
                .Select(v => (DateTime?)v.DataHora).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            var clientes = await db.Clientes.AsNoTracking().Where(c => c.Ativo && c.DataNascimento != null).ToListAsync(ct).ConfigureAwait(false);
            var horarios = vendas.GroupBy(v => v.DataHora.Hour).OrderBy(g => g.Key)
                .Select(g => new VendasPorHora(g.Key, g.Count(), g.Sum(v => v.TotalCentavos))).ToList();
            var dos30 = vendas.Where(v => v.DataHora >= ini30).ToList();
            var descontos = new DescontosDoPeriodo(dos30.Count, dos30.Sum(v => v.SubtotalCentavos), dos30.Sum(v => v.DescontoCentavos));
            var itens = await (from i in db.ItensVenda.AsNoTracking()
                               join v in db.Vendas.AsNoTracking() on i.VendaId equals v.Id
                               where !v.Cancelada && i.Quantidade > 0 && v.DataHora >= ini30 && v.DataHora < hoje
                               select new { i.TotalCentavos, i.Quantidade, i.CustoUnitarioCentavos }).ToListAsync(ct).ConfigureAwait(false);
            var comCusto = itens.Where(i => i.CustoUnitarioCentavos is > 0).ToList();
            var margem = new MargemDoPeriodo(comCusto.Sum(i => i.TotalCentavos), comCusto.Sum(i => i.CustoUnitarioCentavos!.Value * i.Quantidade),
                comCusto.Count, itens.Count - comCusto.Count);
            // Clientes sumidos: uma agregação no banco (compras, última e total por cliente).
            var limiteSumido = hoje.AddDays(-RegraClientesSumidos.DiasSumido);
            var porCliente = (await db.Vendas.AsNoTracking().Where(v => !v.Cancelada && v.ClienteId != null)
                .Select(v => new { ClienteId = v.ClienteId!.Value, v.DataHora, v.TotalCentavos })
                .ToListAsync(ct).ConfigureAwait(false))
                .GroupBy(v => v.ClienteId)
                .Select(g => new { ClienteId = g.Key, Compras = g.Count(), Ultima = g.Max(v => v.DataHora), Total = g.Sum(v => v.TotalCentavos) })
                .Where(x => x.Compras >= RegraClientesSumidos.ComprasMinimas && x.Ultima < limiteSumido).ToList();
            var ids = porCliente.Select(x => x.ClienteId).ToList();
            var nomes = await db.Clientes.AsNoTracking().Where(c => c.Ativo && ids.Contains(c.Id)).Select(c => new { c.Id, c.Nome }).ToListAsync(ct).ConfigureAwait(false);
            var sumidos = (from x in porCliente join c in nomes on x.ClienteId equals c.Id orderby x.Total descending
                           select new ClienteSumido(c.Id, c.Nome, x.Ultima, x.Compras, x.Total)).Take(20).ToList();
            dados = dados with { PrimeiraVenda = primeira, VendasPorDia = porDia, Formas = formas, Horarios = horarios, Descontos = descontos, Margem = margem, ClientesSumidos = sumidos };
            dados = dados with { Aniversariantes = AvisosAoAbrir.AniversariantesProximos(clientes, hoje) };
        }

        if (podeVer(AreaConsultor.Estoque))
        {
            var produtos = await db.Produtos.AsNoTracking().Where(p => p.Ativo && !p.VenderSemBaixa).ToListAsync(ct).ConfigureAwait(false);
            // Última venda de cada produto: um agrupamento feito pelo banco, em vez de trazer todos os itens vendidos.
            var ultimas = await (from i in db.ItensVenda.AsNoTracking()
                                 join v in db.Vendas.AsNoTracking() on i.VendaId equals v.Id
                                 where !v.Cancelada && i.Quantidade > 0
                                 group v.DataHora by i.ProdutoId into g
                                 select new { ProdutoId = g.Key, Ultima = g.Max() }).ToListAsync(ct).ConfigureAwait(false);
            var parados = RelatorioProdutos.Parados(produtos, ultimas.ToDictionary(x => x.ProdutoId, x => x.Ultima), hoje, RegraEstoqueParado.DiasParado)
                .Where(p => p.DiasSemVenda is not null) // quem nunca vendeu pode ser só um cadastro novo: não sabemos, então não entra
                .ToList();

            var ini30 = hoje.AddDays(-30);
            var pecas = await (from i in db.ItensVenda.AsNoTracking()
                               join v in db.Vendas.AsNoTracking() on i.VendaId equals v.Id
                               where !v.Cancelada && i.Quantidade > 0 && v.DataHora >= ini30 && v.DataHora < hoje
                               group i.Quantidade by i.ProdutoId into g
                               select new { ProdutoId = g.Key, Pecas = g.Sum() }).ToListAsync(ct).ConfigureAwait(false);
            var porProduto = produtos.ToDictionary(p => p.Id);
            var comVenda = pecas.Where(x => porProduto.ContainsKey(x.ProdutoId))
                .Select(x =>
                {
                    var p = porProduto[x.ProdutoId];
                    return new ProdutoComGiro(p.Id, p.Nome, p.EstoqueAtual, p.EstoqueMinimo, x.Pecas);
                })
                .ToList();
            dados = dados with { Parados = parados, ProdutosComVenda = comVenda };
        }

        if (podeVer(AreaConsultor.Financeiro))
        {
            // Contas a pagar: mesma definição da tela Início (aberta, não cancelada, sem cheque emitido pendente).
            var limite = hoje.AddDays(RegraContasAVencer.Dias + 1);
            var comCheque = db.ChequesEmitidos.Where(c => c.Situacao == SituacaoChequeEmitido.Pendente).Select(c => c.LancamentoId);
            var contas = await db.Lancamentos.AsNoTracking()
                .Where(l => !l.Cancelado && l.PagoEm == null && l.Tipo == TipoLancamento.Despesa && l.Vencimento < limite && !comCheque.Contains(l.Id))
                .Select(l => new { l.Id, l.Descricao, l.Contraparte, l.Vencimento, l.ValorCentavos })
                .ToListAsync(ct).ConfigureAwait(false);
            var aVencer = contas.OrderBy(l => l.Vencimento).ThenBy(l => l.Id)
                .Select(l => new ContaAVencer(l.Id, l.Descricao, l.Contraparte, l.Vencimento.Date, l.ValorCentavos, l.Vencimento.Date < hoje)).ToList();

            // Saldo do mês: o mesmo cálculo do Relatório financeiro (que abre o próprio contexto).
            var mes = new DateTime(hoje.Year, hoje.Month, 1);
            var rel = await new RelatorioFinanceiroService(_fabrica).GerarAsync(new DesempenhoVendas.Intervalo(mes, hoje)).ConfigureAwait(false);
            dados = dados with
            {
                Contas = aVencer,
                Saldo = new SaldoDoMes(mes, hoje, rel.TotalEntradasCentavos, rel.TotalSaidasCentavos, rel.SaldoCentavos)
            };
        }

        return dados;
    }
}
