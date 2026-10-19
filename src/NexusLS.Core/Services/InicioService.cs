using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Um produto abaixo do estoque mínimo, para a lista de alerta da tela inicial.</summary>
public record ProdutoEstoqueBaixo(string Nome, int Estoque, int Minimo);

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
    int ClientesComDividaVencida);

/// <summary>
/// Junta, numa só consulta, os indicadores do dia: vendas, caixa, estoque baixo e dívidas de clientes.
/// Só lê: nenhuma regra nova, ele reaproveita as mesmas definições do resto do programa
/// (estoque baixo = abaixo do mínimo; dívida = cálculo FIFO de <see cref="ContaCorrente"/>).
/// </summary>
public class InicioService
{
    public const int QuantosEstoquesBaixosMostrar = 5;

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
        long dividaTotal = 0, dividaVencida = 0;
        int comDivida = 0, comVencida = 0;
        foreach (var grupo in aPrazo.GroupBy(v => v.ClienteId!.Value))
        {
            var conta = ContaCorrente.Calcular(grupo, pagamentos.Where(p => p.ClienteId == grupo.Key));
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
            comDivida, dividaTotal, dividaVencida, comVencida);
    }
}
