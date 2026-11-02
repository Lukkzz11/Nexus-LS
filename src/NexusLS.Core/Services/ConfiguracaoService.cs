using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosLoja(
    string NomeLoja,
    string? Documento = null,
    string? Endereco = null,
    string? Telefone = null,
    string? MensagemCupom = null,
    string? ImpressoraCupom = null,
    int LarguraCupom = 42,
    bool ImprimirCupomAoFinalizar = true,
    string NomeOperador = "Caixa",
    bool PerguntarTamanho = true,
    bool AbrirNoPdv = false,
    string? RazaoSocial = null,
    string? InscricaoEstadual = null,
    string? Email = null,
    string? CidadeUf = null,
    string? ChavePix = null,
    bool CupomMostrarRazaoSocial = true,
    bool CupomMostrarDocumento = true,
    bool CupomMostrarEndereco = true,
    bool CupomMostrarTelefone = true,
    bool CupomMostrarPix = false,
    int CupomLinhasFinais = 1,
    long FundoTrocoPadraoCentavos = 0,
    int EstoqueMinimoPadrao = 0,
    int MargemPadraoPercentual = 0);

/// <summary>Dados da loja e do cupom. Só existe uma configuração; ela é criada na primeira leitura.</summary>
public class ConfiguracaoService
{
    public const int LarguraMinima = 24;
    public const int LarguraMaxima = 80;

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
        if (dados.LarguraCupom < LarguraMinima || dados.LarguraCupom > LarguraMaxima)
            throw new RegraDeNegocioException($"A largura do cupom deve ficar entre {LarguraMinima} e {LarguraMaxima} caracteres.");
        if (dados.CupomLinhasFinais is < 0 or > 15)
            throw new RegraDeNegocioException("As linhas em branco no fim do cupom devem ficar entre 0 e 15.");
        if (dados.FundoTrocoPadraoCentavos < 0)
            throw new RegraDeNegocioException("O troco inicial padrão não pode ser negativo.");
        if (dados.EstoqueMinimoPadrao < 0)
            throw new RegraDeNegocioException("O estoque mínimo padrão não pode ser negativo.");
        if (dados.MargemPadraoPercentual is < 0 or >= 100)
            throw new RegraDeNegocioException("A margem padrão deve ficar entre 0% e 99%.");
        if (!string.IsNullOrWhiteSpace(dados.Email) && !dados.Email.Contains('@'))
            throw new RegraDeNegocioException("O e-mail da loja parece errado (falta o @).");

        await ObterAsync(); // garante que a linha existe
        await using var db = await _fabrica.CreateDbContextAsync();
        var config = await db.ConfiguracoesLoja.FirstAsync(c => c.Id == 1);

        config.NomeLoja = dados.NomeLoja.Trim();
        config.Documento = Limpar(dados.Documento);
        config.Endereco = Limpar(dados.Endereco);
        config.Telefone = Limpar(dados.Telefone);
        config.MensagemCupom = Limpar(dados.MensagemCupom);
        config.ImpressoraCupom = Limpar(dados.ImpressoraCupom);
        config.LarguraCupom = dados.LarguraCupom;
        config.ImprimirCupomAoFinalizar = dados.ImprimirCupomAoFinalizar;
        config.PerguntarTamanho = dados.PerguntarTamanho;
        config.AbrirNoPdv = dados.AbrirNoPdv;
        config.RazaoSocial = Limpar(dados.RazaoSocial);
        config.InscricaoEstadual = Limpar(dados.InscricaoEstadual);
        config.Email = Limpar(dados.Email);
        config.CidadeUf = Limpar(dados.CidadeUf);
        config.ChavePix = Limpar(dados.ChavePix);
        config.CupomMostrarRazaoSocial = dados.CupomMostrarRazaoSocial;
        config.CupomMostrarDocumento = dados.CupomMostrarDocumento;
        config.CupomMostrarEndereco = dados.CupomMostrarEndereco;
        config.CupomMostrarTelefone = dados.CupomMostrarTelefone;
        config.CupomMostrarPix = dados.CupomMostrarPix;
        config.CupomLinhasFinais = dados.CupomLinhasFinais;
        config.FundoTrocoPadraoCentavos = dados.FundoTrocoPadraoCentavos;
        config.EstoqueMinimoPadrao = dados.EstoqueMinimoPadrao;
        config.MargemPadraoPercentual = dados.MargemPadraoPercentual;
        config.NomeOperador = string.IsNullOrWhiteSpace(dados.NomeOperador) ? "Caixa" : dados.NomeOperador.Trim();

        await db.SaveChangesAsync();
        return config;
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
