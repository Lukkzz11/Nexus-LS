using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class MensagensClienteTests
{
    [Fact]
    public void Troca_os_campos_e_usa_so_o_primeiro_nome()
    {
        var m = MensagensCliente.Montar("Oi {cliente}, {loja}: deve {valor}, vencido {vencido} desde {vencimento}.", "Maria Souza Lima", "Loja X", 25000, 12000, new DateTime(2026, 11, 10));
        Assert.Equal("Oi Maria, Loja X: deve R$ 250,00, vencido R$ 120,00 desde 10/11/2026.", m);
    }

    [Fact]
    public void Modelo_vazio_usa_o_texto_padrao()
    {
        var m = MensagensCliente.Montar("  ", "João", "Loja X", 100, 100, new DateTime(2026, 1, 2));
        Assert.StartsWith("Olá, João! Aqui é da Loja X.", m);
        Assert.DoesNotContain("{", m);
    }

    [Theory]
    [InlineData("(11) 91234-5678", "5511912345678")]
    [InlineData("11 1234-5678", "551112345678")]
    [InlineData("+55 11 91234-5678", "5511912345678")]
    [InlineData("011 91234-5678", "5511912345678")]
    public void Telefone_vira_link_com_o_codigo_do_pais(string telefone, string esperado)
    {
        var link = MensagensCliente.LinkWhatsApp(telefone, "oi mundo & tchau");
        Assert.Equal($"https://wa.me/{esperado}?text=oi%20mundo%20%26%20tchau", link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("abc")]
    public void Telefone_que_nao_serve_nao_gera_link(string? telefone) =>
        Assert.Null(MensagensCliente.LinkWhatsApp(telefone, "oi"));
}
