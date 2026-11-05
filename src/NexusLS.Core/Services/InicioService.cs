using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Um produto abaixo do estoque mínimo, para a lista de alerta da tela inicial.</summary>
public record ProdutoEstoqueBaixo(string Nome, int Estoque, int Minimo);

/// <summary>Um dia no gráfico "últimos 7 dias" da tela inicial (só vendas não canceladas).</summary>
public record VendasDoDia(DateTime Dia, int Vendas, long TotalCentavos);

/// <summary>Um produto do ranking da semana: peças vendidas e o valor líquido (desconto da venda rateado).</summary>
public record ProdutoDaSemana(int ProdutoId, string Nome, int Quantidade, long TotalLiquidoCentavos);

/// <summary>Uma conta a pagar em aberto que já venceu ou vence nos próximos dias.</summary>
public record ContaAVencer(int Id, string Descricao, string? Contraparte, DateTime Vencimento, long ValorCentavos, bool Atrasada);

/// <summary>Os números da tela inicial. Vendas canceladas nunca entram nos totais.</summary>
public record ResumoInicio(
    DateTime Dia,
    int VendasHoje,
    long TotalHojeCentavos,
    long TicketMedioHojeCentavos,
    int CanceladasHoje,
    long TotalOntemCentavos,
    bool CaixaAberto,
    DateTime? CaixaAbertoDesde,
    int ProdutosAbaixoDoMinimo,
    IReadOnlyList<ProdutoEstoqueBaixo> PioresEstoques,
    int ClientesComDivida,
    long DividaTotalCentavos,
    long DividaVencidaCentavos,
    int ClientesComDividaVencida)
{
    /// <summary>Peças vendidas hoje (soma das quantidades dos itens das vendas não canceladas).</summary>
    public int PecasHoje { get; init; }

    /// <summary>Os últimos <see cref="InicioService.DiasDaSemana"/> dias, do mais antigo para hoje (dia sem venda vem zerado).</summary>
    public IReadOnlyList<VendasDoDia> UltimosDias { get; init; } = [];

    /// <summary>Os mais vendidos dos últimos 7 dias, por peças (mesma ordem do Mais vendidos).</summary>
    public IReadOnlyList<ProdutoDaSemana> MaisVendidosSemana { get; init; } = [];

    /// <summary>Contas a pagar em aberto até daqui a <see cref="InicioService.DiasContasAPagar"/> dias (inclui as atrasadas), as mais antigas primeiro.</summary>
    public IReadOnlyList<ContaAVencer> ContasAPagar { get; init; } = [];
    public int ContasAPagarQuantidade { get; init; }
    public long ContasAPagarTotalCentavos { get; init; }
    public long ContasAtrasadasCentavos { get; init; }
}

/// <summary>
/// Junta os indicadores da tela inicial: vendas do dia e dos últimos 7 dias, mais vendidos da semana, caixa,
/// estoque baixo, dívidas de clientes e contas a pagar que vencem logo.
/// Só lê: nenhuma regra nova, ele reaproveita as mesmas definições do resto do programa
/// (estoque baixo = abaixo do mínimo; dívida = cálculo FIFO de <see cref="ContaCorrente"/>).
/// </summary>
public class InicioService
{
    public const int QuantosEstoquesBaixosMostrar = 5;
    public const int DiasDaSemana = 7;
    public const int QuantosMaisVendidosMostrar = 5;
    public const int DiasContasAPagar = 7;
    public const int QuantasContasMostrar = 6;

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public InicioService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<ResumoInicio> ObterAsync()
    {
        var agora = _agora();
        var hoje = agora.Date; // sempre o dia LOCAL, de 00:00 a 23:59
        var amanha = hoje.AddDays(1);
        var ontem = hoje.AddDays(-1);

        await using var db = await _fabrica.CreateDbContextAsync();

        // Vendas de ontem e de hoje (duas datas, uma consulta).
        var vendas = await db.Vendas.AsNoTracking()
            .Where(v => v.DataHora >= ontem && v.DataHora < amanha)
            .Select(v => new { v.DataHora, v.Cancelada, v.TotalCentavos })
            .ToListAsync();
        var hojeValidas = vendas.Where(v => v.DataHora >= hoje && !v.Cancelada).ToList();

        // Últimos 7 dias (hoje incluído): total por dia e o ranking por peças. Vendas canceladas ficam de fora.
        var inicioSemana = hoje.AddDays(-(DiasDaSemana - 1));
        var semana = await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => !v.Cancelada && v.DataHora >= inicioSemana && v.DataHora < amanha)
            .OrderBy(v => v.Id)
            .ToListAsync();
        var porDia = Enumerable.Range(0, DiasDaSemana).Select(i => inicioSemana.AddDays(i))
            .Select(d =>
            {
                var doDia = semana.Where(v => v.DataHora.Date == d).ToList();
                return new VendasDoDia(d, doDia.Count, doDia.Sum(v => v.TotalCentavos));
            })
            .ToList();
        var pecasHoje = semana.Where(v => v.DataHora >= hoje).SelectMany(v => v.Itens).Sum(i => i.Quantidade);
        var linhasSemana = new List<(int ProdutoId, int ItemId, string Nome, int Quantidade, long Liquido)>();
        foreach (var v in semana)
        {
            var itens = v.Itens.OrderBy(i => i.Id).ToList();
            var liquidos = Participacao.LiquidarItens(itens.Select(i => i.TotalCentavos).ToList(), v.DescontoCentavos);
            for (var k = 0; k < itens.Count; k++)
                linhasSemana.Add((itens[k].ProdutoId, itens[k].Id, itens[k].NomeProduto, itens[k].Quantidade, liquidos[k]));
        }
        var maisVendidos = linhasSemana
            .GroupBy(l => l.ProdutoId)
            .Select(g => new ProdutoDaSemana(g.Key, g.OrderByDescending(l => l.ItemId).First().Nome, g.Sum(l => l.Quantidade), g.Sum(l => l.Liquido)))
            .OrderByDescending(p => p.Quantidade).ThenByDescending(p => p.TotalLiquidoCentavos).ThenBy(p => p.Nome)
            .Take(QuantosMaisVendidosMostrar)
            .ToList();

