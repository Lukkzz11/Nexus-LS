using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexusLS.Core.Atualizacao;

namespace NexusLS.Atualizador;

/// <summary>Um card da página "Mais apps".</summary>
public sealed class CartaoDoCatalogo(AppDoCatalogo app) : INotifyPropertyChanged
{
    public AppDoCatalogo App { get; } = app;
    public string Nome => App.Nome;
    public string Subtitulo => App.Subtitulo;
    public string Descricao => App.Descricao;
    public string Cor => App.Cor;
    public string Situacao { get; private set; } = "";
    public string TextoBotao { get; private set; } = "";
    public bool Livre { get; private set; } = true;
    /// <summary>True quando o botão deve baixar (instalar ou atualizar) em vez de abrir.</summary>
    public bool PrecisaBaixar { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Mostrar(string situacao, string botao, bool livre = true, bool precisaBaixar = false)
    {
        (Situacao, TextoBotao, Livre, PrecisaBaixar) = (situacao, botao, livre, precisaBaixar);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

/// <summary>
/// "Mais apps": os aplicativos listados no catalogo-apps.json da release. Programas são instalados em
/// %LOCALAPPDATA%\Programs\Nexus\Apps\{id}; sistemas web abrem no navegador.
/// </summary>
public partial class MainWindow
{
    private static string CaminhoCatalogoGuardado() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", CatalogoDeApps.NomeDoArquivo);

    private static string PastaDoApp(AppDoCatalogo app) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexus", "Apps", app.Id);

    private void BtnMenuCatalogo_Click(object sender, RoutedEventArgs e) => NavegarPara("catalogo");

    /// <summary>Mostra a cópia guardada na hora e depois troca pelo catálogo da nuvem. Sem internet, fica a cópia.</summary>
    private async Task CarregarCatalogoAsync()
    {
        string? guardado = null;
        try
        {
            var arquivo = CaminhoCatalogoGuardado();
            if (File.Exists(arquivo)) guardado = File.ReadAllText(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        MostrarCatalogo(CatalogoDeApps.Ler(guardado));

        try
        {
            var nuvem = await CatalogoDeApps.BaixarAsync(HttpCompartilhado, ConfiguracaoAtualizacao.Repositorio());
            if (nuvem is null || nuvem == guardado) return;
            // Não troca os cards no meio de um download.
            if (ListaCatalogo.ItemsSource is IEnumerable<CartaoDoCatalogo> atuais && atuais.Any(c => !c.Livre)) return;
            MostrarCatalogo(CatalogoDeApps.Ler(nuvem));
            Directory.CreateDirectory(Path.GetDirectoryName(CaminhoCatalogoGuardado())!);
            File.WriteAllText(CaminhoCatalogoGuardado(), nuvem);
        }
        catch (Exception)
        {
            // Sem internet ou catálogo recusado: segue com o que já está na tela.
        }
    }

    private void MostrarCatalogo(IReadOnlyList<AppDoCatalogo> apps)
    {
        var cartoes = apps.Select(a => new CartaoDoCatalogo(a)).ToList();
        foreach (var cartao in cartoes) AtualizarCartao(cartao);
        ListaCatalogo.ItemsSource = cartoes;
        TxtCatalogoAviso.Text = cartoes.Count == 0
            ? "Nenhum app novo por enquanto. Quando um for publicado, ele aparece aqui sozinho."
            : "";
        TxtCatalogoAviso.Visibility = cartoes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AtualizarCartao(CartaoDoCatalogo cartao)
    {
        var app = cartao.App;
        if (app.EhWeb)
        {
            cartao.Mostrar("Sistema na web", "ABRIR NO NAVEGADOR");
            return;
        }

        var pasta = PastaDoApp(app);
        if (!File.Exists(Path.Combine(pasta, app.Exe!)))
            cartao.Mostrar($"Não instalado • v{app.Versao}", "INSTALAR", precisaBaixar: true);
        else if (InstalacaoAlinhavo.VersaoInstalada(pasta) is var instalada && InstalacaoAlinhavo.HaVersaoNova(instalada, app.Versao))
            cartao.Mostrar($"Nova versão v{app.Versao}", "ATUALIZAR", precisaBaixar: true);
        else
            cartao.Mostrar($"Instalado • v{instalada}", "ABRIR");
    }

    private async void BtnAppCatalogo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CartaoDoCatalogo { Livre: true } cartao) return;
        var app = cartao.App;

        var avaliacao = _ultimoResultadoLicenca ?? _licenca.AvaliarLocal();
        if (!avaliacao.Ativa)
        {
            JanelaDialogo.Mostrar("Licença Necessária",
                $"Não é possível usar o {app.Nome} porque a licença não está ativa neste computador.\n\nDetalhe: {avaliacao.Mensagem}",
                TipoDialogo.Aviso, this);
            NavegarPara("licenca");
            return;
        }

        try
        {
            if (app.EhWeb)
            {
                Process.Start(new ProcessStartInfo(app.Url!.AbsoluteUri) { UseShellExecute = true });
            }
            else if (cartao.PrecisaBaixar)
            {
                await InstalarDoCatalogoAsync(cartao);
            }
            else
            {
                var exe = Path.Combine(PastaDoApp(app), app.Exe!);
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = PastaDoApp(app) });
            }
        }
        catch (Exception ex)
        {
            JanelaDialogo.Mostrar(app.Nome, InstalacaoAlinhavo.Traduzir(ex, app.Nome), TipoDialogo.Erro, this);
            AtualizarCartao(cartao);
        }
    }

    private async Task InstalarDoCatalogoAsync(CartaoDoCatalogo cartao)
    {
        var app = cartao.App;
        var temp = Path.Combine(Path.GetTempPath(), "NexusHub-" + Guid.NewGuid().ToString("N"));
        cartao.Mostrar("Consultando a nuvem...", "AGUARDE", livre: false);
        try
        {
            var fonte = new FonteGitHub(HttpCompartilhado, ConfiguracaoAtualizacao.Repositorio(), app.Pacote!, app.Pacote + ".sha256");
            var info = await fonte.UltimaAsync()
                ?? throw new RegraDeAtualizacaoException($"O pacote do {app.Nome} ainda não foi publicado na nuvem.");

            var progresso = new Progress<double>(p =>
            {
                if (!cartao.Livre) cartao.Mostrar($"Baixando... {p:P0}", "AGUARDE", livre: false);
            });
            var zip = await InstalacaoAlinhavo.BaixarAsync(fonte, info, temp, progresso, nomeDoApp: app.Nome, nomeDoZip: app.Pacote!);

            if (!FecharProcessoSeguro(Path.GetFileNameWithoutExtension(app.Exe!)))
            {
                AvisarQueNaoFechou(app.Nome);
                return;
            }

            cartao.Mostrar("Instalando...", "AGUARDE", livre: false);
            var pasta = PastaDoApp(app);
            await Task.Run(() =>
            {
                InstalacaoAlinhavo.Instalar(zip, pasta, app.Nome, app.Exe!);
                File.WriteAllText(Path.Combine(pasta, InstalacaoAlinhavo.ArquivoVersao), app.Versao + "\n");
            });
            JanelaDialogo.Mostrar("Instalação Concluída", $"{app.Nome} v{app.Versao} instalado. Abra por esta tela.", TipoDialogo.Sucesso, this);
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            AtualizarCartao(cartao);
        }
    }
}
