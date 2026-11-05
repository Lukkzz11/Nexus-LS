using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class ParticipacaoTests
{
    [Fact]
    public void Sem_valores_a_lista_vem_vazia_e_zeros_e_negativos_ficam_de_fora()
    {
        Assert.Empty(Participacao.Fatias(Array.Empty<(string, long)>()));
        Assert.Empty(Participacao.Fatias(new[] { ("A", 0L), ("B", -5L) }));
    }

    [Fact]
    public void Os_seis_maiores_ficam_separados_e_o_resto_vira_Outros()
    {
        var itens = Enumerable.Range(1, 9).Select(i => ($"P{i}", (long)i * 100)).ToList(); // 100..900, total 4.500
        var fatias = Participacao.Fatias(itens);

        Assert.Equal(7, fatias.Count);
        Assert.Equal("P9", fatias[0].Rotulo);
        Assert.Equal("P4", fatias[5].Rotulo);
        Assert.Equal("Outros", fatias[6].Rotulo);
        Assert.Equal(600, fatias[6].ValorCentavos); // P1+P2+P3
        Assert.Equal(1.0, fatias.Sum(f => f.Fracao), 9);
        Assert.Equal(20.0, fatias[0].Percentual, 9);
    }

    [Fact]
    public void Com_ate_seis_itens_nao_cria_Outros_e_o_mesmo_rotulo_soma()
    {
        var fatias = Participacao.Fatias(new[] { ("Camisetas", 3000L), ("Bonés", 1000L), ("Camisetas", 1000L) });

        Assert.Equal(new[] { "Camisetas", "Bonés" }, fatias.Select(f => f.Rotulo));
        Assert.Equal(4000, fatias[0].ValorCentavos);
        Assert.DoesNotContain(fatias, f => f.Rotulo == "Outros");
    }

    [Fact]
    public void Empate_desempata_pelo_nome()
    {
        var fatias = Participacao.Fatias(new[] { ("Zeta", 100L), ("Alfa", 100L) });
        Assert.Equal(new[] { "Alfa", "Zeta" }, fatias.Select(f => f.Rotulo));
    }
}
