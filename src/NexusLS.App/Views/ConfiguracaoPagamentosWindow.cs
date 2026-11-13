using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NexusLS.Core.Pagamentos;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>
/// Configurações de Pagamento (Mercado Pago). O token é digitado aqui pelo dono da loja e vai direto para o cofre do Windows:
/// a tela nunca mostra o token salvo (campo em branco = manter o atual).
/// </summary>
public sealed class ConfiguracaoPagamentosWindow : Window
{
    private readonly ConfiguracaoPagamentosStore _store;
    private readonly RepositorioPixSqlite _repo;
    private readonly ServicoPix _pix;
    private List<CobrancaPixRegistro> _lista = new();
    private readonly CheckBox _outroMonitor = new() { Content = "Abrir a tela do Pix no outro monitor (o do cliente)", Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _porta = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly CheckBox _ativo = new() { Content = "Ativar Pix automático pelo Mercado Pago", Margin = new Thickness(0, 0, 0, 10) };
    private readonly PasswordBox _token = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _email = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _percentual = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _validade = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _mensagem = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ListBox _historico = new() { Height = 150, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ConfiguracaoPagamentos _config;

    public ConfiguracaoPagamentosWindow(ConfiguracaoPagamentosStore store, RepositorioPixSqlite repo, ServicoPix pix)
    {
        _store = store;
        _repo = repo;
        _pix = pix;
        _config = store.Ler();
        Title = "Configurações de Pagamento";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _ativo.IsChecked = _config.MercadoPagoAtivo;
        _outroMonitor.IsChecked = _config.PixEmOutroMonitor;
        _porta.Text = _config.WebhookPorta.ToString(CultureInfo.InvariantCulture);
        _email.Text = _config.EmailPagador;
        _percentual.Text = _config.MercadoPagoPercentual.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
        _validade.Text = _config.ValidadeMinutos.ToString(CultureInfo.InvariantCulture);

        var salvar = new Button { Content = "Salvar", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        salvar.Click += (_, _) => Salvar();
        var testar = new Button { Content = "Testar conexão", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        testar.Click += async (_, _) => await TestarAsync();
        var fechar = new Button { Content = "Fechar", Padding = new Thickness(16, 6, 16, 6), IsCancel = true };

        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        botoes.Children.Add(salvar);
        botoes.Children.Add(testar);
        botoes.Children.Add(fechar);

        var p = new StackPanel { Margin = new Thickness(20) };
        p.Children.Add(new TextBlock { Text = "Mercado Pago (Pix)", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        p.Children.Add(_ativo);
        p.Children.Add(new TextBlock { Text = _config.TemToken ? "Access Token (já cadastrado; deixe em branco para manter)" : "Access Token do Mercado Pago" });
        p.Children.Add(_token);
        p.Children.Add(new TextBlock { Text = "E-mail do pagador (o Mercado Pago exige um; pode ser o da loja)" });
        p.Children.Add(_email);
        p.Children.Add(new TextBlock { Text = "Taxa do Mercado Pago no Pix (%): confirme na sua conta antes de usar" });
        p.Children.Add(_percentual);
        p.Children.Add(new TextBlock { Text = "Validade do QR Code (minutos)" });
        p.Children.Add(_validade);
        p.Children.Add(_outroMonitor);
        p.Children.Add(new TextBlock { Text = "Porta local dos avisos do Mercado Pago (webhook); vale na próxima abertura do Nexus" });
        p.Children.Add(_porta);
        p.Children.Add(botoes);
        p.Children.Add(_mensagem);
        p.Children.Add(new TextBlock { Text = "Últimas cobranças Pix", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
        p.Children.Add(_historico);
        var estornar = new Button { Content = "Estornar o selecionado", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 6, 8, 0) };
        estornar.Click += async (_, _) => await EstornarAsync();
        var conferir = new Button { Content = "Conferência (CSV)", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 6, 0, 0) };
        conferir.Click += async (_, _) => await ConferirAsync();
        var linha = new StackPanel { Orientation = Orientation.Horizontal };
        linha.Children.Add(estornar);
        linha.Children.Add(conferir);
        p.Children.Add(linha);
        Content = p;

        Loaded += async (_, _) => await CarregarHistoricoAsync();
    }

    private bool Aplicar()
    {
        if (!decimal.TryParse(_percentual.Text.Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var pct) || pct < 0 || pct > 20)
        { _mensagem.Text = "Taxa inválida. Digite algo como 0,99."; return false; }
        if (!int.TryParse(_validade.Text.Trim(), out var min) || min < 5 || min > 1440)
        { _mensagem.Text = "A validade deve ficar entre 5 e 1440 minutos."; return false; }

        _config.MercadoPagoAtivo = _ativo.IsChecked == true;
        _config.EmailPagador = _email.Text.Trim();
        _config.MercadoPagoPercentual = pct;
        _config.ValidadeMinutos = min;
        if (!int.TryParse(_porta.Text.Trim(), out var porta) || porta < 1024 || porta > 65535)
        { _mensagem.Text = "A porta deve ficar entre 1024 e 65535."; return false; }
        _config.WebhookPorta = porta;
        _config.PixEmOutroMonitor = _outroMonitor.IsChecked == true;
        _store.DefinirToken(_config, _token.Password);
        if (_config.MercadoPagoAtivo && !_config.TemToken) { _mensagem.Text = "Cadastre o Access Token para ativar."; return false; }
        if (_config.MercadoPagoAtivo && !_config.EmailPagador.Contains('@')) { _mensagem.Text = "Informe um e-mail válido do pagador."; return false; }
        return true;
    }

    private void Salvar()
    {
        if (!Aplicar()) return;
        _store.Salvar(_config);
        _token.Clear();
        _mensagem.Text = "Configurações salvas.";
    }

    private async Task TestarAsync()
    {
        if (!Aplicar()) return;
        _store.Salvar(_config);
        _token.Clear();
        _mensagem.Text = "Testando...";
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var gateway = new MercadoPagoGateway(http, () => _store.Token(), () => _config.EmailPagador);
        var (_, mensagem) = await gateway.TestarAsync();
        _mensagem.Text = mensagem;
    }

    private async Task CarregarHistoricoAsync()
    {
        try
        {
            _historico.Items.Clear();
            _lista = (await _repo.HistoricoAsync(30)).ToList();
            foreach (var c in _lista)
                _historico.Items.Add($"{c.CriadaEm:dd/MM HH:mm} · {Dinheiro.Formatar(c.ValorCentavos)} · {c.Status}" + (c.VendaId is int v ? $" · venda #{v}" : ""));
        }
        catch (Exception) { /* histórico é só informativo */ }
    }

    private async Task EstornarAsync()
    {
        if (_historico.SelectedIndex < 0 || _historico.SelectedIndex >= _lista.Count) { _mensagem.Text = "Selecione uma cobrança na lista."; return; }
        var c = _lista[_historico.SelectedIndex];
        if (c.Status != StatusPix.Aprovado) { _mensagem.Text = "Só dá para estornar um Pix já pago."; return; }
        if (MessageBox.Show(this, $"Devolver {Dinheiro.Formatar(c.ValorCentavos)} ao cliente (estorno total)?\nIsso não pode ser desfeito. Depois, cancele a venda no Nexus.",
                "Estornar Pix", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _pix.EstornarAsync(c.ChaveVenda);
            _mensagem.Text = "Estorno feito. Lembre de cancelar a venda no Nexus.";
        }
        catch (Exception e) when (e is InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException)
        { _mensagem.Text = e.Message; }
        await CarregarHistoricoAsync();
    }

    private async Task ConferirAsync()
    {
        try
        {
            var todas = await _repo.HistoricoAsync(5000);
            var r = ConferenciaPix.Resumir(todas, DateTime.Now);
            var pasta = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var arquivo = System.IO.Path.Combine(pasta, $"Conferencia-Pix-{DateTime.Now:yyyy-MM-dd-HHmm}.csv");
            await System.IO.File.WriteAllTextAsync(arquivo, ConferenciaPix.Csv(todas), new System.Text.UTF8Encoding(true));
            _mensagem.Text = $"{r.Aprovados} pagos ({Dinheiro.Formatar(r.AprovadosCentavos)}), {r.Estornados} estornados ({Dinheiro.Formatar(r.EstornadosCentavos)}), {r.Pendentes} pendentes, {r.Expirados} expirados. "
                + (r.PagosSemVenda.Count > 0 ? $"ATENÇÃO: {r.PagosSemVenda.Count} Pix pago(s) sem venda ligada. " : "Nenhum pago sem venda. ")
                + (r.PendentesAntigos.Count > 0 ? $"{r.PendentesAntigos.Count} pendente(s) com mais de 2 h. " : "") + "Arquivo salvo em Documentos.";
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { _mensagem.Text = "Não foi possível salvar o arquivo: " + e.Message; }
    }
}
