using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NexusLS.LicenseManager;

public partial class MainWindow : Window
{
    private WebView2? _webView;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += (s, e) => LogStep($"Window Closing! Cancel={e.Cancel}");
        Closed += (s, e) => LogStep("Window Closed!");
        ConfigurarIcone();
    }

    private static void LogStep(string s)
    {
        try
        {
            var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_steps.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] {s}\n");
        }
        catch { }
    }

    private void ConfigurarIcone()
    {
        try
        {
            var ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus.ico");
            if (File.Exists(ico))
            {
                Icon = BitmapFrame.Create(new Uri(ico, UriKind.Absolute));
            }
        }
        catch
        {
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LogStep("1. Iniciando MainWindow_Loaded");
            _webView = new WebView2();
            RootGrid.Children.Insert(0, _webView);

            // Tenta inicializar com pasta persistente; se der 0x800700AA (recurso em uso), cai no fallback com GUID único
            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NexusLS",
                    "LicenseData");
                Directory.CreateDirectory(userDataFolder);
                LogStep($"2. Tentando diretório persistente: {userDataFolder}");

                _webView.CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = userDataFolder
                };

                await _webView.EnsureCoreWebView2Async();
                LogStep("3. EnsureCoreWebView2Async com pasta persistente funcionou!");
            }
            catch (Exception exInit)
            {
                LogStep($"4. Falha na pasta persistente ({exInit.Message}). Usando fallback isolado...");
                RootGrid.Children.Remove(_webView);
                _webView.Dispose();

                _webView = new WebView2();
                RootGrid.Children.Insert(0, _webView);

                var uniqueTempFolder = Path.Combine(Path.GetTempPath(), "NexusLS_LM_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(uniqueTempFolder);
                LogStep($"5. Diretório fallback único: {uniqueTempFolder}");

                _webView.CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = uniqueTempFolder
                };

                await _webView.EnsureCoreWebView2Async();
                LogStep("6. EnsureCoreWebView2Async com fallback funcionou perfeitamente!");
            }

            _webView.CoreWebView2.ProcessFailed += (s, args) =>
            {
                LogStep($"PROCESS_FAILED: Kind={args.ProcessFailedKind}, Reason={args.Reason}, ExitCode={args.ExitCode}");
            };

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var wwwroot = Path.Combine(baseDir, "wwwroot");

            if (!Directory.Exists(wwwroot) || !File.Exists(Path.Combine(wwwroot, "index.html")))
            {
                var devDist = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\license-manager\dist"));
                if (Directory.Exists(devDist) && File.Exists(Path.Combine(devDist, "index.html")))
                {
                    wwwroot = devDist;
                }
            }

            LogStep($"6. Pasta wwwroot: {wwwroot}");

            if (Directory.Exists(wwwroot) && File.Exists(Path.Combine(wwwroot, "index.html")))
            {
                LogStep("7. Registrando VirtualHostName https://nexus.local");
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "nexus.local",
                    wwwroot,
                    CoreWebView2HostResourceAccessKind.Allow);

                _webView.CoreWebView2.NavigationCompleted += (s, args) =>
                {
                    LogStep($"8. Navegação concluída! Sucesso: {args.IsSuccess}, Erro: {args.WebErrorStatus}");
                    LoadingProgress.Visibility = Visibility.Collapsed;
                };

                LogStep("9. Navegando para https://nexus.local/index.html");
                _webView.CoreWebView2.Navigate("https://nexus.local/index.html");
            }
            else
            {
                LogStep("ERRO: wwwroot não encontrado!");
                LoadingProgress.Visibility = Visibility.Collapsed;
                MessageBox.Show(
                    $"Os arquivos da interface do License Manager não foram localizados em:\n{wwwroot}",
                    "Nexus LS License Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            LogStep($"EXCEÇÃO: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_init.log"), ex.ToString());
            }
            catch { }
            LoadingProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show(
                $"Falha ao inicializar o License Manager:\n{ex.Message}\n\n{ex.StackTrace}",
                "Nexus LS License Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
