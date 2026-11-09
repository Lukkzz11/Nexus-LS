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
    int MargemPadraoPercentual = 0,
    bool CupomMostrarCidade = true,
    bool CupomMostrarInscricao = true,
    bool CupomMostrarEmail = false,
    bool CupomMostrarOperador = true,
    bool CupomMostrarCliente = true,
    bool CupomMostrarPagamento = true,
    bool CupomMostrarAssinatura = false,
    string? CupomMensagemCabecalho = null,
    string? Segmento = null,
    string? EnderecoNumero = null,
    string? Bairro = null,
    string? Cep = null,
    bool CupomMostrarLogo = true,
    byte[]? LogoCupom = null,
    bool LogoAlterado = false,
    bool CupomMostrarReferencia = false,
    bool CupomTituloEmCaixa = false,
    bool CupomMostrarAssinaturaCliente = false,
    string? CupomTextoGarantia = null);

/// <summary>Dados da loja e do cupom. Só existe uma configuração; ela é criada na primeira leitura.</summary>
public class ConfiguracaoService
{
    public const int LarguraMinima = 24;
    public const int LarguraMaxima = 80;
    public const int MensagemCabecalhoMaxima = 200;
    public const int LogoMaximoBytes = 300 * 1024;

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
        if ((dados.CupomMensagemCabecalho?.Length ?? 0) > MensagemCabecalhoMaxima)
            throw new RegraDeNegocioException($"A mensagem do cabeçalho do cupom aceita até {MensagemCabecalhoMaxima} letras.");
        if (dados.FundoTrocoPadraoCentavos < 0)
            throw new RegraDeNegocioException("O troco inicial padrão não pode ser negativo.");
        if (dados.EstoqueMinimoPadrao < 0)
            throw new RegraDeNegocioException("O estoque mínimo padrão não pode ser negativo.");
        if (dados.MargemPadraoPercentual is < 0 or >= 100)
            throw new RegraDeNegocioException("A margem padrão deve ficar entre 0% e 99%.");
        if (!string.IsNullOrWhiteSpace(dados.Email) && !dados.Email.Contains('@'))
            throw new RegraDeNegocioException("O e-mail da loja parece errado (falta o @).");

        if (dados.LogoAlterado && dados.LogoCupom is { Length: > 0 } logo)
        {
            if (logo.Length > LogoMaximoBytes) throw new RegraDeNegocioException("O logo é grande demais (máximo 300 KB). Escolha uma imagem menor.");
            if (!(logo.Length > 8 && logo[0] == 0x89 && logo[1] == 0x50 && logo[2] == 0x4E && logo[3] == 0x47))
                throw new RegraDeNegocioException("O logo precisa ser uma imagem PNG (a tela converte sozinha ao enviar).");
        }

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
        config.CupomMostrarCidade = dados.CupomMostrarCidade;
        config.CupomMostrarInscricao = dados.CupomMostrarInscricao;
        config.CupomMostrarEmail = dados.CupomMostrarEmail;
        config.CupomMostrarOperador = dados.CupomMostrarOperador;
        config.CupomMostrarCliente = dados.CupomMostrarCliente;
        config.CupomMostrarPagamento = dados.CupomMostrarPagamento;
        config.CupomMostrarAssinatura = dados.CupomMostrarAssinatura;
        config.CupomMensagemCabecalho = Limpar(dados.CupomMensagemCabecalho);
        config.FundoTrocoPadraoCentavos = dados.FundoTrocoPadraoCentavos;
        config.EstoqueMinimoPadrao = dados.EstoqueMinimoPadrao;
        config.MargemPadraoPercentual = dados.MargemPadraoPercentual;
        config.Segmento = Limpar(dados.Segmento);
        config.EnderecoNumero = Limpar(dados.EnderecoNumero);
        config.Bairro = Limpar(dados.Bairro);
        config.Cep = Limpar(dados.Cep);
        config.CupomMostrarLogo = dados.CupomMostrarLogo;
        config.CupomMostrarReferencia = dados.CupomMostrarReferencia;
        config.CupomTituloEmCaixa = dados.CupomTituloEmCaixa;
        config.CupomMostrarAssinaturaCliente = dados.CupomMostrarAssinaturaCliente;
        config.CupomTextoGarantia = Limpar(dados.CupomTextoGarantia);
        if (dados.LogoAlterado) config.LogoCupom = dados.LogoCupom is { Length: > 0 } ? dados.LogoCupom : null;
        config.NomeOperador = string.IsNullOrWhiteSpace(dados.NomeOperador) ? "Caixa" : dados.NomeOperador.Trim();

        await db.SaveChangesAsync();
        return config;
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
