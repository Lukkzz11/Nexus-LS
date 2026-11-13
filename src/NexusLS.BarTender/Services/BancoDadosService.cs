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
        CaminhoAtual = ResolverCaminhoInicial(caminhoPersonalizado);
    }

    /// <summary>Caminho explícito (testes) vence; depois a última escolha do operador; depois o padrão.</summary>
    private static string ResolverCaminhoInicial(string? caminhoPersonalizado)
    {
        if (!string.IsNullOrWhiteSpace(caminhoPersonalizado))
            return caminhoPersonalizado;

        var preferencias = PreferenciasBarTender.Carregar();
        if (!string.IsNullOrWhiteSpace(preferencias.CaminhoBanco) && File.Exists(preferencias.CaminhoBanco))
            return preferencias.CaminhoBanco!;

        return CaminhoBanco.Padrao();
    }

    public void DefinirCaminho(string novoCaminho)
    {
        if (string.IsNullOrWhiteSpace(novoCaminho))
            throw new ArgumentException("Caminho do banco de dados não pode ser vazio.", nameof(novoCaminho));

        CaminhoAtual = novoCaminho;

        // Guarda a escolha: na próxima abertura o BarTender já abre no banco certo.
        var preferencias = PreferenciasBarTender.Carregar();
        preferencias.CaminhoBanco = novoCaminho;
        preferencias.Salvar();
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

        // A sequência de cada tipo é lida do próprio espaço de códigos, não de outro:
        // EAN-13 usa os "2..." de 13 dígitos e o Code 128 usa os "7..." de 11 dígitos.
        var existentes = await db.Produtos
            .AsNoTracking()
            .Where(p => p.CodigoBarras != null && p.CodigoBarras != "")
            .Select(p => p.CodigoBarras!)
            .ToListAsync();

        var usados = new HashSet<string>(existentes, StringComparer.Ordinal);

        var maiorEan = existentes
            .Where(c => c.Length == 13 && c.StartsWith("2", StringComparison.Ordinal))
            .Select(CodigoBarrasInterno.SequenciaDe)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var maiorCode128 = existentes
            .Where(c => c.Length == 11 && c[0] == '7' && c.Skip(1).All(char.IsDigit))
            .Select(c => long.TryParse(c.AsSpan(1), out var numero) ? numero : 0)
            .DefaultIfEmpty(0)
            .Max();

        // Backup do banco antes de escrever em lote: se algo falhar, o arquivo anterior está a salvo.
        var backup = await FazerBackupAsync();

        var alterados = 0;
        await using var transacao = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var prod in produtos)
            {
                string codigoGerado;

                if (tipo == TipoCodigoBarras.Ean13)
                {
                    do
                    {
                        maiorEan++;
                        codigoGerado = CodigoBarrasInterno.Montar(maiorEan);
                    }
                    while (!usados.Add(codigoGerado));
                }
                else
                {
                    do
                    {
                        maiorCode128++;
                        codigoGerado = "7" + maiorCode128.ToString("D10");
                    }
                    while (!usados.Add(codigoGerado));
                }

                prod.CodigoBarras = codigoGerado;
                prod.AtualizadoEm = DateTime.UtcNow;
                alterados++;
                progresso?.Report(alterados);
            }

            await db.SaveChangesAsync();
            await transacao.CommitAsync();
            return alterados;
        }
        catch (DbUpdateException ex)
        {
            await transacao.RollbackAsync();
            throw new InvalidOperationException(
                $"Nada foi gravado: um código gerado já existia no banco. " +
                $"O backup anterior está em {backup ?? "pasta de backups do banco"}.", ex);
        }
    }

    /// <summary>
    /// Cópia consistente do banco (VACUUM INTO, que o SQLite garante mesmo com o arquivo aberto).
    /// Devolve o caminho do backup ou nulo quando ainda não existe banco.
    /// </summary>
    public async Task<string?> FazerBackupAsync()
    {
        if (!BancoExiste()) return null;

        var pastaBanco = Path.GetDirectoryName(CaminhoAtual)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pastaBackup = Path.Combine(pastaBanco, "Backups");
        Directory.CreateDirectory(pastaBackup);

        var nome = Path.GetFileNameWithoutExtension(CaminhoAtual);
        var destino = Path.Combine(pastaBackup, $"{nome}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");

        await using var db = CriarContexto();
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", destino);
        return destino;
    }
}
