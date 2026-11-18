using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class PreparoFiscalTests
{
    [Fact]
    public void Tudo_preenchido_deixa_a_lista_toda_ok()
    {
        var itens = PreparoFiscal.Verificar("11.222.333/0001-81", "123456", "Recife/PE", "Mei", 1, "1");
        Assert.All(itens, i => Assert.True(i.Ok, i.Texto));
    }

    [Fact]
    public void Cnpj_invalido_e_cpf_nao_servem()
    {
        Assert.False(PreparoFiscal.Verificar("11.222.333/0001-00", "1", "x", "Mei", 1, "1")[0].Ok);
        Assert.False(PreparoFiscal.Verificar("529.982.247-25", "1", "x", "Mei", 1, "1")[0].Ok);
    }

    [Fact]
    public void Vazio_marca_pendencias()
    {
        var itens = PreparoFiscal.Verificar(null, null, null, null, 0, null);
        Assert.All(itens, i => Assert.False(i.Ok, i.Texto));
    }
}
