using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;

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

    [Fact]
    public void Folha_A4_no_tamanho_do_modelo_cabe_na_folha_e_a_Canon_a_aceita()
    {
        var folha = PerfisMidia.FolhaA4NoTamanho(35, 60);
        var canon = PerfilImpressora.DoNome("Canon G3010 series")!;

        Assert.Equal(20, folha.EtiquetasPorPagina);
        Assert.True(canon.Aceita(folha.Tipo));
        Assert.Empty(folha.Validar(canon.LarguraMaximaMm));
        Assert.Null(PerfilImpressora.DoNome("ELGIN i8"));
    }
}
