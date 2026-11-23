using System.IO;
using NexusLS.BarTender.Views;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Desenha a janela "Qual tamanho você quer editar?" para conferir o visual. Só roda com NEXUSLS_PRINTS.</summary>
public sealed class CapturaEscolherTamanho
{
    [Fact]
    public void Gera_a_janela_de_escolher_tamanho()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);
        var tamanhos = new[] { "G", "GG", "M", "P", "EXG", "G1", "G2", "G3", "G4", "G5", "G6", "42", "44", "46", "48", "50", "38", "40" };
        Capturador.DesenharJanela(() => new EscolherTamanhoJanela("SHORT TACTEL", tamanhos), Path.Combine(saida, "escolher-tamanho.png"), 640, 420);
    }
}
