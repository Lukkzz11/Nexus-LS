using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexusLS.Setup;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var op = OpcoesSetup.Ler(args);
        if (op.Desinstalar)
        {
            var script = System.IO.Path.Combine(op.Pasta, "Desinstalar-Nexus.ps1");
            if (System.IO.File.Exists(script))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = true });
            return 0;
        }
        if (op.Silencioso)
        {
            try { new Instalador(op).InstalarAsync(new Progress<(double, string)>()).GetAwaiter().GetResult(); return 0; }
            catch (Exception e) { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus-setup-erro.txt"), e.ToString()); return 1; }
        }
        var app = new Application();
        return app.Run(new JanelaSetup(op));
    }
}

public sealed class JanelaSetup : Window
{
    private readonly OpcoesSetup _op;
    private readonly TextBlock _texto = new() { Text = "Pronto para instalar o Nexus (PDV, Atualizador e Etiquetas).", TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ProgressBar _barra = new() { Height = 14, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button _botao = new() { Content = "Instalar", Padding = new Thickness(24, 8, 24, 8), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 14 };

    public JanelaSetup(OpcoesSetup op)
    {
        _op = op;
        Title = "Instalador do Nexus";
        Width = 520; Height = 300; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1F));
        Foreground = Brushes.White;
        var painel = new StackPanel { Margin = new Thickness(28) };
        painel.Children.Add(new TextBlock { Text = "Nexus", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        painel.Children.Add(_texto);
        painel.Children.Add(new TextBlock { Text = "Pasta: " + _op.Pasta + (_op.Empresa is null ? "" : "   •   Empresa: " + _op.Empresa), FontSize = 11, Opacity = .6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        painel.Children.Add(_barra);
        painel.Children.Add(_botao);
        Content = painel;
        _botao.Click += async (_, _) => await ExecutarAsync();
    }

    private async Task ExecutarAsync()
    {
        if (_botao.Content is "Abrir o Nexus")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(_op.Pasta, "NexusLS.exe")) { UseShellExecute = true });
            Close();
            return;
        }
        _botao.IsEnabled = false;
        var progresso = new Progress<(double Percentual, string Texto)>(p => { _barra.Value = p.Percentual; _texto.Text = p.Texto; });
        try
        {
            await new Instalador(_op).InstalarAsync(progresso);
            _texto.Text = "Tudo certo! Os atalhos foram criados na Área de Trabalho e no menu Iniciar.";
            _botao.Content = "Abrir o Nexus";
        }
        catch (Exception e)
        {
            _texto.Text = "Não foi possível instalar: " + e.Message;
            _botao.Content = "Tentar de novo";
        }
        _botao.IsEnabled = true;
    }
}
