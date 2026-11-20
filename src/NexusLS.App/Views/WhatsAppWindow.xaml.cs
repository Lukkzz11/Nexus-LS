using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using NexusLS.Core.Comunicacao;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>Um produto na lista de ofertas: marcado ou não, com preço promocional opcional.</summary>
public sealed class ItemOfertaWhats
{
    public required NexusLS.Core.Models.Produto Produto { get; init; }
    public bool Marcado { get; set; }
    public string Promocao { get; set; } = "";
    public string Texto => $"{Produto.Nome} — {Dinheiro.Formatar(Produto.PrecoVendaCentavos)}";
}

/// <summary>
/// WhatsApp semi-automático, sem custo: abre o WhatsApp Web dentro do programa (a loja lê o QR code uma vez), prepara a conversa
/// com a mensagem e o anexo e, se a pessoa quiser, aperta "Enviar" sozinho. Funciona para pessoas e grupos. É feito sobre a
/// página do WhatsApp Web, então se o WhatsApp mudar o visual o envio automático pode falhar; nesse caso a mensagem fica
/// pronta e é só clicar no botão verde.
/// </summary>
public partial class WhatsAppWindow : Window
{
    private static WhatsAppWindow? _aberta;

    private readonly ConfiguracaoWhatsAppStore _store = new(ConfiguracaoWhatsAppStore.CaminhoPadrao());
    private readonly ObservableCollection<ItemOfertaWhats> _ofertas = new();
    private ConfiguracaoWhatsApp _config = new();
    private CancellationTokenSource? _lote;
    private bool _pronto;
    private string _nomeLoja = "Nexus LS";

    public WhatsAppWindow()
    {
        InitializeComponent();
        ListaOfertas.ItemsSource = _ofertas;
        Loaded += async (_, _) => await IniciarAsync();
        Closed += (_, _) => { _aberta = null; _lote?.Cancel(); };
    }

    /// <summary>Abre (ou traz para a frente) a janela, já com destino, mensagem e anexo preenchidos.</summary>
    public static void Abrir(Window? dono, string? telefone = null, string? mensagem = null, string? anexo = null)
    {
        var janela = _aberta ??= new WhatsAppWindow();
        if (!janela.IsVisible) janela.Show();
        janela.WindowState = WindowState.Normal;
        janela.Activate();
        janela.Preencher(telefone, mensagem, anexo);
    }

    private void Preencher(string? telefone, string? mensagem, string? anexo)
    {
        if (telefone is not null) { RbPessoa.IsChecked = true; TxtTelefone.Text = telefone; }
        if (mensagem is not null) TxtMensagem.Text = mensagem;
        if (anexo is not null) TxtAnexo.Text = anexo;
    }

    // ------------------------------------------------------------------ inicialização

