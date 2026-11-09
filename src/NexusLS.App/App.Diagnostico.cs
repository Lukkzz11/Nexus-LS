using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using NexusLS.Core.Diagnostico;

namespace NexusLS.App;

/// <summary>
/// Captura global de erros. O gerente chama <see cref="ConfigurarDiagnostico"/> no começo de
/// <c>App.OnStartup</c> (antes de abrir a janela). Tudo o que não for tratado fica registrado
/// em %LOCALAPPDATA%\NexusLS\logs para dar para descobrir o que aconteceu.
/// </summary>
public partial class App
{
    private static readonly object _travaDiagnostico = new();
    private static bool _diagnosticoConfigurado;
    private static int _mostrandoErro;

    /// <summary>Pasta onde os logs ficam. Mostrada ao usuário quando um erro é registrado.</summary>
    public static string PastaDeLogs => RegistroDeLog.PastaPadrao;

    /// <summary>
    /// Liga a captura de erros. Pode ser chamado mais de uma vez: só a primeira chamada registra
    /// os eventos (idempotente).
    /// </summary>
    public static void ConfigurarDiagnostico()
    {
        lock (_travaDiagnostico)
        {
            if (_diagnosticoConfigurado) return;
            _diagnosticoConfigurado = true;
        }

        if (Current is { } app)
            app.DispatcherUnhandledException += EmExcecaoNaoTratadaNaInterface;

        AppDomain.CurrentDomain.UnhandledException += EmExcecaoNaoTratadaNoDominio;
        TaskScheduler.UnobservedTaskException += EmTarefaNaoObservada;
    }

    /// <summary>
    /// Erro na thread da interface. Registra a pilha completa e avisa o usuário.
    /// Só continua o programa quando a falha é considerada recuperável; falha grave fecha,
    /// para não seguir com o estado interno possivelmente estragado (nem esconder o problema).
    /// </summary>
    private static void EmExcecaoNaoTratadaNaInterface(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var fatal = EhFatal(e.Exception);
        RegistroDeLog.Erro("Falha não tratada na interface.", e.Exception);

        var texto = fatal
            ? "O Nexus encontrou um erro grave e precisará ser fechado." + Environment.NewLine
              + "O erro foi registrado em:" + Environment.NewLine + PastaDeLogs
            : "Ocorreu um erro inesperado, mas o Nexus pode continuar." + Environment.NewLine
              + "O erro foi registrado em:" + Environment.NewLine + PastaDeLogs + Environment.NewLine + Environment.NewLine
              + "Se o problema se repetir, feche e abra o programa.";

        MostrarErroUmaVez(texto, fatal ? MessageBoxImage.Error : MessageBoxImage.Warning);

        // Impede o Windows de mostrar o diálogo de "parou de funcionar" e permite um encerramento limpo.
        // Numa falha grave, o encerramento logo abaixo evita continuar com dados inconsistentes.
        e.Handled = true;
        if (fatal) Current?.Shutdown(1);
    }

    /// <summary>
    /// Erro fora do dispatcher (thread de fundo, finalizador). Quando chega aqui o processo já está
    /// terminando: registra e não tenta abrir janela, para não travar o encerramento.
    /// </summary>
    private static void EmExcecaoNaoTratadaNoDominio(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception excecao)
            RegistroDeLog.Erro("Falha não tratada fora da interface (o programa vai encerrar).", excecao);
        else
            RegistroDeLog.Erro("Falha não tratada fora da interface (o programa vai encerrar). Objeto: " + e.ExceptionObject);
    }

    /// <summary>
    /// Tarefa cujo erro ninguém observou. Não pode derrubar a venda por causa de uma tarefa esquecida:
    /// registra e marca como observada.
    /// </summary>
    private static void EmTarefaNaoObservada(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        RegistroDeLog.Erro("Falha em tarefa não observada.", e.Exception);
        e.SetObserved();
    }

    /// <summary>Falhas de memória/stack/access violation não têm volta segura; melhor encerrar do que continuar.</summary>
    private static bool EhFatal(Exception? excecao)
    {
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
        {
            if (atual is OutOfMemoryException or AccessViolationException or SEHException
                or BadImageFormatException or InsufficientExecutionStackException)
                return true;
        }
        return false;
    }

    /// <summary>Se outro erro estourar enquanto o aviso está na tela, não empilha caixas de diálogo.</summary>
    private static void MostrarErroUmaVez(string texto, MessageBoxImage icone)
    {
        if (Interlocked.Exchange(ref _mostrandoErro, 1) == 1) return;
        try
        {
            MessageBox.Show(texto, "Nexus", MessageBoxButton.OK, icone);
        }
        catch (Exception)
        {
            // Avisar é importante, mas não pode virar um segundo erro.
        }
        finally
        {
            Volatile.Write(ref _mostrandoErro, 0);
        }
    }
}
