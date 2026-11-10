using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class CupomExtrasTests
{
    private static string Cupom(Action<ConfiguracaoLoja> ajusta)
    {
        var loja = new ConfiguracaoLoja { NomeLoja = "Life" };
        ajusta(loja);
        return MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), loja);
    }

    [Fact]
    public void Por_padrao_nada_novo_aparece()
    {
        var c = Cupom(_ => { });
        Assert.DoesNotContain("Ref:", c);
        Assert.DoesNotContain("+--", c);
        Assert.DoesNotContain("Assinatura do cliente", c);
    }

    [Fact]
    public void Referencia_caixa_garantia_e_assinatura_aparecem_quando_ligados()
    {
        var c = Cupom(l =>
        {
            l.CupomMostrarReferencia = true;
            l.CupomTituloEmCaixa = true;
            l.CupomMostrarAssinaturaCliente = true;
            l.CupomTextoGarantia = "Troca em 7 dias com a etiqueta.";
        });
        Assert.Contains("Ref: CAM-001", c);
        Assert.Contains("│ CUPOM NÃO FISCAL │", c);
        Assert.Contains("Troca em 7 dias com a etiqueta.", c);
        Assert.Contains("Assinatura do cliente", c);
        Assert.True(c.IndexOf("Troca em 7 dias", StringComparison.Ordinal) < c.IndexOf("Assinatura do cliente", StringComparison.Ordinal));
    }
}
