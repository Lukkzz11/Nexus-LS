using System.IO;
using System.Windows;

namespace NexusLS.Atualizador;

public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.WriteAllText(@"C:\NexusLS\crash_atualizador.txt", e.ExceptionObject?.ToString() ?? "Unhandled null"); } catch { }
        };

        DispatcherUnhandledException += (s, e) =>
        {
            try { File.WriteAllText(@"C:\NexusLS\crash_atualizador.txt", e.Exception?.ToString() ?? "Dispatcher null"); } catch { }
        };
    }
}
