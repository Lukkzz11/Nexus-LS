using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Tests;

/// <summary>Impressora de teste: guarda o que seria impresso e pode simular defeito.</summary>
public sealed class ImpressoraDeMentira : IImpressoraCupom
{
    public List<string> Impressos { get; } = new();
    public Exception? Falha { get; set; }

    public Task ImprimirAsync(string texto, ConfiguracaoLoja loja)
    {
        if (Falha is not null) throw Falha;
        Impressos.Add(texto);
        return Task.CompletedTask;
    }
}
