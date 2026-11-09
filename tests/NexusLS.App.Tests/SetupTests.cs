using NexusLS.Setup;

namespace NexusLS.App.Tests;

public class SetupTests
{
    [Fact]
    public void Linha_de_comando_le_silencioso_empresa_e_pasta()
    {
        var op = OpcoesSetup.Ler(["/S", "--empresa=arena-sandplay", "--pasta=D:\Nexus"]);
        Assert.True(op.Silencioso);
        Assert.Equal("arena-sandplay", op.Empresa);
        Assert.Equal("D:\Nexus", op.Pasta);
        Assert.False(OpcoesSetup.Ler([]).Silencioso);
        Assert.EndsWith("Nexus", OpcoesSetup.Ler([]).Pasta);
    }

    [Theory]
    [InlineData("lifesurf-principal", "lifesurf-principal")]
    [InlineData("a\",\"x\":\"b", null)]
    [InlineData("../x", null)]
    [InlineData("", null)]
    public void Id_da_empresa_so_aceita_caracteres_seguros(string entrada, string? esperado) =>
        Assert.Equal(esperado, OpcoesSetup.SomenteIdValido(entrada));

    [Fact]
    public void Hash_confere_com_o_formato_do_arquivo_sha256()
    {
        var h = new string('a', 64);
        Assert.True(Instalador.HashConfere(h + "  NexusLS.exe\n", h.ToUpperInvariant()));
        Assert.False(Instalador.HashConfere(h + "  NexusLS.exe", new string('b', 64)));
        Assert.False(Instalador.HashConfere("", h));
    }

    [Fact]
    public void Conexao_com_o_erp_gera_json_valido_com_a_empresa()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Instalador.ConteudoErpJson("arena-sandplay"));
        Assert.Equal("arena-sandplay", doc.RootElement.GetProperty("EmpresaId").GetString());
        Assert.Equal("pdv---lifesurf", doc.RootElement.GetProperty("ProjectId").GetString());
    }
}
