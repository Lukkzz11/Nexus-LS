using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Relatório financeiro (entradas, saídas e saldo) de um período. Funções puras sobre as listas (testáveis); a leitura do
/// banco fica em <see cref="RelatorioFinanceiroService"/>.
/// Regras: vendas canceladas ficam fora. Venda enviada a Pedidos CONTINUA contando (o pedido é só acompanhamento; a venda
/// já existe). Entradas = vendas + entradas lançadas; Saídas = saídas lançadas; Saldo = Entradas - Saídas. Devoluções
/// (vale-troca) são mostradas à parte e dão o faturamento líquido; não mexem no saldo (não saiu dinheiro, saiu um vale).
/// </summary>
public static class RelatorioFinanceiro
{
    /// <summary>Venda paga com vale-troca aparece na linha "Vale-troca"; assim a soma das formas fecha com o faturamento.</summary>
    public sealed record PorForma(FormaPagamento? Forma, long VendasCentavos, long EntradasCentavos, long SaidasCentavos)
    {
        public string Nome => Forma is FormaPagamento f ? SelecaoPagamento.Nome(f) : "Sem forma informada";
    }

    public sealed record PorDia(DateTime Dia, long VendasCentavos, long EntradasLancadasCentavos, long SaidasCentavos,
        long SaldoDoDiaCentavos, long SaldoAcumuladoCentavos);

    public sealed record PorCategoria(string Categoria, long ValorCentavos, int Lancamentos);

    public sealed record Resultado(
        DesempenhoVendas.Intervalo Periodo,
        int NumeroDeVendas, long VendasCentavos,
        long DevolucoesCentavos, long FaturamentoLiquidoCentavos,
        long EntradasLancadasCentavos, long SaidasLancadasCentavos,
        long TotalEntradasCentavos, long TotalSaidasCentavos, long SaldoCentavos,
        IReadOnlyList<PorForma> PorFormaPagamento,
        IReadOnlyList<PorDia> PorDiaDoPeriodo,
        IReadOnlyList<PorCategoria> EntradasPorCategoria,
        IReadOnlyList<PorCategoria> SaidasPorCategoria);

    /// <param name="devolucoes">Opcional: devoluções (trocas) do período; as canceladas são ignoradas.</param>
    public static Resultado Gerar(IEnumerable<Venda> vendas, IEnumerable<LancamentoEmpresa> lancamentos,
        IEnumerable<Devolucao>? devolucoes, DesempenhoVendas.Intervalo periodo)
    {
        var ini = periodo.Inicio.Date;
        var fim = periodo.Fim.Date;
        bool Dentro(DateTime d) => d.Date >= ini && d.Date <= fim;

        var vs = vendas.Where(v => !v.Cancelada && Dentro(v.DataHora)).ToList();
        var ls = lancamentos.Where(l => Dentro(l.Data)).ToList();
        var ds = (devolucoes ?? Array.Empty<Devolucao>()).Where(d => !d.Cancelada && Dentro(d.DataHora)).ToList();

        long vendasTotal = vs.Sum(v => v.TotalCentavos);
        long devolvido = ds.Sum(d => d.ValorCentavos);
        long entradasL = ls.Where(l => l.Tipo == TipoMovimentoEmpresa.Entrada).Sum(l => l.ValorCentavos);
        long saidasL = ls.Where(l => l.Tipo == TipoMovimentoEmpresa.Saida).Sum(l => l.ValorCentavos);

        // Por forma: o valor pago vai para a forma da venda; a parte paga com vale-troca, para "Vale-troca".
        var formas = new Dictionary<int, (long V, long E, long S)>();
        void Soma(FormaPagamento? f, long v, long e, long s)
        {
            var chave = f is FormaPagamento ff ? (int)ff : -1;
            formas.TryGetValue(chave, out var x);
            formas[chave] = (x.V + v, x.E + e, x.S + s);
        }
        foreach (var v in vs)
        {
            if (v.FormaPagamento == FormaPagamento.ValeTroca) { Soma(FormaPagamento.ValeTroca, v.TotalCentavos, 0, 0); continue; }
            var vale = Math.Clamp(v.ValeTrocaCentavos, 0, v.TotalCentavos);
            Soma(v.FormaPagamento, v.TotalCentavos - vale, 0, 0);
            if (vale > 0) Soma(FormaPagamento.ValeTroca, vale, 0, 0);
        }
        foreach (var l in ls)
            Soma(l.Forma, 0, l.Tipo == TipoMovimentoEmpresa.Entrada ? l.ValorCentavos : 0, l.Tipo == TipoMovimentoEmpresa.Saida ? l.ValorCentavos : 0);
        var porForma = formas
            .Select(k => new PorForma(k.Key < 0 ? null : (FormaPagamento)k.Key, k.Value.V, k.Value.E, k.Value.S))
            .OrderByDescending(f => f.VendasCentavos + f.EntradasCentavos).ThenBy(f => f.Forma is null ? 99 : (int)f.Forma).ToList();

        // Por dia (todos os dias do período, inclusive os zerados), com saldo acumulado.
        var dias = new List<PorDia>();
        long acumulado = 0;
        for (var d = ini; d <= fim; d = d.AddDays(1))
        {
            long dv = vs.Where(v => v.DataHora.Date == d).Sum(v => v.TotalCentavos);
            long de = ls.Where(l => l.Data.Date == d && l.Tipo == TipoMovimentoEmpresa.Entrada).Sum(l => l.ValorCentavos);
            long dsd = ls.Where(l => l.Data.Date == d && l.Tipo == TipoMovimentoEmpresa.Saida).Sum(l => l.ValorCentavos);
            var saldoDia = dv + de - dsd;
            acumulado += saldoDia;
            dias.Add(new PorDia(d, dv, de, dsd, saldoDia, acumulado));
        }

        return new Resultado(periodo, vs.Count, vendasTotal, devolvido, vendasTotal - devolvido, entradasL, saidasL,
            vendasTotal + entradasL, saidasL, vendasTotal + entradasL - saidasL,
            porForma, dias,
            Categorias(ls, TipoMovimentoEmpresa.Entrada), Categorias(ls, TipoMovimentoEmpresa.Saida));
    }

