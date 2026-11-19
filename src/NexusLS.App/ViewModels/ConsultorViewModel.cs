using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Consultor;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Os cartões do Consultor Nexus na tela Início. Tudo é calculado por regras no <see cref="ConsultorNexus"/>, fora da thread da
/// tela: aqui só se mostra o resultado, se atualiza (botão "Atualizar") e se dispensa um cartão. Carrega sozinho ao abrir,
/// sem segurar a tela (o Início aparece primeiro e os cartões chegam depois).
/// </summary>
public sealed partial class ConsultorViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly ConsultorNexus _consultor;
    private readonly Action<ItemMenu> _abrir;
    private readonly Func<AreaConsultor, bool> _podeVer;
    private readonly Func<DateTime> _agora;

    /// <param name="podeVer">Que assuntos esta pessoa pode ver (nulo = pelas permissões de quem entrou).</param>
    public ConsultorViewModel(ConsultorNexus consultor, Action<ItemMenu> abrir, Func<AreaConsultor, bool>? podeVer = null,
        Func<DateTime>? agora = null, bool carregarAoCriar = true)
    {
        _consultor = consultor;
        _abrir = abrir;
        _podeVer = podeVer ?? PodeVerPelaSessao;
        _agora = agora ?? (() => DateTime.Now);
        Carregamento = carregarAoCriar ? CarregarAsync(atualizar: false) : Task.CompletedTask;
    }

    /// <summary>A primeira carga (os testes esperam por ela).</summary>
    public Task Carregamento { get; private set; }

    public ObservableCollection<CartaoConsultor> Cartoes { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Vazio), nameof(TextoBotaoAtualizar))]
    private bool _carregando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Vazio))]
    private bool _jaCarregou;

    [ObservableProperty] private string _atualizadoEmTexto = "";

    /// <summary>Há cartões dispensados que podem voltar (botão "Mostrar os dispensados").</summary>
    [ObservableProperty] private bool _temDispensados;

    /// <summary>Não há cartão para mostrar (e já terminou de carregar): a tela mostra o estado vazio.</summary>
    public bool Vazio => JaCarregou && !Carregando && Cartoes.Count == 0;

    public bool TemCartoes => Cartoes.Count > 0;

    public string TextoBotaoAtualizar => Carregando ? "Calculando..." : "Atualizar";

    /// <summary>Explica por que não há cartões, em português simples.</summary>
    public string TextoVazio { get; private set; } = "";

    public static bool PodeVerPelaSessao(AreaConsultor area) => area switch
    {
        AreaConsultor.Financeiro => Sessao.Pode(Permissao.Financeiro),
        AreaConsultor.Estoque => Sessao.Pode(Permissao.Estoque),
        _ => Sessao.Pode(Permissao.Vender) || Sessao.Pode(Permissao.Caixa) || Sessao.Pode(Permissao.Relatorios) || Sessao.Pode(Permissao.Clientes)
    };

    /// <summary>Atualizar ignora o cache e lê tudo de novo.</summary>
    [RelayCommand]
    public Task AtualizarAsync() => Carregamento = CarregarAsync(atualizar: true);

    private async Task CarregarAsync(bool atualizar)
    {
        if (Carregando) return;
        Carregando = true;
        Mensagem = "";
        try
        {
            var sugestoes = await _consultor.ObterAsync(_podeVer, atualizar);
            Cartoes.Clear();
            foreach (var s in sugestoes) Cartoes.Add(new CartaoConsultor(s));
            AtualizadoEmTexto = "Atualizado às " + _agora().ToString("HH:mm", PtBr);
            MontarTextoVazio();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sugestão é um extra: se não deu para calcular, o resto do Início continua funcionando.
            MostrarErro("Não foi possível calcular as sugestões agora. Tente \"Atualizar\" em instantes.");
            Cartoes.Clear();
            TextoVazio = "";
        }
        finally
        {
            TemDispensados = _consultor.QuantasDispensadas > 0;
            JaCarregou = true;
            Carregando = false;
            OnPropertyChanged(nameof(Vazio));
            OnPropertyChanged(nameof(TemCartoes));
            OnPropertyChanged(nameof(TextoVazio));
        }
    }

    private void MontarTextoVazio()
    {
        var algumaArea = Enum.GetValues<AreaConsultor>().Any(_podeVer);
        TextoVazio = !algumaArea
            ? "O seu acesso não inclui assuntos para o Consultor mostrar."
            : "Tudo tranquilo por aqui: o Consultor não achou nada que precise de atenção agora. As sugestões de vendas aparecem depois de algumas semanas de movimento para comparar.";
    }

    /// <summary>Esconde o cartão por 7 dias; guardado neste computador.</summary>
    [RelayCommand]
    private void Dispensar(CartaoConsultor? cartao)
    {
        if (cartao is null) return;
        _consultor.Dispensar(cartao.Id);
        Cartoes.Remove(cartao);
        TemDispensados = true;
        MontarTextoVazio();
        OnPropertyChanged(nameof(Vazio));
        OnPropertyChanged(nameof(TemCartoes));
        OnPropertyChanged(nameof(TextoVazio));
    }

    /// <summary>Traz de volta os cartões dispensados.</summary>
    [RelayCommand]
    private Task MostrarDispensadosAsync()
    {
        _consultor.RestaurarDispensadas();
        return AtualizarAsync();
    }

    /// <summary>Abre a tela que o cartão sugere.</summary>
    [RelayCommand]
    private void Abrir(CartaoConsultor? cartao)
    {
        if (cartao is null) return;
        var item = cartao.Destino switch
        {
            DestinoConsultor.Estoque => (ItemMenu?)ItemMenu.EstoqueDaLoja,
            DestinoConsultor.EstoqueParado => ItemMenu.RelatorioProdutos,
            DestinoConsultor.Desempenho => ItemMenu.Desempenho,
            DestinoConsultor.Clientes => ItemMenu.Clientes,
            DestinoConsultor.Financeiro => ItemMenu.Financeiro,
            DestinoConsultor.RelatorioFinanceiro => ItemMenu.RelatorioFinanceiro,
            _ => null
        };
        if (item is { } i) _abrir(i);
    }
}

