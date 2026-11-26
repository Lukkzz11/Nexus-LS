using NexusLS.BarTender.Services;

namespace NexusLS.App.Tests;

/// <summary>A impressão vai direto para a fila; nada é impresso aqui, só a escolha da impressora.</summary>
public sealed class ImpressaoDiretaTests
{
    [Fact]
    public void Impressora_que_nao_existe_vira_erro_em_vez_de_trocar_calado()
    {
        var erro = Assert.Throws<InvalidOperationException>(() => ImpressaoService.LocalizarFila("Impressora que nao existe 123"));

        Assert.Contains("não foi encontrada", erro.Message);
    }

    [Fact]
    public void Acha_cada_impressora_instalada_pelo_nome_da_lista()
    {
        foreach (string nome in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
        {
            using var fila = ImpressaoService.LocalizarFila(nome);
            Assert.Equal(nome, fila.FullName, ignoreCase: true);
        }
    }
}
