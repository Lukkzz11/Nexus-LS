using System;
using System.IO;
using System.Windows;

namespace NexusLS.LicenseManager;

public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogErro("AppDomain UnhandledException", e.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, e) =>
        {
            LogErro("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        };
    }

    private static void LogErro(string tipo, Exception? ex)
    {
        try
        {
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{tipo}] {ex?.Message}\n{ex?.StackTrace}\n";
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), msg);
            MessageBox.Show(
                $"Erro capturado: {ex?.Message}\n\nDetalhes gravados em crash.log",
                "Nexus LS License Manager - Diagnóstico",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }
}
