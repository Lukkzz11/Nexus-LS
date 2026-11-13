using System.Diagnostics;
using System.IO;
using System.Windows;

namespace NexusLS.Setup;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var op = OpcoesSetup.Ler(args);
        if (op.Desinstalar)
        {
            var script = Path.Combine(op.Pasta, "Desinstalar-Nexus.ps1");
            if (File.Exists(script))
            {
                Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = true });
            }
            return 0;
        }

        if (op.Silencioso)
        {
            try
            {
                new Instalador(op).InstalarAsync(new Progress<(double, string)>()).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "nexus-setup-erro.txt"), e.ToString());
                return 1;
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var msg = e.ExceptionObject?.ToString() ?? "Erro fatal desconhecido";
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "nexus-setup-fatal.txt"), msg); } catch { }
            MessageBox.Show(msg, "Erro Fatal do Instalador", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        var app = new Application();
        app.DispatcherUnhandledException += (s, e) =>
        {
            e.Handled = true;
            var msg = e.Exception.ToString();
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "nexus-setup-fatal.txt"), msg); } catch { }
            MessageBox.Show(msg, "Erro na Interface do Instalador", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        return app.Run(new JanelaSetup(op));
    }
}
