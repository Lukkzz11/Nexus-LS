using System.IO;
using System.Windows;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;

namespace NexusLS.BarTender;

public partial class App : Application
{
    public static readonly string LogFile = "C:\\NexusLS\\src\\NexusLS.BarTender\\debug.log";

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup BEGIN");
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        try
        {
            Log("Testing BarcodeGeneratorService.Gerar...");
            var bmp = BarcodeGeneratorService.Gerar("7891234567890", TipoSimboloCodigo.Code128, 200, 60);
            Log($"ZXing generated bmp: {(bmp != null ? $"{bmp.PixelWidth}x{bmp.PixelHeight}" : "NULL")}");
        }
        catch (Exception ex)
        {
            Log($"ZXing EXCEPTION: {ex}");
        }

        Log("OnStartup END");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit with code: {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
