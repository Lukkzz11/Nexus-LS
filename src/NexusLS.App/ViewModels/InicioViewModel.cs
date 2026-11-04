using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Backup;
using NexusLS.Core.Services;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Tela inicial do ERP: indicadores do dia e atalhos para os módulos. Só mostra números que o resto do
/// programa já calcula (<see cref="InicioService"/>); nenhuma regra nova mora aqui.
/// </summary>
public partial class InicioViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly InicioService _inicio;
    private readonly GestorBackup? _backup;
    private readonly Action<ItemMenu> _abrir;
    private readonly Func<DateTime> _agora;
    private readonly AgendaService? _agenda;

    public InicioViewModel(InicioService inicio, GestorBackup? backup, Action<ItemMenu> abrir, Func<DateTime>? agora = null, AgendaService? agenda = null)
    {
        _inicio = inicio;
        _agenda = agenda;
        _backup = backup;
        _abrir = abrir;
        _agora = agora ?? (() => DateTime.Now);
        var data = _agora().ToString("dddd, d 'de' MMMM 'de' yyyy", PtBr);
        DataTexto = char.ToUpper(data[0], PtBr) + data[1..]; // só a primeira letra maiúscula: "Segunda-feira, 19 de outubro de 2026"
        Saudacao = _agora().Hour switch { < 12 => "Bom dia", < 18 => "Boa tarde", _ => "Boa noite" };
        Atalhos = MontarAtalhos();
        Carregamento = AtualizarAsync();
    }

    /// <summary>A primeira carga dos indicadores (os testes esperam por ela).</summary>
    public Task Carregamento { get; }

    public ObservableCollection<string> EstoquesBaixos { get; } = new();

    /// <summary>Compromissos de hoje que ainda não foram concluídos, em ordem de horário.</summary>
    public ObservableCollection<string> AgendaHoje { get; } = new();

    /// <summary>Só os atalhos que a pessoa que entrou pode usar.</summary>
    public IReadOnlyList<AtalhoInicio> Atalhos { get; }

    public bool TemAgenda => _agenda is not null;
    public bool AgendaVazia => AgendaHoje.Count == 0;

    /// <summary>Nada precisa de atenção agora (estoque, dívida vencida, backup).</summary>
    public bool TudoEmDia => !EstoqueBaixoAlerta && !DividaAlerta && !BackupAlerta;

    [ObservableProperty] private string _saudacao = "";

    /// <summary>O cartão de backup só aparece quando há gestor de backup.</summary>
    public bool TemBackup => _backup is not null;

    [ObservableProperty] private string _dataTexto = "";

    [ObservableProperty] private string _vendasHojeTexto = "R$ 0,00";
    [ObservableProperty] private string _vendasHojeDetalhe = "";
    [ObservableProperty] private string _comparacaoOntemTexto = "";
    [ObservableProperty] private string _ticketMedioTexto = "R$ 0,00";

    [ObservableProperty] private string _caixaTexto = "";
    [ObservableProperty] private bool _caixaAberto;

    [ObservableProperty] private string _estoqueBaixoTexto = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TudoEmDia))]
    private bool _estoqueBaixoAlerta;

    [ObservableProperty] private string _dividaTexto = "";
    [ObservableProperty] private string _dividaDetalhe = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TudoEmDia))]
    private bool _dividaAlerta;

    [ObservableProperty] private string _backupTexto = "";
    [ObservableProperty] private string _backupDetalhe = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TudoEmDia))]
    private bool _backupAlerta;

    [RelayCommand]
    public Task AtualizarAsync() => ExecutarAsync(async () =>
    {
        var r = await _inicio.ObterAsync();

        VendasHojeTexto = Dinheiro.Formatar(r.TotalHojeCentavos);
        VendasHojeDetalhe = r.VendasHoje switch
        {
            0 => "Nenhuma venda hoje ainda",
            1 => "1 venda",
            _ => $"{r.VendasHoje} vendas"
        } + (r.CanceladasHoje switch { 0 => "", 1 => " · 1 cancelada", _ => $" · {r.CanceladasHoje} canceladas" });
        ComparacaoOntemTexto = $"Ontem: {Dinheiro.Formatar(r.TotalOntemCentavos)}";
        TicketMedioTexto = Dinheiro.Formatar(r.TicketMedioHojeCentavos);

        CaixaAberto = r.CaixaAberto;
        CaixaTexto = r.CaixaAberto && r.CaixaAbertoDesde is { } desde
            ? $"Aberto desde {(desde.Date == r.Dia ? desde.ToString("HH:mm", PtBr) : desde.ToString("dd/MM HH:mm", PtBr))}"
            : "Fechado";

        EstoqueBaixoAlerta = r.ProdutosAbaixoDoMinimo > 0;
        EstoqueBaixoTexto = r.ProdutosAbaixoDoMinimo switch
        {
            0 => "Tudo acima do mínimo",
            1 => "1 produto abaixo do mínimo",
            _ => $"{r.ProdutosAbaixoDoMinimo} produtos abaixo do mínimo"
        };
        EstoquesBaixos.Clear();
        foreach (var p in r.PioresEstoques) EstoquesBaixos.Add($"{p.Nome}  ·  {p.Estoque} em estoque (mínimo {p.Minimo})");
        if (r.ProdutosAbaixoDoMinimo > r.PioresEstoques.Count)
            EstoquesBaixos.Add($"e mais {r.ProdutosAbaixoDoMinimo - r.PioresEstoques.Count}...");

        DividaAlerta = r.DividaVencidaCentavos > 0;
        DividaTexto = r.ClientesComDivida == 0
            ? "Nenhuma dívida em aberto"
            : $"{Dinheiro.Formatar(r.DividaTotalCentavos)} a receber";
        DividaDetalhe = r.ClientesComDivida == 0
            ? ""
            : Clientes(r.ClientesComDivida) + (r.DividaVencidaCentavos > 0
                ? $" · {Dinheiro.Formatar(r.DividaVencidaCentavos)} vencido ({Clientes(r.ClientesComDividaVencida)})"
                : " · nada vencido");

        AtualizarBackup();
        await AtualizarAgendaAsync();
    });

    private async Task AtualizarAgendaAsync()
    {
        if (_agenda is null) return;
        var hoje = (await _agenda.DoDiaAsync(_agora().Date)).Where(c => !c.Concluido);
        AgendaHoje.Clear();
        foreach (var c in hoje) AgendaHoje.Add($"{(c.DiaInteiro ? "Dia inteiro" : c.Inicio.ToString("HH:mm", PtBr))}  ·  {c.Titulo}");
        OnPropertyChanged(nameof(AgendaVazia));
    }

    private IReadOnlyList<AtalhoInicio> MontarAtalhos()
    {
        var todos = new (string Nome, string Dica, ItemMenu Item, Permissao? Exige)[]
        {
            ("PDV", "Vender (F1)", ItemMenu.Pdv, Permissao.Vender),
            ("Caixa", "Vendas do dia", ItemMenu.Caixa, Permissao.Caixa),
            ("Estoque", "Produtos e fotos", ItemMenu.EstoqueDaLoja, Permissao.Estoque),
            ("Clientes", "Cadastro e dívidas", ItemMenu.Clientes, Permissao.Clientes),
            ("Mais vendidos", "O que mais saiu", ItemMenu.MaisVendidos, Permissao.Relatorios),
            ("Relatórios", "Resultado e lucro", ItemMenu.Relatorios, Permissao.Relatorios),
            ("Agenda", "Compromissos", ItemMenu.Agenda, null),
            ("Configurações", "Loja, backup, Google", ItemMenu.Configuracoes, Permissao.Configuracoes),
        };
        return todos.Where(a => a.Exige is not { } p || Sessao.Pode(p))
            .Select(a => new AtalhoInicio(a.Nome, a.Dica, new RelayCommand(() => _abrir(a.Item)))).ToList();
    }

    private static string Clientes(int n) => n == 1 ? "1 cliente" : $"{n} clientes";

    private void AtualizarBackup()
    {
        if (_backup is null)
        {
            BackupTexto = "";
            return;
        }
        var c = _backup.Configuracao;
        var situacao = RegrasBackup.Situacao(c.UltimoBackup, _agora(), c.IntervaloHoras);
        BackupAlerta = situacao is SituacaoBackup.Nunca or SituacaoBackup.Atrasado;
        BackupTexto = situacao switch
        {
            SituacaoBackup.Nunca => "Nenhum backup feito ainda",
            SituacaoBackup.Atrasado => "Backup atrasado",
            SituacaoBackup.SoManual => "Backup automático desligado",
            _ => "Backup em dia"
        };
        BackupDetalhe = c.UltimoBackup is { } u ? $"Último: {u.ToString("dd/MM/yyyy HH:mm", PtBr)}" : "Faça um em Configurações";
    }

    // Atalhos: cada um só pede ao menu principal para abrir a tela.
    [RelayCommand] private void AbrirPdv() => _abrir(ItemMenu.Pdv);
    [RelayCommand] private void AbrirCaixa() => _abrir(ItemMenu.Caixa);
    [RelayCommand] private void AbrirFechamento() => _abrir(ItemMenu.Fechamento);
    [RelayCommand] private void AbrirMaisVendidos() => _abrir(ItemMenu.MaisVendidos);
    [RelayCommand] private void AbrirEstoque() => _abrir(ItemMenu.EstoqueDaLoja);
    [RelayCommand] private void AbrirEtiquetas() => _abrir(ItemMenu.Etiquetas);
    [RelayCommand] private void AbrirClientes() => _abrir(ItemMenu.Clientes);
    [RelayCommand] private void AbrirRelatorios() => _abrir(ItemMenu.Relatorios);
    [RelayCommand] private void AbrirCatalogoOnline() => _abrir(ItemMenu.CatalogoOnline);
    [RelayCommand] private void AbrirConfiguracoes() => _abrir(ItemMenu.Configuracoes);
    [RelayCommand] private void AbrirAgenda() => _abrir(ItemMenu.Agenda);
}

/// <summary>Um botão grande de atalho na tela Início.</summary>
public sealed record AtalhoInicio(string Nome, string Dica, System.Windows.Input.ICommand Abrir);