    private async Task IniciarAsync()
    {
        _config = _store.Carregar();
        ChkAutomatico.IsChecked = _config.EnviarAutomatico;
        TxtLink.Text = _config.LinkCatalogo;
        CmbGrupo.ItemsSource = _config.Grupos.ToList();
        try { _nomeLoja = (await App.Servicos.Configuracao.ObterAsync()).NomeLoja; } catch (Exception) { }

        try
        {
            var dados = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "whatsapp-web");
            var ambiente = await CoreWebView2Environment.CreateAsync(userDataFolder: dados);
            await Navegador.EnsureCoreWebView2Async(ambiente);
            Navegador.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Navegador.CoreWebView2.NewWindowRequested += (_, a) => a.Handled = true;
            Navegador.CoreWebView2.Navigate("https://web.whatsapp.com/");
            _pronto = true;
            TxtStatus.Text = "WhatsApp Web aberto. Se pedir, leia o QR code com o celular da loja (Aparelhos conectados).";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or System.Runtime.InteropServices.COMException or WebView2RuntimeNotFoundException)
        {
            AvisoWeb.Text = ex is WebView2RuntimeNotFoundException
                ? "O WhatsApp precisa do Microsoft Edge WebView2 Runtime (já vem no Windows 11 e no Windows 10 atualizado)."
                : "Não foi possível abrir o WhatsApp Web: " + ex.Message;
            AvisoWeb.Visibility = Visibility.Visible;
            TxtStatus.Text = "WhatsApp Web indisponível.";
        }
    }

    // ------------------------------------------------------------------ formulário

    private void AoMudarDestino(object sender, RoutedEventArgs e)
    {
        if (PainelPessoa is null || PainelGrupo is null) return;
        var grupo = RbGrupo.IsChecked == true;
        PainelPessoa.Visibility = grupo ? Visibility.Collapsed : Visibility.Visible;
        PainelGrupo.Visibility = grupo ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SalvarGrupo(object sender, RoutedEventArgs e)
    {
        var nome = (CmbGrupo.Text ?? "").Trim();
        if (nome.Length == 0) return;
        _config.Grupos.Add(nome);
        SalvarConfig();
        CmbGrupo.ItemsSource = _config.Grupos.ToList();
        CmbGrupo.Text = nome;
        Resultado($"Grupo \"{nome}\" salvo na lista.", false);
    }

    private void RemoverGrupo(object sender, RoutedEventArgs e)
    {
        var nome = (CmbGrupo.Text ?? "").Trim();
        _config.Grupos.RemoveAll(g => string.Equals(g, nome, StringComparison.CurrentCultureIgnoreCase));
        SalvarConfig();
        CmbGrupo.ItemsSource = _config.Grupos.ToList();
        CmbGrupo.Text = "";
    }

    private void SalvarConfig()
    {
        _config.EnviarAutomatico = ChkAutomatico.IsChecked == true;
        _config.LinkCatalogo = TxtLink.Text.Trim();
        try { _store.Salvar(_config); } catch (Exception ex) { Resultado("Não foi possível salvar as preferências: " + ex.Message, true); }
    }

    private void EscolherAnexo(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Title = "Escolha o arquivo para enviar", CheckFileExists = true };
        if (d.ShowDialog(this) == true) TxtAnexo.Text = d.FileName;
    }

    private void LimparAnexo(object sender, RoutedEventArgs e) => TxtAnexo.Text = "";

    private void Resultado(string texto, bool erro)
    {
        TxtResultado.Text = texto;
        TxtResultado.Foreground = (Brush)FindResource(erro ? "CorNegativo" : "CorPositivo");
    }

    // ------------------------------------------------------------------ ofertas do catálogo

    private void BuscaTecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Buscar(sender, e);
    }

    private async void Buscar(object sender, RoutedEventArgs e)
    {
        try
        {
            var achados = await App.Servicos.Produtos.BuscarAsync(TxtBusca.Text.Trim());
            foreach (var p in achados.Where(p => p.Ativo && !p.Sobra).Take(40))
                if (_ofertas.All(o => o.Produto.Id != p.Id)) _ofertas.Add(new ItemOfertaWhats { Produto = p });
            Resultado(achados.Count == 0 ? "Nenhum produto encontrado." : "Marque os produtos e, se quiser, ponha o preço promocional.", false);
        }
        catch (Exception ex) { Resultado("Erro na busca: " + ex.Message, true); }
    }

    private void MontarOfertas(object sender, RoutedEventArgs e)
    {
        var marcados = _ofertas.Where(o => o.Marcado).ToList();
        if (marcados.Count == 0) { Resultado("Marque pelo menos um produto.", true); return; }
        var lista = new List<OfertaWhatsApp>();
        foreach (var o in marcados)
        {
            long? promo = null;
            if (!string.IsNullOrWhiteSpace(o.Promocao))
            {
                if (!Dinheiro.TentarConverter(o.Promocao, out var p)) { Resultado($"Preço promocional inválido em \"{o.Produto.Nome}\". Exemplo: 49,90", true); return; }
                promo = p;
            }
            lista.Add(new OfertaWhatsApp(o.Produto.Nome, o.Produto.PrecoVendaCentavos, promo));
        }
        SalvarConfig();
        TxtMensagem.Text = RegrasWhatsApp.MensagemOfertas(lista, _nomeLoja, TxtLink.Text);
        Resultado("Mensagem de ofertas montada. Confira o texto, escolha o grupo e clique em Enviar.", false);
    }

    // ------------------------------------------------------------------ envio

    private async void Enviar(object sender, RoutedEventArgs e)
    {
        if (!_pronto) { Resultado("O WhatsApp Web ainda não abriu.", true); return; }
        var texto = TxtMensagem.Text.Trim();
        var anexo = string.IsNullOrWhiteSpace(TxtAnexo.Text) ? null : TxtAnexo.Text;
        if (texto.Length == 0 && anexo is null) { Resultado("Escreva uma mensagem ou escolha um anexo.", true); return; }
        SalvarConfig();
        SetOcupado(true);
        try
        {
            if (RbGrupo.IsChecked == true)
            {
                var nome = (CmbGrupo.Text ?? "").Trim();
                if (nome.Length == 0) { Resultado("Digite ou escolha o nome do grupo.", true); return; }
                await EnviarParaGrupoAsync(nome, texto, anexo, ChkAutomatico.IsChecked == true, CancellationToken.None);
            }
            else
            {
                var tel = RegrasWhatsApp.NormalizarTelefone(TxtTelefone.Text);
                if (tel is null) { Resultado("Telefone inválido. Use DDD + número, ex.: (81) 99999-0000.", true); return; }
                await EnviarParaPessoaAsync(tel, texto, anexo, ChkAutomatico.IsChecked == true, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { Resultado("Envio cancelado.", true); }
        catch (Exception ex) { Resultado(ex.Message, true); }
        finally { SetOcupado(false); }
    }

    private async void EnviarLote(object sender, RoutedEventArgs e)
    {
        if (!_pronto) { Resultado("O WhatsApp Web ainda não abriu.", true); return; }
        SalvarConfig();
        var grupos = _config.Grupos.ToList();
        if (grupos.Count == 0) { Resultado("Salve pelo menos um grupo na lista (modo Grupo → Salvar na lista).", true); return; }
        var texto = TxtMensagem.Text.Trim();
        var anexo = string.IsNullOrWhiteSpace(TxtAnexo.Text) ? null : TxtAnexo.Text;
        if (texto.Length == 0 && anexo is null) { Resultado("Escreva uma mensagem ou escolha um anexo.", true); return; }
        if (MessageBox.Show(this, $"Enviar esta mensagem para {grupos.Count} grupo(s)?\n\n" + string.Join("\n", grupos.Select(g => "• " + g)) +
                "\n\nO programa espera alguns segundos entre um grupo e outro.", "Envio em lote", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _lote = new CancellationTokenSource();
        SetOcupado(true);
        BtnParar.Visibility = Visibility.Visible;
        var ok = 0;
        var falhas = new List<string>();
        var rng = new Random();
        try
        {
            for (var i = 0; i < grupos.Count; i++)
            {
                _lote.Token.ThrowIfCancellationRequested();
                Resultado($"Enviando {i + 1} de {grupos.Count}: {grupos[i]}...", false);
                try
                {
                    await EnviarParaGrupoAsync(grupos[i], texto, anexo, true, _lote.Token);
                    ok++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { falhas.Add($"{grupos[i]}: {ex.Message}"); }
                if (i < grupos.Count - 1)
                {
                    var espera = rng.Next(_config.IntervaloMinimoSegundos, _config.IntervaloMaximoSegundos + 1);
                    for (var s = espera; s > 0; s--)
                    {
                        Resultado($"Enviado a {ok} grupo(s). Próximo em {s}s...", false);
                        await Task.Delay(1000, _lote.Token);
                    }
                }
            }
            Resultado($"Pronto: {ok} de {grupos.Count} grupo(s)." + (falhas.Count == 0 ? "" : "\nNão enviou: " + string.Join("; ", falhas)), falhas.Count > 0);
        }
        catch (OperationCanceledException) { Resultado($"Envio em lote interrompido. Enviado a {ok} grupo(s).", true); }
        finally
        {
            BtnParar.Visibility = Visibility.Collapsed;
            SetOcupado(false);
            _lote = null;
        }
    }

    private void PararLote(object sender, RoutedEventArgs e) => _lote?.Cancel();

    private void SetOcupado(bool ocupado)
    {
        BtnEnviar.IsEnabled = !ocupado;
        BtnLote.IsEnabled = !ocupado;
    }

    private async Task EnviarParaPessoaAsync(string telefone, string texto, string? anexo, bool automatico, CancellationToken ct)
    {
        var curto = texto.Length <= RegrasWhatsApp.LimiteTextoNoLink;
        Navegador.CoreWebView2.Navigate(RegrasWhatsApp.LinkConversa(telefone, curto ? texto : null));
        await EsperarAsync(ComposerPronto, 45, "A conversa não abriu. Confira se o WhatsApp Web está conectado e se o número tem WhatsApp.", ct);
        if (!curto) await InserirTextoAsync(texto);
        await FinalizarEnvioAsync(texto, anexo, automatico, ct);
    }

    private async Task EnviarParaGrupoAsync(string grupo, string texto, string? anexo, bool automatico, CancellationToken ct)
    {
        if (!(Navegador.CoreWebView2.Source ?? "").StartsWith("https://web.whatsapp.com", StringComparison.OrdinalIgnoreCase))
            Navegador.CoreWebView2.Navigate("https://web.whatsapp.com/");
        await EsperarAsync(() => Js("!!document.querySelector('#side [contenteditable=\"true\"], div[contenteditable=\"true\"][data-tab=\"3\"]')"), 60,
            "O WhatsApp Web não carregou a lista de conversas. Leia o QR code e tente de novo.", ct);

        var nome = JsonSerializer.Serialize(grupo);
        await Navegador.CoreWebView2.ExecuteScriptAsync(
            "(function(n){const b=document.querySelector('#side [contenteditable=\"true\"], div[contenteditable=\"true\"][data-tab=\"3\"]');" +
            "b.focus();document.execCommand('selectAll');document.execCommand('insertText',false,n);return true;})(" + nome + ")");
        await Task.Delay(1800, ct);
        var clicou = false;
        for (var t = 0; t < 8 && !clicou; t++)
        {
            clicou = await Js("(function(n){n=n.trim().toLowerCase();const s=[...document.querySelectorAll('#pane-side span[title]')];" +
                "const x=s.find(e=>e.getAttribute('title').trim().toLowerCase()===n)||s.find(e=>e.getAttribute('title').toLowerCase().includes(n));" +
                "if(!x)return false;const a=x.closest('[role=\"listitem\"]')||x.closest('[role=\"row\"]')||x.closest('div[tabindex]')||x;" +
                "a.dispatchEvent(new MouseEvent('mousedown',{bubbles:true}));a.click();return true;})(" + nome + ")");
            if (!clicou) await Task.Delay(700, ct);
        }
        if (!clicou) throw new InvalidOperationException($"Não achei o grupo \"{grupo}\". O nome precisa ser igual ao do WhatsApp.");
        await EsperarAsync(ComposerPronto, 20, "A conversa do grupo não abriu.", ct);
        if (texto.Length > 0) await InserirTextoAsync(texto);
        await FinalizarEnvioAsync(texto, anexo, automatico, ct);
    }

    /// <summary>Depois de o texto estar escrito: aperta Enviar (se for automático) e então manda o anexo.</summary>
    private async Task FinalizarEnvioAsync(string texto, string? anexo, bool automatico, CancellationToken ct)
    {
        if (!automatico)
        {
            if (anexo is not null) await ColarArquivoAsync(anexo, false, ct);
            Resultado("Pronto! Confira a conversa no painel ao lado e clique no botão verde de enviar.", false);
            return;
        }
        if (texto.Length > 0)
        {
            await Task.Delay(500, ct);
            if (!await Js(ScriptEnviar)) throw new InvalidOperationException("Não consegui apertar Enviar sozinho. A mensagem está escrita: clique no botão verde.");
            await Task.Delay(1200, ct);
        }
        if (anexo is not null) await ColarArquivoAsync(anexo, true, ct);
        Resultado("Enviado!", false);
    }

    private async Task ColarArquivoAsync(string caminho, bool enviar, CancellationToken ct)
    {
        if (!System.IO.File.Exists(caminho)) throw new InvalidOperationException("O arquivo anexado não existe mais.");
        var lista = new StringCollection { caminho };
        Clipboard.SetFileDropList(lista);
        await Js("(function(){const e=document.querySelector('footer [contenteditable=\"true\"]');if(e)e.focus();return true;})()");
        Navegador.Focus();
        foreach (var tipo in new[] { "keyDown", "keyUp" })
            await Navegador.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                "{\"type\":\"" + tipo + "\",\"modifiers\":2,\"windowsVirtualKeyCode\":86,\"key\":\"v\",\"code\":\"KeyV\",\"commands\":[\"paste\"]}");
        if (!enviar) return;
        await Task.Delay(2500, ct);
        var ok = false;
        for (var t = 0; t < 10 && !ok; t++)
        {
            ok = await Js(ScriptEnviar);
            if (!ok) await Task.Delay(700, ct);
        }
        if (!ok) throw new InvalidOperationException("O arquivo foi colado mas não consegui apertar Enviar. Clique no botão verde.");
        await Task.Delay(1500, ct);
    }

    // ------------------------------------------------------------------ ajudantes do WhatsApp Web

    private const string ScriptEnviar =
        "(function(){const b=document.querySelector('button[aria-label=\"Enviar\"],button[aria-label=\"Send\"],div[aria-label=\"Enviar\"],div[aria-label=\"Send\"],span[data-icon=\"send\"]');" +
        "if(!b)return false;(b.closest('button')||b.closest('[role=\"button\"]')||b).click();return true;})()";

    private Task<bool> ComposerPronto() => Js("!!document.querySelector('footer [contenteditable=\"true\"]')");

    private async Task InserirTextoAsync(string texto)
    {
        var json = JsonSerializer.Serialize(texto);
        await Navegador.CoreWebView2.ExecuteScriptAsync(
            "(function(t){const e=document.querySelector('footer [contenteditable=\"true\"]');if(!e)return false;e.focus();" +
            "const l=t.split('\\n');for(let i=0;i<l.length;i++){if(i>0)document.execCommand('insertLineBreak');if(l[i])document.execCommand('insertText',false,l[i]);}return true;})(" + json + ")");
    }

    private async Task<bool> Js(string codigo)
    {
        var r = await Navegador.CoreWebView2.ExecuteScriptAsync(codigo);
        return r == "true";
    }

    private async Task EsperarAsync(Func<Task<bool>> condicao, int segundos, string erro, CancellationToken ct)
    {
        for (var i = 0; i < segundos * 2; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await condicao()) return;
            await Task.Delay(500, ct);
        }
        throw new InvalidOperationException(erro);
    }
}
