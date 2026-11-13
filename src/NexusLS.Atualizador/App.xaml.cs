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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--capturar-prints", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var idx = Array.FindIndex(e.Args, a => a.Equals("--capturar-prints", StringComparison.OrdinalIgnoreCase));
                var pastaDestino = (idx >= 0 && idx < e.Args.Length - 1 && !e.Args[idx + 1].StartsWith("-"))
                    ? e.Args[idx + 1]
                    : @"C:\Users\Lucas Emanuel\Desktop\Prints_Nexus";

                var win = new MainWindow();
                win.CapturarPrintsHub(pastaDestino);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(@"C:\NexusLS\crash_atualizador.txt", ex.ToString()); } catch { }
            }
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }
}
