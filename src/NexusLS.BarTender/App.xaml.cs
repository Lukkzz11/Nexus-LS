using System.IO;
using System.Windows;
using NexusLS.Core.Atualizacao;

namespace NexusLS.BarTender;

public partial class App : Application
{
    /// <summary>Pasta de log do usuário; nunca dentro da pasta do código-fonte.</summary>
    public static string PastaLogs => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusLS",
        "logs");

    public static string LogFile => Path.Combine(PastaLogs, $"bartender-{DateTime.Now:yyyyMMdd}.log");

    public App() => CulturaBr.Aplicar();

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(PastaLogs);

            var arquivo = LogFile;
            var info = new FileInfo(arquivo);
            if (info.Exists && info.Length > 2_000_000)
            {
                var rotacionado = Path.Combine(PastaLogs, $"bartender-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.Move(arquivo, rotacionado, overwrite: true);
            }

            File.AppendAllText(arquivo, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch
        {
            // Log nunca pode derrubar o programa.
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log($"OnStartup {VersaoPrograma.Atual()}");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");

            var continuar = MessageBoxResult.No;
            try
            {
                continuar = MessageBox.Show(
                    "O Nexus BarTender encontrou um erro inesperado.\n\n" +
                    $"Detalhe: {args.Exception.Message}\n\n" +
                    "Deseja continuar? Os dados que não foram salvos podem ser perdidos.\n" +
                    $"Se preferir, feche o programa e envie o log:\n{LogFile}",
                    "Nexus BarTender",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Se nem a mensagem abrir, cai no encerramento abaixo.
            }

            args.Handled = continuar == MessageBoxResult.Yes;
            if (!args.Handled)
            {
                Shutdown(1);
            }
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit com código {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