/// <summary>Um cartão na tela: o texto da sugestão mais o que a tela precisa para pintar (cor da faixa, rótulo da área).</summary>
public sealed class CartaoConsultor
{
    public CartaoConsultor(Sugestao s)
    {
        Sugestao = s;
    }

    public Sugestao Sugestao { get; }
    public string Id => Sugestao.Id;
    public string Titulo => Sugestao.Titulo;
    public string Texto => Sugestao.Texto;
    public string AcaoSugerida => Sugestao.AcaoSugerida;
    public DestinoConsultor Destino => Sugestao.Destino;
    public bool TemAcao => Sugestao.Destino != DestinoConsultor.Nenhum;

    /// <summary>"Urgente", "Atenção" ou "Dica": aparece escrito no cartão (a cor sozinha não basta para quem não enxerga bem).</summary>
    public string GravidadeTexto => Sugestao.Gravidade switch
    {
        GravidadeSugestao.Urgente => "Urgente",
        GravidadeSugestao.Atencao => "Atenção",
        _ => "Dica"
    };

    public bool EhUrgente => Sugestao.Gravidade == GravidadeSugestao.Urgente;
    public bool PedeAtencao => Sugestao.Gravidade == GravidadeSugestao.Atencao;

    public string AreaTexto => Sugestao.Area switch
    {
        AreaConsultor.Financeiro => "Financeiro",
        AreaConsultor.Estoque => "Estoque",
        _ => "Vendas"
    };

    public string NomeAcessivel => $"Sugestão de {AreaTexto.ToLowerInvariant()}, {GravidadeTexto.ToLowerInvariant()}: {Titulo}";
    public string NomeDispensar => $"Dispensar a sugestão {Titulo}";
}
