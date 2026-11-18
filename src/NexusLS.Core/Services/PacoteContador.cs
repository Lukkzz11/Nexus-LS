using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;

namespace NexusLS.Core.Services;

/// <summary>Gera os arquivos que o contador costuma pedir de um período: vendas, itens vendidos e contas a pagar/receber. Só lê o banco.</summary>
public class PacoteContador
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public PacoteContador(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>Grava 3 arquivos CSV na <paramref name="pasta"/> e devolve os caminhos. <paramref name="fim"/> é inclusive.</summary>
    public async Task<List<string>> GerarAsync(DateTime inicio, DateTime fim, string pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta)) throw new RegraDeNegocioException("Escolha a pasta onde salvar os arquivos.");
        var de = inicio.Date;
        var ate = fim.Date.AddDays(1);
        if (ate <= de) throw new RegraDeNegocioException("O período é inválido: o fim vem antes do início.");

        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Cliente).Include(v => v.Itens)
            .Where(v => v.DataHora >= de && v.DataHora < ate).ToListAsync();
        var contas = await db.Lancamentos.AsNoTracking()
            .Where(l => l.Vencimento >= de && l.Vencimento < ate).ToListAsync();

        try { Directory.CreateDirectory(pasta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new RegraDeNegocioException("Não foi possível usar essa pasta: " + ex.Message); }

        var sufixo = de.Day == 1 && ate == de.AddMonths(1) ? de.ToString("yyyy-MM") : $"{de:yyyy-MM-dd}_a_{fim.Date:yyyy-MM-dd}";
        var arquivos = new List<(string Nome, string Conteudo)>
        {
            ($"vendas-{sufixo}.csv", ExportadorCsv.Vendas(vendas)),
            ($"itens-vendidos-{sufixo}.csv", ExportadorCsv.Itens(vendas)),
            ($"contas-{sufixo}.csv", ExportadorCsv.Lancamentos(contas, DateTime.Today)),
        };
        var caminhos = new List<string>();
        try
        {
            foreach (var (nome, conteudo) in arquivos)
            {
                var caminho = Path.Combine(pasta, nome);
                ExportadorCsv.Salvar(caminho, conteudo);
                caminhos.Add(caminho);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new RegraDeNegocioException("Não foi possível gravar os arquivos (algum está aberto no Excel?): " + ex.Message); }
        return caminhos;
    }
}
