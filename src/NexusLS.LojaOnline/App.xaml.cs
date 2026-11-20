using System.Globalization;
using System.Windows;

namespace NexusLS.LojaOnline;

public partial class App : Application
{
    public App()
    {
        // O programa fala pt-BR independente do formato regional do Windows (mesma regra do PDV).
        var br = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = br;
        CultureInfo.DefaultThreadCurrentUICulture = br;
        CultureInfo.CurrentCulture = br;
        CultureInfo.CurrentUICulture = br;
    }
}