    private static List<PorCategoria> Categorias(List<LancamentoEmpresa> ls, TipoMovimentoEmpresa tipo) => ls
        .Where(l => l.Tipo == tipo)
        .GroupBy(l => ResumoVendas.Normalizar(l.Categoria))
        .Select(g => new PorCategoria(g.First().Categoria.Trim(), g.Sum(l => l.ValorCentavos), g.Count()))
        .OrderByDescending(c => c.ValorCentavos).ThenBy(c => c.Categoria, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>CSV em blocos (Seção;Item;Entradas;Saídas;Valor) para o Excel brasileiro.</summary>
    public static string ParaCsv(Resultado r)
    {
        var c = new CsvRelatorios();
        c.Linha("Seção", "Item", "Vendas", "Entradas", "Saídas", "Saldo");
        c.Linha("Período", $"{CsvRelatorios.Data(r.Periodo.Inicio)} a {CsvRelatorios.Data(r.Periodo.Fim)}", "", "", "", "");
        c.Linha("Resumo", "Vendas (faturamento)", CsvRelatorios.Valor(r.VendasCentavos), "", "", "");
        c.Linha("Resumo", "Número de vendas", CsvRelatorios.N(r.NumeroDeVendas), "", "", "");
        c.Linha("Resumo", "Devoluções (vale-troca)", CsvRelatorios.Valor(r.DevolucoesCentavos), "", "", "");
        c.Linha("Resumo", "Faturamento líquido", CsvRelatorios.Valor(r.FaturamentoLiquidoCentavos), "", "", "");
        c.Linha("Resumo", "Entradas lançadas", "", CsvRelatorios.Valor(r.EntradasLancadasCentavos), "", "");
        c.Linha("Resumo", "Saídas lançadas", "", "", CsvRelatorios.Valor(r.SaidasLancadasCentavos), "");
        c.Linha("Resumo", "Total", "", CsvRelatorios.Valor(r.TotalEntradasCentavos), CsvRelatorios.Valor(r.TotalSaidasCentavos), CsvRelatorios.Valor(r.SaldoCentavos));
        foreach (var f in r.PorFormaPagamento)
            c.Linha("Forma de pagamento", f.Nome, CsvRelatorios.Valor(f.VendasCentavos), CsvRelatorios.Valor(f.EntradasCentavos), CsvRelatorios.Valor(f.SaidasCentavos), "");
        foreach (var d in r.PorDiaDoPeriodo)
            c.Linha("Por dia", CsvRelatorios.Data(d.Dia), CsvRelatorios.Valor(d.VendasCentavos), CsvRelatorios.Valor(d.EntradasLancadasCentavos),
                CsvRelatorios.Valor(d.SaidasCentavos), CsvRelatorios.Valor(d.SaldoAcumuladoCentavos));
        foreach (var x in r.EntradasPorCategoria) c.Linha("Entradas por categoria", x.Categoria, "", CsvRelatorios.Valor(x.ValorCentavos), "", "");
        foreach (var x in r.SaidasPorCategoria) c.Linha("Saídas por categoria", x.Categoria, "", "", CsvRelatorios.Valor(x.ValorCentavos), "");
        return c.ToString();
    }
}

/// <summary>Lê vendas, lançamentos da empresa e devoluções do banco e entrega o relatório financeiro.</summary>
public class RelatorioFinanceiroService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public RelatorioFinanceiroService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    public async Task<RelatorioFinanceiro.Resultado> GerarAsync(DesempenhoVendas.Intervalo periodo)
    {
        var de = periodo.Inicio.Date;
        var ate = periodo.Fim.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync().ConfigureAwait(false);
        var vendas = await db.Vendas.AsNoTracking().Where(v => v.DataHora >= de && v.DataHora < ate && !v.Cancelada).ToListAsync().ConfigureAwait(false);
        var lancs = await db.LancamentosEmpresa.AsNoTracking().Where(l => l.Data >= de && l.Data < ate).ToListAsync().ConfigureAwait(false);
        var devs = await db.Devolucoes.AsNoTracking().Where(d => d.DataHora >= de && d.DataHora < ate && !d.Cancelada).ToListAsync().ConfigureAwait(false);
        return RelatorioFinanceiro.Gerar(vendas, lancs, devs, periodo);
    }
}
