using NexusLS.Core.Etiquetas;

namespace NexusLS.Tests;

public class PedidoBarTenderTests
{
    [Fact]
    public void Montar_e_ler_devolvem_os_mesmos_itens()
    {
        var itens = new[]
        {
            new ItemPedidoBarTender(12, "P", 3),
            new ItemPedidoBarTender(15, "", 1),
        };

        var lidos = PedidoBarTender.Ler(PedidoBarTender.Montar(itens));

        Assert.Equal(itens, lidos);
    }

    [Fact]
    public void Itens_invalidos_sao_ignorados_na_montagem_e_na_leitura()
    {
        Assert.Empty(PedidoBarTender.Montar(new[]
        {
            new ItemPedidoBarTender(0, "P", 1),
            new ItemPedidoBarTender(5, "P", 0),
        }));

        var lidos = PedidoBarTender.Ler(new[] { "--item=abc|P|1", "--item=7|M|-2", "--item=7|M", "--outra=1", "--item=9|G|2" });

        Assert.Single(lidos);
        Assert.Equal(new ItemPedidoBarTender(9, "G", 2), lidos[0]);
    }

    [Fact]
    public void Tamanho_com_barra_nao_quebra_o_formato_e_quantidade_tem_teto()
    {
        var args = PedidoBarTender.Montar(new[] { new ItemPedidoBarTender(3, "P|M", 50000) });

        var lido = Assert.Single(PedidoBarTender.Ler(args));
        Assert.Equal("P M", lido.Tamanho);
        Assert.Equal(9999, lido.Quantidade);
    }

    [Fact]
    public void Sem_argumentos_nao_ha_pedido()
    {
        Assert.Empty(PedidoBarTender.Ler(null));
        Assert.Empty(PedidoBarTender.Ler(Array.Empty<string>()));
    }

    [Fact]
    public void LocalizarExe_acha_na_pasta_do_programa_e_devolve_nulo_quando_nao_existe()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "nexus-bt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            Assert.Null(PedidoBarTender.LocalizarExe(pasta, pasta));

            var exe = Path.Combine(pasta, PedidoBarTender.NomeExe);
            File.WriteAllText(exe, "x");
            Assert.Equal(exe, PedidoBarTender.LocalizarExe(pasta, pasta));
        }
        finally { Directory.Delete(pasta, true); }
    }
}