        // Contas a pagar: despesas em aberto (não canceladas) vencidas ou vencendo nos próximos dias. A conta já paga com
        // cheque emitido pendente fica "em compensação": o dinheiro já está comprometido, então não aparece como a pagar.
        var limiteContas = hoje.AddDays(DiasContasAPagar + 1);
        var comCheque = db.ChequesEmitidos.Where(c => c.Situacao == SituacaoChequeEmitido.Pendente).Select(c => c.LancamentoId);
        var contas = await db.Lancamentos.AsNoTracking()
            .Where(l => !l.Cancelado && l.PagoEm == null && l.Tipo == TipoLancamento.Despesa && l.Vencimento < limiteContas
                        && !comCheque.Contains(l.Id))
            .Select(l => new { l.Id, l.Descricao, l.Contraparte, l.Vencimento, l.ValorCentavos })
            .ToListAsync();
        var contasOrdenadas = contas
            .OrderBy(l => l.Vencimento).ThenByDescending(l => l.ValorCentavos).ThenBy(l => l.Id)
            .Select(l => new ContaAVencer(l.Id, l.Descricao, l.Contraparte, l.Vencimento.Date, l.ValorCentavos, l.Vencimento.Date < hoje))
            .ToList();
        var totalHoje = hojeValidas.Sum(v => v.TotalCentavos);
        var ticket = hojeValidas.Count == 0
            ? 0
            : (long)Math.Round(totalHoje / (decimal)hojeValidas.Count, MidpointRounding.AwayFromZero);

        var turno = await db.TurnosCaixa.AsNoTracking().Where(t => t.DataFechamento == null).FirstOrDefaultAsync();

        // Estoque baixo: mesma regra da tela de estoque (EstoqueAtual < EstoqueMinimo). Produto "vender sem baixa"
        // não controla estoque, então não entra no alerta.
        var baixos = await db.Produtos.AsNoTracking()
            .Where(p => p.Ativo && !p.VenderSemBaixa && p.EstoqueAtual < p.EstoqueMinimo)
            .Select(p => new ProdutoEstoqueBaixo(p.Nome, p.EstoqueAtual, p.EstoqueMinimo))
            .ToListAsync();
        var piores = baixos
            .OrderBy(p => p.Estoque - p.Minimo).ThenBy(p => p.Nome)
            .Take(QuantosEstoquesBaixosMostrar)
            .ToList();

        // Dívidas: recalculadas das vendas a prazo e dos pagamentos, como na ficha do cliente.
        var aPrazo = await db.Vendas.AsNoTracking()
            .Where(v => v.FormaPagamento == FormaPagamento.APrazo && !v.Cancelada && v.ClienteId != null)
            .ToListAsync();
        var pagamentos = await db.PagamentosCliente.AsNoTracking().ToListAsync();
        var ajustesTitulos = await db.AjustesTitulosPrazo.AsNoTracking().ToListAsync();
        long dividaTotal = 0, dividaVencida = 0;
        int comDivida = 0, comVencida = 0;
        foreach (var grupo in aPrazo.GroupBy(v => v.ClienteId!.Value))
        {
            var conta = ContaCorrente.Calcular(grupo, pagamentos.Where(p => p.ClienteId == grupo.Key), ajustesTitulos);
            var saldo = conta.SaldoDevedorCentavos;
            if (saldo <= 0) continue;
            comDivida++;
            dividaTotal += saldo;
            var vencida = conta.Titulos.Where(t => t.VencidoEm(agora)).Sum(t => t.SaldoCentavos);
            if (vencida > 0) { comVencida++; dividaVencida += vencida; }
        }

        return new ResumoInicio(
            hoje,
            hojeValidas.Count, totalHoje, ticket,
            vendas.Count(v => v.DataHora >= hoje && v.Cancelada),
            vendas.Where(v => v.DataHora < hoje && !v.Cancelada).Sum(v => v.TotalCentavos),
            turno is not null, turno?.DataAbertura,
            baixos.Count, piores,
            comDivida, dividaTotal, dividaVencida, comVencida)
        {
            PecasHoje = pecasHoje,
            UltimosDias = porDia,
            MaisVendidosSemana = maisVendidos,
            ContasAPagar = contasOrdenadas.Take(QuantasContasMostrar).ToList(),
            ContasAPagarQuantidade = contasOrdenadas.Count,
            ContasAPagarTotalCentavos = contasOrdenadas.Sum(c => c.ValorCentavos),
            ContasAtrasadasCentavos = contasOrdenadas.Where(c => c.Atrasada).Sum(c => c.ValorCentavos)
        };
    }
}
