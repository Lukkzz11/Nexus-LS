using NexusLS.Core.Comunicacao;
using NexusLS.Core.Models;

namespace NexusLS.Tests;

public class WhatsAppTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nx-wa-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() { try { File.Delete(_arquivo); } catch { } }

    [Theory]
    [InlineData("(81) 99999-0000", "5581999990000")]
    [InlineData("81 3333-4444", "558133334444")]
    [InlineData("+55 81 99999-0000", "5581999990000")]
    [InlineData("5581999990000", "5581999990000")]
    [InlineData("123", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Telefone_e_normalizado_com_55(string? entrada, string? esperado) =>
        Assert.Equal(esperado, RegrasWhatsApp.NormalizarTelefone(entrada));

    [Fact]
    public void Link_da_conversa_escapa_o_texto()
    {
        var link = RegrasWhatsApp.LinkConversa("5581999990000", "Oi, tudo bem?\nR$ 10 & mais");
        Assert.StartsWith("https://web.whatsapp.com/send?phone=5581999990000&text=", link);
        Assert.DoesNotContain(" ", link);
        Assert.DoesNotContain("\n", link);
        Assert.Contains("%26", link);
    }

    [Fact]
    public void Mensagem_de_ofertas_mostra_de_por_link_e_loja()
    {
        var t = RegrasWhatsApp.MensagemOfertas(new[]
        {
            new OfertaWhatsApp("Camiseta Surf", 12000, 9900),
            new OfertaWhatsApp("Boné", 5000)
        }, "Life Surf", "https://loja.exemplo/catalogo");
        Assert.Contains("Ofertas Life Surf", t);
        Assert.Contains("Camiseta Surf", t);
        Assert.Contains("por *", t);
        Assert.Contains("Boné", t);
        Assert.Contains("https://loja.exemplo/catalogo", t);
    }

    [Fact]
    public void Promocao_maior_que_o_preco_e_ignorada()
    {
        var t = RegrasWhatsApp.MensagemOfertas(new[] { new OfertaWhatsApp("Meia", 1000, 2000) }, "L", null);
        Assert.DoesNotContain("por *", t);
    }

    [Fact]
    public void Comprovante_cumprimenta_pelo_primeiro_nome()
    {
        var v = new Venda { Id = 7, DataHora = new DateTime(2026, 11, 19), TotalCentavos = 15000, Cliente = new Cliente { Nome = "Maria Souza" } };
        var t = RegrasWhatsApp.MensagemComprovante(v, "Life Surf");
        Assert.StartsWith("Olá, Maria!", t);
        Assert.Contains("nº 7", t);
    }

    [Fact]
    public void Configuracao_salva_grupos_sem_repetir_e_limita_intervalos()
    {
        var store = new ConfiguracaoWhatsAppStore(_arquivo);
        store.Salvar(new ConfiguracaoWhatsApp { Grupos = new() { "Clientes VIP", " clientes vip ", "", "Atacado" }, IntervaloMinimoSegundos = 1, IntervaloMaximoSegundos = 2 });
        var c = store.Carregar();
        Assert.Equal(new[] { "Clientes VIP", "Atacado" }, c.Grupos);
        Assert.Equal(3, c.IntervaloMinimoSegundos);
        Assert.True(c.IntervaloMaximoSegundos >= 3);
    }

    [Fact]
    public void Arquivo_estragado_volta_ao_padrao()
    {
        File.WriteAllText(_arquivo, "{ isto nao e json");
        Assert.Empty(new ConfiguracaoWhatsAppStore(_arquivo).Carregar().Grupos);
    }
}
