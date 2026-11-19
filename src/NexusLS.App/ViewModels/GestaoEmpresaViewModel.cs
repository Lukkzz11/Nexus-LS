using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha da lista de lojas. Hoje só existe a loja deste computador; as outras vêm da nuvem numa etapa futura.</summary>
public sealed record LinhaLoja(string Nome, string Codigo, string Faturamento, string Vendas, string TicketMedio, string Situacao);

/// <summary>Um passo do "como adicionar uma loja".</summary>
public sealed record PassoAdicionarLoja(string Numero, string Titulo, string Texto);

/// <summary>
/// Aba "Lojas" da Gestão da empresa. Cada loja é OUTRO computador, com banco próprio. Aqui aparece a loja deste computador
/// (nome, código e desempenho do período) e o passo a passo para adicionar outra. Nada é enviado à nuvem nesta etapa.
/// Sem a permissão Financeiro os valores não aparecem.
/// </summary>
public partial class LojasViewModel : PeriodoViewModelBase
{
    private readonly RelatorioFinanceiroService _relatorio;
    private readonly Func<ConfiguracaoSeguranca> _config;
    private readonly Func<bool> _podeVer;
    private int _versao;

    public LojasViewModel(RelatorioFinanceiroService relatorio, Func<ConfiguracaoSeguranca> config, Func<DateTime>? hoje = null, Func<bool>? podeVerValores = null)
        : base(hoje)
    {
        _relatorio = relatorio;
        _config = config;
        _podeVer = podeVerValores ?? (() => Sessao.Pode(Permissao.Financeiro));
        AtualizarIdentificacao();
        Carregamento = AtualizarAsync();
    }

    public ObservableCollection<LinhaLoja> Lojas { get; } = new();
    public ObservableCollection<CartaoNumero> Desempenho { get; } = new();

    [ObservableProperty] private string _lojaNome = "";
    [ObservableProperty] private string _lojaId = "";
    [ObservableProperty] private string _textoPeriodo = "";
    [ObservableProperty] private bool _semPermissao;
    [ObservableProperty] private bool _mostrandoComoAdicionar;

    /// <summary>Copia um texto para a área de transferência (a tela liga; sem isso nada acontece).</summary>
    public Action<string> CopiarTexto { get; set; } = _ => { };

    public IReadOnlyList<PassoAdicionarLoja> Passos => new[]
    {
        new PassoAdicionarLoja("1", "Cada loja é um computador", "Uma loja nova é outro computador com o Nexus instalado. Cada computador tem o seu próprio banco de dados; nada é misturado sozinho."),
        new PassoAdicionarLoja("2", "Instale o Nexus no outro computador", "Instale e abra o Nexus lá, como foi feito aqui. Não copie o arquivo do banco deste computador para o outro: dois computadores com o mesmo código se confundem."),
        new PassoAdicionarLoja("3", "Dê um nome à loja nova", "No outro computador, abra as Configurações do Nexus e escreva o nome da loja em \"Nome desta loja\" (por exemplo, Loja 2). O código da loja é criado sozinho, uma vez só."),
        new PassoAdicionarLoja("4", "Compartilhar com a nuvem (em breve)", "Quando o envio estiver liberado, cada loja manda um resumo dos seus dias pelo botão \"Compartilhar com a nuvem\". Só o administrador vê todas as lojas aqui. Nesta versão nada é enviado."),
    };

    protected override void PeriodoMudou() => Carregamento = AtualizarAsync();

    private void AtualizarIdentificacao()
    {
        var c = _config();
        LojaNome = string.IsNullOrWhiteSpace(c.LojaNome) ? ConfiguracaoSeguranca.LojaNomePadrao : c.LojaNome.Trim();
        LojaId = c.LojaId ?? "";
    }

    [RelayCommand]
    private void AdicionarLoja()
    {
        AtualizarIdentificacao();
        MostrandoComoAdicionar = !MostrandoComoAdicionar;
    }

    [RelayCommand] private void FecharComoAdicionar() => MostrandoComoAdicionar = false;

    [RelayCommand]
    private void CopiarCodigo()
    {
        if (string.IsNullOrWhiteSpace(LojaId)) { MostrarErro("Esta loja ainda não tem código. Abra Configurações e salve o nome da loja."); return; }
        CopiarTexto(LojaId);
        MostrarSucesso("Código da loja copiado.");
    }

    [RelayCommand]
    private Task AtualizarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        AtualizarIdentificacao();
        if (!_podeVer())
        {
            SemPermissao = true;
            Desempenho.Clear();
            Lojas.Clear();
            Lojas.Add(new LinhaLoja(LojaNome, LojaId, "—", "—", "—", "Só neste computador"));
            return;
        }
        SemPermissao = false;
        var periodo = PeriodoEscolhido();
        var r = await _relatorio.GerarAsync(periodo);
        if (versao != _versao) return;
        MostrarSucesso("");
        TextoPeriodo = TextoDoPeriodo(r.Periodo);
        var ticket = r.NumeroDeVendas > 0 ? r.VendasCentavos / r.NumeroDeVendas : 0;
        Desempenho.Clear();
        Desempenho.Add(new CartaoNumero("Faturamento", Reais(r.VendasCentavos), $"{r.NumeroDeVendas} venda(s)"));
        Desempenho.Add(new CartaoNumero("Ticket médio", Reais(ticket), "por venda"));
        Desempenho.Add(new CartaoNumero("Entradas e saídas lançadas", Reais(r.EntradasLancadasCentavos - r.SaidasLancadasCentavos),
            $"{Reais(r.EntradasLancadasCentavos)} entradas, {Reais(r.SaidasLancadasCentavos)} saídas", r.EntradasLancadasCentavos < r.SaidasLancadasCentavos));
        Desempenho.Add(new CartaoNumero("Saldo do período", Reais(r.SaldoCentavos), "vendas e lançamentos", r.SaldoCentavos < 0));
        Lojas.Clear();
        Lojas.Add(new LinhaLoja(LojaNome, LojaId, Reais(r.VendasCentavos), r.NumeroDeVendas.ToString(PtBr), Reais(ticket), "Este computador"));
    });
}

/// <summary>
/// A tela "Gestão da empresa": abas Lojas e Entradas e saídas. É do administrador. A aba de lançamentos só aparece para quem tem
/// a permissão Financeiro (sem ela não há valores para ver).
/// </summary>
public partial class GestaoEmpresaViewModel : ObservableObject
{
    public GestaoEmpresaViewModel(LojasViewModel lojas, EntradasESaidasViewModel? entradasESaidas)
    {
        Lojas = lojas;
        EntradasESaidas = entradasESaidas;
    }

    public LojasViewModel Lojas { get; }
    public EntradasESaidasViewModel? EntradasESaidas { get; }
    public bool TemEntradasESaidas => EntradasESaidas is not null;

    [ObservableProperty] private int _abaSelecionada;
}
