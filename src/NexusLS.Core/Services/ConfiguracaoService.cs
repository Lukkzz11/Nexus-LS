using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosLoja(
    string NomeLoja,
    string? Documento = null,
    string? Endereco = null,
    string? Telefone = null,
    string? MensagemCupom = null);

/// <summary>Dados da loja e do cupom. Só existe uma configuração; ela é criada na primeira leitura.</summary>
public class ConfiguracaoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public ConfiguracaoService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    public async Task<ConfiguracaoLoja> ObterAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var config = await db.ConfiguracoesLoja.AsNoTracking().FirstOrDefaultAsync(c => c.Id == 1);
        if (config is not null) return config;

        config = new ConfiguracaoLoja();
        db.ConfiguracoesLoja.Add(config);
        await db.SaveChangesAsync();
        return config;
    }

    public async Task<ConfiguracaoLoja> SalvarAsync(DadosLoja dados)
    {
        if (string.IsNullOrWhiteSpace(dados.NomeLoja))
            throw new RegraDeNegocioException("Informe o nome da loja.");

        await ObterAsync(); // garante que a linha existe
        await using var db = await _fabrica.CreateDbContextAsync();
        var config = await db.ConfiguracoesLoja.FirstAsync(c => c.Id == 1);

        config.NomeLoja = dados.NomeLoja.Trim();
        config.Documento = Limpar(dados.Documento);
        config.Endereco = Limpar(dados.Endereco);
        config.Telefone = Limpar(dados.Telefone);
        config.MensagemCupom = Limpar(dados.MensagemCupom);

        await db.SaveChangesAsync();
        return config;
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
