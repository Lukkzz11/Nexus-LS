using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusLS.Core.Pagamentos;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>
/// Mostra o QR Code e o Pix copia e cola e espera o gateway confirmar. Consulta o status a cada poucos segundos.
/// Fechar a janela sem pagamento NÃO cancela a cobrança: reabrir a mesma venda reaproveita o mesmo QR Code.
/// </summary>
public sealed class PixCobrancaWindow : Window
{
    private readonly ServicoPix _pix;
    private readonly string _chave;
    private readonly TextBlock _status = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBox _codigo = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 70, FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Image _qr = new() { Width = 260, Height = 260, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _consultando;

    public bool Aprovado { get; private set; }

    public PixCobrancaWindow(ServicoPix pix, string chaveVenda, long valorCentavos, bool emOutroMonitor = false)
    {
        _pix = pix;
        _chave = chaveVenda;
        Title = "Pix · " + Dinheiro.Formatar(valorCentavos);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var copiar = new Button { Content = "Copiar código Pix", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 8, 8, 0) };
        copiar.Click += (_, _) => { try { Clipboard.SetText(_codigo.Text); _status.Text = "Código copiado. " + _status.Text; } catch { /* área de transferência ocupada */ } };
        var cancelar = new Button { Content = "Fechar sem pagar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 8, 0, 0), IsCancel = true };
        cancelar.Click += (_, _) => Close();

        var painel = new StackPanel { Margin = new Thickness(20) };
        painel.Children.Add(new TextBlock { Text = "Mostre o QR Code ao cliente", FontSize = 18, FontWeight = FontWeights.Bold });
        painel.Children.Add(new TextBlock { Text = "Total: " + Dinheiro.Formatar(valorCentavos), FontSize = 15, Margin = new Thickness(0, 2, 0, 10) });
        painel.Children.Add(_qr);
        painel.Children.Add(new TextBlock { Text = "Pix copia e cola:", Margin = new Thickness(0, 10, 0, 2) });
        painel.Children.Add(_codigo);
        painel.Children.Add(_status);
        var botoes = new StackPanel { Orientation = Orientation.Horizontal };
        botoes.Children.Add(copiar);
        botoes.Children.Add(cancelar);
        painel.Children.Add(botoes);
        Content = painel;

        _status.Text = "Gerando a cobrança...";
        Loaded += async (_, _) => await GerarAsync(valorCentavos);
        _timer.Tick += async (_, _) => await ConsultarAsync();
        // O aviso do Mercado Pago (webhook) chega em outra thread: reconsulta na hora, sem esperar o próximo ciclo.
        void AoAvisar(CobrancaPix _) => Dispatcher.BeginInvoke(async () => await ConsultarAsync());
        _pix.CobrancaAtualizada += AoAvisar;
        Closed += (_, _) => { _timer.Stop(); _pix.CobrancaAtualizada -= AoAvisar; };
        if (emOutroMonitor) ContentRendered += (_, _) => TelasPix.MoverParaOutroMonitor(this);
    }

    private async Task GerarAsync(long valorCentavos)
    {
        try
        {
            var cobranca = await _pix.IniciarAsync(_chave, valorCentavos, "Venda Nexus");
            _codigo.Text = cobranca.CopiaECola;
            if (!string.IsNullOrWhiteSpace(cobranca.QrCodeBase64)) _qr.Source = Imagem(cobranca.QrCodeBase64!);
            Tratar(cobranca.Status);
            if (!Aprovado) _timer.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = "Não foi possível gerar o Pix: " + ex.Message;
        }
    }

    private async Task ConsultarAsync()
    {
        if (_consultando) return;
        _consultando = true;
        try
        {
            var c = await _pix.AtualizarAsync(_chave);
            if (c is not null) Tratar(c.Status);
        }
        finally { _consultando = false; }
    }

    private void Tratar(StatusPix status)
    {
        switch (status)
        {
            case StatusPix.Aprovado:
                _timer.Stop();
                Aprovado = true;
                _status.Foreground = Brushes.SeaGreen;
                _status.Text = "Pagamento confirmado!";
                DialogResult = true;
                break;
            case StatusPix.Expirado:
            case StatusPix.Cancelado:
                _timer.Stop();
                _status.Foreground = Brushes.Firebrick;
                _status.Text = status == StatusPix.Expirado ? "O QR Code expirou. Feche e tente de novo para gerar outro." : "A cobrança foi cancelada. Feche e tente de novo.";
                break;
            default:
                _status.Text = "Aguardando o pagamento... (sem internet a venda continua pendente)";
                break;
        }
    }

    private static ImageSource? Imagem(string base64)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(Convert.FromBase64String(base64));
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception) { return null; }   // sem imagem ainda dá para pagar pelo copia e cola
    }
}
