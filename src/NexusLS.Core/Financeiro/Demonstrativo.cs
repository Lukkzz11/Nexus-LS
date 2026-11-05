using NexusLS.Core.Services;

namespace NexusLS.Core.Financeiro;

/// <summary>
/// DRE / demonstrativo do período. Não calcula nada por conta própria: tudo vem do <see cref="PeriodoFin"/> do
/// <see cref="MotorFinanceiro"/> (a mesma fonte do Financeiro e de Contas e saldos). O que NÃO é receita nem despesa
/// (compra de mercadoria, recebimento de dívida, aporte, retiradas...) aparece em blocos separados, fora do resultado.
/// </summary>
public record DemonstrativoFinanceiro(
    DateTime De, DateTime Ate,
    long ReceitaBrutaCentavos, long DevolucoesCentavos, long ReceitaLiquidaCentavos,
    long CustoMercadoriasCentavos, long LucroBrutoCentavos, double MargemBrutaPercentual,
    long OutrasReceitasCentavos, long DespesasOperacionaisCentavos, long ResultadoCentavos,
    IReadOnlyList<ResumoCategoriaFinanceira> DespesasPorCategoria,
    // Dinheiro que entrou ou saiu mas NÃO é receita nem despesa
    long RecebimentosDeDividasCentavos, long AportesCentavos, long OutrasEntradasCentavos,
    long ComprasMercadoriaCentavos, long RetiradasProprietarioCentavos, long OutrasSaidasCentavos,
    long RetiradasCaixaCentavos, long ReforcosCaixaCentavos,
    // Fluxo de caixa realizado
    long EntradasRealizadasCentavos, long SaidasRealizadasCentavos,
    // Vendas
    int QuantidadeVendas = 0, long DescontosCentavos = 0, long TicketMedioCentavos = 0,
    int PecasSemCusto = 0,
    IReadOnlyList<ResumoForma>? VendasPorForma = null,
    IReadOnlyList<string>? Avisos = null)
{
    public long FluxoCaixaCentavos => EntradasRealizadasCentavos - SaidasRealizadasCentavos;

    /// <summary>Entradas que mexem no saldo mas não são receita.</summary>
    public long EntradasForaDoResultadoCentavos => RecebimentosDeDividasCentavos + AportesCentavos + OutrasEntradasCentavos + ReforcosCaixaCentavos;

    /// <summary>Saídas que mexem no saldo mas não são despesa.</summary>
    public long SaidasForaDoResultadoCentavos => ComprasMercadoriaCentavos + RetiradasProprietarioCentavos + OutrasSaidasCentavos + RetiradasCaixaCentavos;
}

public static class Demonstrativo
{
    /// <summary>Monta o demonstrativo a partir do período do motor. Puro: sem banco.</summary>
    public static DemonstrativoFinanceiro Montar(PeriodoFin p, IReadOnlyList<AvisoFin>? avisosDoMotor = null,
        int quantidadeVendas = 0, long descontosCentavos = 0, int pecasSemCusto = 0, IReadOnlyList<ResumoForma>? vendasPorForma = null)
    {
        var margem = p.ReceitaLiquidaCentavos > 0 ? 100.0 * p.LucroBrutoCentavos / p.ReceitaLiquidaCentavos : 0;
        var categorias = p.DespesasPorCategoria
            .Select(kv => new ResumoCategoriaFinanceira(kv.Key, kv.Value))
            .OrderByDescending(c => c.TotalCentavos).ThenBy(c => c.Categoria).ToList();

        var avisos = new List<string>();
        if (pecasSemCusto > 0)
            avisos.Add($"{pecasSemCusto} peça(s) vendida(s) sem custo cadastrado: o resultado está ANTES do custo dessas peças. Cadastre o custo no produto.");
        if (avisosDoMotor is not null) avisos.AddRange(avisosDoMotor.Select(a => a.Mensagem));
        avisos.Add("Impostos sobre vendas não são calculados: só entram os lançados como despesa.");

        var ticket = quantidadeVendas == 0 ? 0 : (long)Math.Round(p.ReceitaBrutaCentavos / (decimal)quantidadeVendas, MidpointRounding.AwayFromZero);

        return new DemonstrativoFinanceiro(
            p.Inicio, p.Fim,
            p.ReceitaBrutaCentavos, p.DevolucoesCentavos, p.ReceitaLiquidaCentavos,
            p.CustoMercadoriasCentavos, p.LucroBrutoCentavos, margem,
            p.OutrasReceitasCentavos, p.DespesasOperacionaisCentavos, p.ResultadoCentavos, categorias,
            p.RecebimentosClientesCentavos, p.AportesCentavos, p.OutrasEntradasCentavos,
            p.ComprasMercadoriaCentavos, p.RetiradasProprietarioCentavos, p.OutrasSaidasCentavos,
            p.RetiradasCaixaCentavos, p.ReforcosCaixaCentavos,
            p.EntradasRealizadasCentavos, p.SaidasRealizadasCentavos,
            quantidadeVendas, descontosCentavos, ticket, pecasSemCusto,
            vendasPorForma ?? Array.Empty<ResumoForma>(), avisos);
    }
}
