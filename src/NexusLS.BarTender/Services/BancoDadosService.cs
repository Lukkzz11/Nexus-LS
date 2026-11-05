using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.BarTender.Models;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Services;

public class BancoDadosService
{
    public string CaminhoAtual { get; private set; }

    public BancoDadosService(string? caminhoPersonalizado = null)
    {
        CaminhoAtual = !string.IsNullOrWhiteSpace(caminhoPersonalizado)
            ? caminhoPersonalizado
            : CaminhoBanco.Padrao();
    }

    public void DefinirCaminho(string novoCaminho)
    {
        if (string.IsNullOrWhiteSpace(novoCaminho))
            throw new ArgumentException("Caminho do banco de dados não pode ser vazio.", nameof(novoCaminho));

        CaminhoAtual = novoCaminho;
    }

    public bool BancoExiste() => File.Exists(CaminhoAtual);

    public PdvDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<PdvDbContext>()
            .UseSqlite($"Data Source={CaminhoAtual};Default Timeout=30")
            .AddInterceptors(new FilaSincronizacaoInterceptor(() => null))
            .Options;

        return new PdvDbContext(options);
    }

    public async Task<List<Produto>> ObterProdutosAsync()
    {
        if (!BancoExiste())
            return new List<Produto>();

        await using var db = CriarContexto();
        return await db.Produtos
            .AsNoTracking()
            .Include(p => p.Tamanhos)
            .Where(p => p.Ativo)
            .OrderBy(p => p.Nome)
            .ToListAsync();
    }

    public async Task<ConfiguracaoLoja?> ObterConfiguracaoLojaAsync()
    {
        if (!BancoExiste())
            return null;

        try
        {
            await using var db = CriarContexto();
            return await db.ConfiguracoesLoja.AsNoTracking().FirstOrDefaultAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GerarProximoCodigoEan13Async()
    {
        await using var db = CriarContexto();
        var existentes = await db.Produtos
            .AsNoTracking()
            .Where(p => p.CodigoBarras != null && p.CodigoBarras.StartsWith("2") && p.CodigoBarras.Length == 13)
            .Select(p => p.CodigoBarras!)
            .ToListAsync();

        var maior = existentes
            .Select(CodigoBarrasInterno.SequenciaDe)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return CodigoBarrasInterno.Montar(maior + 1);
    }

    public async Task<bool> SalvarCodigoBarrasAsync(int produtoId, string novoCodigo)
    {
        await using var db = CriarContexto();
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId);
        if (produto == null) return false;

        produto.CodigoBarras = string.IsNullOrWhiteSpace(novoCodigo) ? null : novoCodigo.Trim();
        produto.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> GerarCodigosLoteAsync(
        IEnumerable<int> produtoIds,
        TipoCodigoBarras tipo,
        IProgress<int>? progresso = null)
    {
        var ids = produtoIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        await using var db = CriarContexto();
        var produtos = await db.Produtos
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        if (produtos.Count == 0) return 0;

        // Se for EAN-13, buscamos a maior sequência existente com prefixo 2
        var existentes = await db.Produtos
            .AsNoTracking()
            .Where(p => p.CodigoBarras != null && p.CodigoBarras.StartsWith("2") && p.CodigoBarras.Length == 13)
            .Select(p => p.CodigoBarras!)
            .ToListAsync();

        var maiorSequencia = existentes
            .Select(CodigoBarrasInterno.SequenciaDe)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var alterados = 0;
        foreach (var prod in produtos)
        {
            string codigoGerado;
            if (tipo == TipoCodigoBarras.Ean13)
            {
                maiorSequencia++;
                codigoGerado = CodigoBarrasInterno.Montar(maiorSequencia);
            }
            else
            {
                // Code 128 numérico sequencial formatado
                maiorSequencia++;
                codigoGerado = "7" + maiorSequencia.ToString("D10");
            }

            prod.CodigoBarras = codigoGerado;
            prod.AtualizadoEm = DateTime.UtcNow;
            alterados++;
            progresso?.Report(alterados);
        }

        await db.SaveChangesAsync();
        return alterados;
    }
}
