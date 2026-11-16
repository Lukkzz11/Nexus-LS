using System.IO;
using NexusLS.BarTender.Services;

namespace NexusLS.App.Tests;

public class PreferenciasBarTenderTests
{
    [Fact]
    public void Guia_de_impressao_aparece_ate_ser_fechado_e_a_escolha_sobrevive_ao_reiniciar()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"bt_pref_{Guid.NewGuid():N}.json");
        try
        {
            Assert.False(PreferenciasBarTender.Carregar(caminho).GuiaImpressaoVisto);

            var pref = PreferenciasBarTender.Carregar(caminho);
            pref.GuiaImpressaoVisto = true;
            pref.CaminhoBanco = @"C:\x\nexus.db";
            pref.Salvar(caminho);

            var lida = PreferenciasBarTender.Carregar(caminho);
            Assert.True(lida.GuiaImpressaoVisto);
            Assert.Equal(@"C:\x\nexus.db", lida.CaminhoBanco);
        }
        finally
        {
            if (File.Exists(caminho)) File.Delete(caminho);
        }
    }
}
