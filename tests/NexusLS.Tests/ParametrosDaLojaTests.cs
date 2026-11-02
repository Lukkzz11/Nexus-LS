using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class ParametrosDaLojaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static ConfiguracaoLoja Loja() => new()
    {
        NomeLoja = "Life Surf", RazaoSocial = "Life Confecções LTDA", Documento = "12.345.678/0001-90", InscricaoEstadual = "0012345-67",
        Endereco = "Rua A, 10", CidadeUf = "Caruaru - PE", Telefone = "81 99999-0000", ChavePix = "pix@life.com", MensagemCupom = "Obrigado!", LarguraCupom = 42
    };

    [Fact]
    public void Cupom_mostra_os_dados_da_empresa_e_respeita_o_que_foi_desligado()
    {
        var venda = MontadorCupom.VendaDeExemplo();
        var cheio = MontadorCupom.Montar(venda, Loja());
        Assert.Contains("Life Confecções LTDA", cheio);
        Assert.Contains("IE: 0012345-67", cheio);
        Assert.Contains("Caruaru - PE", cheio);
        Assert.DoesNotContain("Pix:", cheio);                        // Pix só se ligar

        var loja = Loja();
        loja.CupomMostrarRazaoSocial = false; loja.CupomMostrarDocumento = false; loja.CupomMostrarEndereco = false;
        loja.CupomMostrarTelefone = false; loja.CupomMostrarPix = true;
        var enxuto = MontadorCupom.Montar(venda, loja);
        Assert.DoesNotContain("LTDA", enxuto);
        Assert.DoesNotContain("CNPJ", enxuto);
        Assert.DoesNotContain("IE:", enxuto);
        Assert.DoesNotContain("Rua A", enxuto);
        Assert.DoesNotContain("Tel:", enxuto);
        Assert.Contains("Pix: pix@life.com", enxuto);
        Assert.Contains("Life Surf", enxuto);                        // o nome fantasia sempre sai
    }

    [Fact]
    public void Pix_sem_mensagem_no_rodape_ganha_o_proprio_separador_e_as_linhas_finais_sao_configuraveis()
    {
        var loja = Loja();
        loja.MensagemCupom = null; loja.CupomMostrarPix = true; loja.CupomLinhasFinais = 4;
        var texto = MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), loja);
        Assert.EndsWith("Pix: pix@life.com\n\n\n\n", texto.Replace("\r", "").TrimEnd(' '), StringComparison.Ordinal);
        loja.CupomLinhasFinais = 0;
        Assert.False(MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), loja).EndsWith("\n"));
    }

    [Fact]
    public async Task Servico_grava_e_devolve_os_parametros_e_recusa_valores_invalidos()
    {
        var s = new ConfiguracaoService(_banco.Fabrica);
        var salvo = await s.SalvarAsync(new DadosLoja("Life", RazaoSocial: " Life LTDA ", Email: "a@b.com", CidadeUf: "Caruaru - PE", ChavePix: "x",
            CupomMostrarPix: true, CupomLinhasFinais: 3, FundoTrocoPadraoCentavos: 5000, EstoqueMinimoPadrao: 2, MargemPadraoPercentual: 40));
        Assert.Equal("Life LTDA", salvo.RazaoSocial);
        var lido = await s.ObterAsync();
        Assert.True(lido.CupomMostrarPix);
        Assert.Equal(3, lido.CupomLinhasFinais);
        Assert.Equal(5000, lido.FundoTrocoPadraoCentavos);
        Assert.Equal(40, lido.MargemPadraoPercentual);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAsync(new DadosLoja("Life", CupomLinhasFinais: 99)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAsync(new DadosLoja("Life", FundoTrocoPadraoCentavos: -1)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAsync(new DadosLoja("Life", EstoqueMinimoPadrao: -1)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAsync(new DadosLoja("Life", MargemPadraoPercentual: 100)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAsync(new DadosLoja("Life", Email: "sem-arroba")));
    }

    [Fact]
    public async Task Configuracao_nova_nasce_com_o_cupom_como_sempre_foi()
    {
        var c = await new ConfiguracaoService(_banco.Fabrica).ObterAsync();
        Assert.True(c.CupomMostrarDocumento && c.CupomMostrarEndereco && c.CupomMostrarTelefone && c.CupomMostrarRazaoSocial);
        Assert.False(c.CupomMostrarPix);
        Assert.Equal(1, c.CupomLinhasFinais);
    }
}
