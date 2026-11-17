using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;

namespace NexusLS.Core.Services;

/// <summary>A comissão de um vendedor no período. A base é a venda líquida: itens menos desconto (sem o acréscimo do cartão).</summary>
public record LinhaComissao(string Vendedor, int Vendas, long VendidoCentavos, long DevolvidoCentavos, decimal Percentual)
{
    public long BaseCentavos => Math.Max(0, VendidoCentavos - DevolvidoCentavos);
    public long ComissaoCentavos => (long)Math.Round(BaseCentavos * Percentual / 100m, MidpointRounding.AwayFromZero);
}

public static class ComissaoCalculo
{
    public const string SemVendedor = "(sem vendedor)";

    public static string Nome(string? operador) => string.IsNullOrWhiteSpace(operador) ? SemVendedor : operador.Trim();

    /// <summary>
    /// Agrupa por vendedor. <paramref name="devolucoes"/> são pares (vendedor da venda original, valor devolvido).
    /// Venda cancelada não entra. Vendedor só com devolução aparece com base zero (a devolução nunca deixa a base negativa).
    /// </summary>
    public static List<LinhaComissao> Calcular(IEnumerable<Venda> vendas, IEnumerable<(string? Vendedor, long ValorCentavos)> devolucoes,
        ConfiguracaoSeguranca cfg)
    {
        var vendido = vendas.Where(v => !v.Cancelada)
            .GroupBy(v => Nome(v.Operador), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Qtd: g.Count(), Valor: g.Sum(v => v.TotalCentavos - v.AcrescimoCentavos)), StringComparer.OrdinalIgnoreCase);
        var devolvido = cfg.ComissaoDescontaDevolucoes
            ? devolucoes.GroupBy(d => Nome(d.Vendedor), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(d => d.ValorCentavos), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        return vendido.Keys.Union(devolvido.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(nome => new LinhaComissao(
                nome,
                vendido.TryGetValue(nome, out var v) ? v.Qtd : 0,
                vendido.TryGetValue(nome, out v) ? v.Valor : 0,
                devolvido.TryGetValue(nome, out var d) ? d : 0,
                cfg.ComissaoPorVendedor.TryGetValue(nome, out var p) ? p : cfg.ComissaoPadraoPercent))
            .OrderByDescending(l => l.ComissaoCentavos).ThenBy(l => l.Vendedor, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>Lê as vendas e devoluções do período e calcula a comissão. Só lê: não grava nada.</summary>
public class ComissaoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public ComissaoService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <param name="inicio">Primeiro dia (inclusive).</param>
    /// <param name="fim">Último dia (inclusive).</param>
    public async Task<List<LinhaComissao>> CalcularAsync(DateTime inicio, DateTime fim, ConfiguracaoSeguranca cfg)
    {
        var de = inicio.Date;
        var ate = fim.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking().Where(v => v.DataHora >= de && v.DataHora < ate && !v.Cancelada).ToListAsync();
        var devolucoes = new List<(string? Vendedor, long ValorCentavos)>();
        if (cfg.ComissaoDescontaDevolucoes)
        {
            var lidas = await db.Devolucoes.AsNoTracking().Include(d => d.Venda)
                .Where(d => d.DataHora >= de && d.DataHora < ate && !d.Cancelada)
                .Select(d => new { Vendedor = d.Venda!.Operador, d.ValorCentavos }).ToListAsync();
            devolucoes.AddRange(lidas.Select(d => (d.Vendedor, d.ValorCentavos)));
        }
        return ComissaoCalculo.Calcular(vendas, devolucoes, cfg);
    }
}
