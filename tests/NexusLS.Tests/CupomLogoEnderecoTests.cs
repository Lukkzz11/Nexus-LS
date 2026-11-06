using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Cadastro da loja no formato do ERP (endereço em partes, segmento, logo) e o cupom que sai dele.</summary>
public sealed class CupomLogoEnderecoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static readonly byte[] PngMinimo = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    [Fact]
    public void Endereco_sai_numa_linha_como_no_erp_e_junta_so_o_que_existe()
    {
        var loja = new ConfiguracaoLoja { NomeLoja = "Life", Endereco = "Galeria da Moda Lado B", EnderecoNumero = "100", Bairro = "Feira de Caruaru", CidadeUf = "Caruaru - PE" };
        Assert.Equal("Galeria da Moda Lado B, 100 - Feira de Caruaru", MontadorCupom.EnderecoCompleto(loja));
        Assert.Contains("Galeria da Moda Lado B, 100 -", MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), loja));

        Assert.Equal("Rua A", MontadorCupom.EnderecoCompleto(new ConfiguracaoLoja { Endereco = "Rua A" }));
        Assert.Equal("Rua A, 5", MontadorCupom.EnderecoCompleto(new ConfiguracaoLoja { Endereco = "Rua A", EnderecoNumero = "5" }));
        Assert.Equal("Centro", MontadorCupom.EnderecoCompleto(new ConfiguracaoLoja { Bairro = "Centro" }));
        Assert.Equal("", MontadorCupom.EnderecoCompleto(new ConfiguracaoLoja()));
    }

    [Fact]
    public void Sem_mensagem_de_rodape_sai_o_agradecimento_padrao_do_erp()
    {
        var cupom = MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), new ConfiguracaoLoja { NomeLoja = "Life" });
        Assert.Contains("OBRIGADO PELA PREFERÊNCIA! VOLTE SEMPRE!", cupom);
        Assert.Contains("Venda / Pedido: #123", cupom);
        Assert.Contains("1x R$ 40,00", cupom);
    }

    [Fact]
    public async Task Cadastro_guarda_segmento_endereco_em_partes_e_logo_e_mantem_o_logo_quando_nao_alterado()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        await config.SalvarAsync(new DadosLoja("Life", Segmento: "Moda surf", EnderecoNumero: "100", Bairro: "Feira", Cep: "55038-275",
            LogoCupom: PngMinimo, LogoAlterado: true));
        var c = await config.ObterAsync();
        Assert.Equal(("Moda surf", "100", "Feira", "55038-275"), (c.Segmento, c.EnderecoNumero, c.Bairro, c.Cep));
        Assert.Equal(PngMinimo, c.LogoCupom);
        Assert.True(c.CupomMostrarLogo);

        await config.SalvarAsync(new DadosLoja("Life 2")); // sem LogoAlterado: o logo continua
        Assert.Equal(PngMinimo, (await config.ObterAsync()).LogoCupom);

        await config.SalvarAsync(new DadosLoja("Life 2", LogoCupom: null, LogoAlterado: true)); // removido de propósito
        Assert.Null((await config.ObterAsync()).LogoCupom);
    }

    [Fact]
    public async Task Logo_que_nao_e_png_ou_e_grande_demais_e_recusado_sem_gravar()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        var ex1 = await Assert.ThrowsAsync<RegraDeNegocioException>(() => config.SalvarAsync(new DadosLoja("Life", LogoCupom: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, LogoAlterado: true)));
        Assert.Contains("PNG", ex1.Message);
        var grande = new byte[ConfiguracaoService.LogoMaximoBytes + 1];
        Array.Copy(PngMinimo, grande, PngMinimo.Length);
        var ex2 = await Assert.ThrowsAsync<RegraDeNegocioException>(() => config.SalvarAsync(new DadosLoja("Life", LogoCupom: grande, LogoAlterado: true)));
        Assert.Contains("300 KB", ex2.Message);
        Assert.Null((await config.ObterAsync()).LogoCupom);
    }
}
