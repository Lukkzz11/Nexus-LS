using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Vitrine;

namespace NexusLS.App.ViewModels;

/// <summary>Uma cor editável: o campo de texto guarda o #RRGGBB (vazio = vale a do modelo).</summary>
public partial class CorEditavel : ObservableObject
{
    public CorEditavel(string id, string rotulo, string valor) { Id = id; Rotulo = rotulo; _valor = valor; }
    public string Id { get; }
    public string Rotulo { get; }
    [ObservableProperty] private string _valor;

    /// <summary>A cor para o quadradinho de prévia (nulo = sem cor ou inválida).</summary>
    public string? Previa => ConfiguracaoVitrine.NormalizarHex(Valor);
    partial void OnValorChanged(string value) => OnPropertyChanged(nameof(Previa));
}

public partial class BannerEditavel : ObservableObject
{
    [ObservableProperty] private string _selo = "";
    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string _destaque = "";
    [ObservableProperty] private string _subtitulo = "";
    [ObservableProperty] private string _botaoRotulo = "";
    public string Nome { get; init; } = "";
}

/// <summary>
/// Editor do catálogo online: modelo, fonte, cores, textos, dados da loja, atacado e banners. Salvar guarda no
/// computador; Publicar manda para o Firebase (documento meta/vitrine). O envio só acontece no clique.
/// </summary>
public partial class EditorCatalogoViewModel : ViewModelBase
{
    private readonly VitrineStore _store;
    private readonly IPublicadorVitrine? _publicador;

    public EditorCatalogoViewModel(VitrineStore store, IPublicadorVitrine? publicador = null)
    {
        _store = store;
        _publicador = publicador;
        Carregar(_store.Carregar());
    }

    public IReadOnlyList<Opcao> Modelos => OpcoesVitrine.Modelos;
    public IReadOnlyList<Opcao> Fontes => OpcoesVitrine.Fontes;
    public IReadOnlyList<Opcao> Ordens => OpcoesVitrine.Ordens;
    public bool PodePublicar => _publicador is not null;

    public ObservableCollection<CorEditavel> Cores { get; } = new();
    public ObservableCollection<BannerEditavel> Banners { get; } = new();
    public ObservableCollection<string> Avisos { get; } = new();

    [ObservableProperty] private string _modelo = "padrao";
    [ObservableProperty] private string _fonte = "sistema";
    [ObservableProperty] private string _ordemProdutos = "padrao";
    [ObservableProperty] private bool _mostrarDescricao;
    [ObservableProperty] private string _textoAnuncio = "";
    [ObservableProperty] private string _eyebrow = "";
    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string _lojaNome = "";
    [ObservableProperty] private string _lojaApresentacao = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _whatsapp = "";
    [ObservableProperty] private string _instagram = "";
    [ObservableProperty] private string _endereco = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _atacadoMinPecas = "10";
    [ObservableProperty] private string _novidadesQtd = "8";

    private void Carregar(ConfiguracaoVitrine c)
    {
        Modelo = c.Modelo; Fonte = c.Fonte; OrdemProdutos = c.OrdemProdutos; MostrarDescricao = c.MostrarDescricao;
        TextoAnuncio = c.TextoAnuncio; Eyebrow = c.Eyebrow; Titulo = c.Titulo;
        LojaNome = c.LojaNome; LojaApresentacao = c.LojaApresentacao; Telefone = c.Telefone; Whatsapp = c.Whatsapp;
        Instagram = c.Instagram; Endereco = c.Endereco; Email = c.Email;
        AtacadoMinPecas = c.AtacadoMinPecas.ToString(); NovidadesQtd = c.NovidadesQtd.ToString();

        Cores.Clear();
        foreach (var (id, rotulo) in OpcoesVitrine.Cores)
        {
            var cor = new CorEditavel(id, rotulo, c.Cores.GetValueOrDefault(id, ""));
            cor.PropertyChanged += (_, _) => AtualizarAvisos();
            Cores.Add(cor);
        }
        Banners.Clear();
        for (var i = 0; i < c.Banners.Count; i++)
        {
            var b = c.Banners[i];
            Banners.Add(new BannerEditavel
            {
                Nome = i == 0 ? "Banner de novidades" : i == 1 ? "Banner do atacado" : $"Banner {i + 1}",
                Selo = b.Selo, Titulo = b.Titulo, Destaque = b.Destaque, Subtitulo = b.Subtitulo, BotaoRotulo = b.BotaoRotulo
            });
        }
        AtualizarAvisos();
    }

    private ConfiguracaoVitrine Montar()
    {
        if (!int.TryParse(AtacadoMinPecas.Trim(), out var minimo)) throw new RegraDeNegocioException("O mínimo de peças do atacado deve ser um número inteiro.");
        if (!int.TryParse(NovidadesQtd.Trim(), out var novidades)) throw new RegraDeNegocioException("A quantidade de novidades deve ser um número inteiro.");
        var c = new ConfiguracaoVitrine
        {
            Modelo = Modelo, Fonte = Fonte, OrdemProdutos = OrdemProdutos, MostrarDescricao = MostrarDescricao,
            TextoAnuncio = TextoAnuncio, Eyebrow = Eyebrow, Titulo = Titulo,
            LojaNome = LojaNome, LojaApresentacao = LojaApresentacao, Telefone = Telefone, Whatsapp = Whatsapp,
            Instagram = Instagram, Endereco = Endereco, Email = Email, AtacadoMinPecas = minimo, NovidadesQtd = novidades,
            Cores = Cores.Where(x => !string.IsNullOrWhiteSpace(x.Valor)).ToDictionary(x => x.Id, x => x.Valor),
            Banners = Banners.Select(b => new BannerVitrine { Selo = b.Selo, Titulo = b.Titulo, Destaque = b.Destaque, Subtitulo = b.Subtitulo, BotaoRotulo = b.BotaoRotulo }).ToList()
        };
        c.Normalizar();
        return c;
    }

    private void AtualizarAvisos()
    {
        Avisos.Clear();
        var rascunho = new ConfiguracaoVitrine
        {
            Cores = Cores.Where(c => c.Previa is not null).ToDictionary(c => c.Id, c => c.Previa!)
        };
        foreach (var a in rascunho.AvisosDeContraste()) Avisos.Add(a);
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(() =>
    {
        var c = Montar();
        _store.Salvar(c);
        Carregar(c);                                   // mostra o que ficou depois da limpeza
        MostrarSucesso("Catálogo salvo neste computador. Use Publicar para enviar ao Firebase.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task PublicarAsync() => ExecutarAsync(async () =>
    {
        if (_publicador is null) throw new RegraDeNegocioException("A publicação não está disponível.");
        var c = Montar();
        _store.Salvar(c);
        Carregar(c);
        try
        {
            await _publicador.PublicarAsync(c);
        }
        catch (FalhaDestinoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        MostrarSucesso("Catálogo publicado. A vitrine online passa a usar estas escolhas.");
    });

    [RelayCommand]
    private void Restaurar()
    {
        Carregar(new ConfiguracaoVitrine());
        MostrarSucesso("Escolhas padrão carregadas. Use Salvar para guardar.");
    }
}
