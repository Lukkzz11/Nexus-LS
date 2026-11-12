using System.Globalization;
using System.Runtime.CompilerServices;

namespace NexusLS.App.Tests;

/// <summary>
/// Roda antes de qualquer teste: as telas são brasileiras, então os testes também rodam em pt-BR.
/// O Windows do CI está em inglês; sem isto, textos como "2,5 metros" e "66,67%" saíam com ponto e
/// os testes falhavam por causa do formato regional, não por um erro do programa.
/// </summary>
internal static class Cultura
{
    [ModuleInitializer]
    internal static void Aplicar()
    {
        var ptBr = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = ptBr;
        CultureInfo.DefaultThreadCurrentUICulture = ptBr;
        CultureInfo.CurrentCulture = ptBr;
        CultureInfo.CurrentUICulture = ptBr;
    }
}
