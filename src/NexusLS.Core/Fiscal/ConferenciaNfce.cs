using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;

namespace NexusLS.Core.Fiscal;

/// <summary>
/// Pega a última venda gravada e monta o XML de conferência da NFC-e. Só lê o banco: não grava nada, não assina, não envia.
/// O número da nota é calculado por consulta (posição entre as vendas não canceladas), então não existe coluna nem contador guardado.
/// </summary>
public class ConferenciaNfce
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    public ConferenciaNfce(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    public async Task<ResultadoNfce> ConferirUltimaVendaAsync(ConfiguracaoSeguranca fiscal)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var venda = await db.Vendas.AsNoTracking().Include(v => v.Itens).OrderByDescending(v => v.Id).FirstOrDefaultAsync();
        return await ConferirAsync(db, venda, fiscal);
    }

    public async Task<ResultadoNfce> ConferirVendaAsync(int vendaId, ConfiguracaoSeguranca fiscal)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var venda = await db.Vendas.AsNoTracking().Include(v => v.Itens).FirstOrDefaultAsync(v => v.Id == vendaId);
        return await ConferirAsync(db, venda, fiscal);
    }

    private static async Task<ResultadoNfce> ConferirAsync(PdvDbContext db, Venda? venda, ConfiguracaoSeguranca fiscal)
    {
        var vazio = new EmitenteNfce(null, null, null, null, null, null, null, null, null, null, null);
        if (venda is null)
            return MontadorNfce.Montar(null, vazio, Converter(fiscal), new Dictionary<int, Produto>(), Array.Empty<int>());
        var loja = await db.ConfiguracoesLoja.AsNoTracking().FirstOrDefaultAsync() ?? new ConfiguracaoLoja();
        var ids = venda.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var validas = await db.Vendas.AsNoTracking().Where(v => !v.Cancelada && v.Id <= venda.Id).Select(v => v.Id).ToListAsync();
        return MontadorNfce.Montar(venda, EmitenteNfce.De(loja), Converter(fiscal), produtos, validas);
    }

    public static ConfigNfce Converter(ConfiguracaoSeguranca c) =>
        new(c.FiscalRegime, c.FiscalAmbiente, c.FiscalSerie, c.FiscalCscId, c.FiscalNcmPadrao, c.FiscalCfopPadrao);

    /// <summary>Nome sugerido para salvar: deixa claro que não vale como nota.</summary>
    public static string NomeArquivo(ResultadoNfce r) =>
        $"SEM-VALIDADE-FISCAL_conferencia_NFCe_{(r.Chave ?? "sem-chave")}.xml";
}
