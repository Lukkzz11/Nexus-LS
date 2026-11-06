using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
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

    /// <summary>Barras do gráfico dos últimos 7 dias (do mais antigo para hoje).</summary>
    public ObservableCollection<BarraDiaInicio> UltimosDias { get; } = new();

    /// <summary>Os mais vendidos da semana, por peças.</summary>
    public ObservableCollection<LinhaRankingInicio> MaisVendidosSemana { get; } = new();

    /// <summary>Contas a pagar atrasadas ou que vencem nos próximos dias (só para quem pode ver o Financeiro).</summary>
    public ObservableCollection<LinhaContaInicio> ContasAPagar { get; } = new();

    /// <summary>A agenda de hoje separada em horário e título, para a tela (o texto pronto fica em <see cref="AgendaHoje"/>).</summary>
    public ObservableCollection<LinhaAgendaInicio> AgendaLinhas { get; } = new();

    /// <summary>Compromissos de hoje que ainda não foram concluídos, em ordem de horário.</summary>
    public ObservableCollection<string> AgendaHoje { get; } = new();

    /// <summary>Só os atalhos que a pessoa que entrou pode usar.</summary>
    public IReadOnlyList<AtalhoInicio> Atalhos { get; }

    public bool TemAgenda => _agenda is not null;
    public bool AgendaVazia => AgendaHoje.Count == 0;

    /// <summary>Nada precisa de atenção agora (estoque, dívida vencida, backup).</summary>
    public bool TudoEmDia => !EstoqueBaixoAlerta && !DividaAlerta && !BackupAlerta;

    /// <summary>O quadro de contas a pagar só aparece para quem pode abrir o Financeiro.</summary>
    public bool PodeVerFinanceiro { get; } = Sessao.Pode(Permissao.Financeiro);

    /// <summary>O botão do ranking completo só aparece para quem pode abrir o Mais vendidos.</summary>
    public bool PodeVerMaisVendidos { get; } = Sessao.Pode(Permissao.Relatorios);

    public bool SemanaVazia => UltimosDias.All(d => d.TotalCentavos == 0);
    public bool MaisVendidosVazio => MaisVendidosSemana.Count == 0;
    public bool ContasVazias => ContasAPagar.Count == 0;

    [ObservableProperty] private string _saudacao = "";

    /// <summary>O cartão de backup só aparece quando há gestor de backup.</summary>
    public bool TemBackup => _backup is not null;

    [ObservableProperty] private string _dataTexto = "";

    [ObservableProperty] private string _vendasHojeTexto = "R$ 0,00";
    [ObservableProperty] private string _vendasHojeDetalhe = "";
    [ObservableProperty] private string _comparacaoOntemTexto = "";
    [ObservableProperty] private string _ticketMedioTexto = "R$ 0,00";
    [ObservableProperty] private string _pecasHojeTexto = "0";
    [ObservableProperty] private string _pecasHojeDetalhe = "";

    [ObservableProperty] private string _semanaTotalTexto = "R$ 0,00";
    [ObservableProperty] private string _semanaDetalhe = "";

    [ObservableProperty] private string _contasResumoTexto = "";
    [ObservableProperty] private bool _contasAtrasadas;

    [ObservableProperty] private string _caixaTexto = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaixaFechado))]
    private bool _caixaAberto;

    /// <summary>Só para a bolinha do cartão do caixa (laranja quando fechado).</summary>
    public bool CaixaFechado => !CaixaAberto;

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
        PecasHojeTexto = r.PecasHoje.ToString("N0", PtBr);
        PecasHojeDetalhe = r.VendasHoje == 0
            ? "nenhuma peça saiu hoje"
            : $"{(r.PecasHoje / (double)r.VendasHoje).ToString("0.#", PtBr)} por venda, em média";

        AtualizarSemana(r);
        AtualizarContas(r);

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
        AgendaLinhas.Clear();
        foreach (var c in hoje)
        {
            var hora = c.DiaInteiro ? "Dia inteiro" : c.Inicio.ToString("HH:mm", PtBr);
            AgendaHoje.Add($"{hora}  ·  {c.Titulo}");
            AgendaLinhas.Add(new LinhaAgendaInicio(hora, c.Titulo, !c.DiaInteiro && c.Inicio < _agora()));
        }
        OnPropertyChanged(nameof(AgendaVazia));
    }

    private void AtualizarSemana(ResumoInicio r)
    {
        var maior = r.UltimosDias.Count == 0 ? 0 : r.UltimosDias.Max(d => d.TotalCentavos);
        UltimosDias.Clear();
        foreach (var d in r.UltimosDias)
        {
            var ehHoje = d.Dia == r.Dia;
            var semana = d.Dia.ToString("ddd", PtBr).TrimEnd('.');
            UltimosDias.Add(new BarraDiaInicio(
                ehHoje ? "Hoje" : char.ToUpper(semana[0], PtBr) + semana[1..],
                d.Dia.ToString("dd/MM", PtBr),
                d.TotalCentavos == 0 ? "" : Compacto(d.TotalCentavos),
                $"{d.Dia.ToString("dddd, dd/MM", PtBr)}: {Dinheiro.Formatar(d.TotalCentavos)} em {Vendas(d.Vendas)}",
                d.TotalCentavos,
                Fracao(d.TotalCentavos, maior),
                ehHoje));
        }
        var total = r.UltimosDias.Sum(d => d.TotalCentavos);
        var quantas = r.UltimosDias.Sum(d => d.Vendas);
        SemanaTotalTexto = Dinheiro.Formatar(total);
        SemanaDetalhe = quantas == 0
            ? "Nenhuma venda nos últimos 7 dias"
            : $"{Vendas(quantas)} · média de {Dinheiro.Formatar((long)Math.Round(total / (decimal)InicioService.DiasDaSemana, MidpointRounding.AwayFromZero))} por dia";
        OnPropertyChanged(nameof(SemanaVazia));

        var maisPecas = r.MaisVendidosSemana.Count == 0 ? 0 : r.MaisVendidosSemana.Max(p => p.Quantidade);
        MaisVendidosSemana.Clear();
        var posicao = 1;
        foreach (var p in r.MaisVendidosSemana)
            MaisVendidosSemana.Add(new LinhaRankingInicio(
                posicao++, p.Nome,
                p.Quantidade == 1 ? "1 peça" : $"{p.Quantidade.ToString("N0", PtBr)} peças",
                Dinheiro.Formatar(p.TotalLiquidoCentavos),
                Fracao(p.Quantidade, maisPecas)));
        OnPropertyChanged(nameof(MaisVendidosVazio));
    }

    private void AtualizarContas(ResumoInicio r)
    {
        ContasAPagar.Clear();
        if (PodeVerFinanceiro)
            foreach (var c in r.ContasAPagar)
                ContasAPagar.Add(new LinhaContaInicio(
                    c.Descricao,
                    QuandoVence(c.Vencimento, r.Dia) + (string.IsNullOrWhiteSpace(c.Contraparte) ? "" : $" · {c.Contraparte}"),
                    Dinheiro.Formatar(c.ValorCentavos),
                    c.Atrasada));
        ContasAtrasadas = PodeVerFinanceiro && r.ContasAtrasadasCentavos > 0;
        ContasResumoTexto = r.ContasAPagarQuantidade switch
        {
            0 => "Nada vence nos próximos 7 dias",
            1 => $"{Dinheiro.Formatar(r.ContasAPagarTotalCentavos)} em 1 conta",
            _ => $"{Dinheiro.Formatar(r.ContasAPagarTotalCentavos)} em {r.ContasAPagarQuantidade} contas"
        } + (r.ContasAtrasadasCentavos > 0 ? $" · {Dinheiro.Formatar(r.ContasAtrasadasCentavos)} atrasado" : "")
          + (r.ContasAPagarQuantidade > r.ContasAPagar.Count ? $" · mostrando {r.ContasAPagar.Count}" : "");
        OnPropertyChanged(nameof(ContasVazias));
    }

    /// <summary>"venceu há 3 dias", "venceu ontem", "vence hoje", "vence amanhã", "vence sex, 23/10".</summary>
    public static string QuandoVence(DateTime vencimento, DateTime hoje)
    {
        var dias = (vencimento.Date - hoje.Date).Days;
        return dias switch
        {
            < -1 => $"venceu há {-dias} dias",
            -1 => "venceu ontem",
            0 => "vence hoje",
            1 => "vence amanhã",
            _ => $"vence {vencimento.ToString("ddd", PtBr).TrimEnd('.')}, {vencimento.ToString("dd/MM", PtBr)}"
        };
    }

    /// <summary>Valor curto para cima das barras: "R$ 1.234" (sem centavos) ou, de 10 mil para cima, "R$ 12,5 mil".</summary>
    public static string Compacto(long centavos)
    {
        var reais = centavos / 100m;
        return reais >= 10_000m
            ? $"R$ {Math.Round(reais / 1000m, 1, MidpointRounding.AwayFromZero).ToString("0.#", PtBr)} mil"
            : $"R$ {Math.Round(reais, MidpointRounding.AwayFromZero).ToString("N0", PtBr)}";
    }

    // Barra mínima visível quando houve venda, para o dia fraco não parecer "sem venda".
    private static double Fracao(long valor, long maior) =>
        maior <= 0 || valor <= 0 ? 0 : Math.Max(0.03, valor / (double)maior);

    private static string Vendas(int n) => n == 1 ? "1 venda" : $"{n} vendas";

    private IReadOnlyList<AtalhoInicio> MontarAtalhos()
    {
        var todos = new (string Nome, string Dica, string Icone, ItemMenu Item, Permissao? Exige)[]
        {
            ("PDV", "Vender (F1)", "Cart24", ItemMenu.Pdv, Permissao.Vender),
            ("Caixa", "Vendas do dia", "Money24", ItemMenu.Caixa, Permissao.Caixa),
            ("Estoque", "Produtos e fotos", "Box24", ItemMenu.EstoqueDaLoja, Permissao.Estoque),
            ("Clientes", "Cadastro e dívidas", "People24", ItemMenu.Clientes, Permissao.Clientes),
            ("Mais vendidos", "O que mais saiu", "ArrowTrending24", ItemMenu.MaisVendidos, Permissao.Relatorios),
            ("Relatórios", "Resultado e lucro", "ChartMultiple24", ItemMenu.Relatorios, Permissao.Relatorios),
            ("Agenda", "Compromissos", "CalendarLtr24", ItemMenu.Agenda, null),
            ("Configurações", "Loja, backup, Google", "Settings24", ItemMenu.Configuracoes, Permissao.Configuracoes),
        };
        return todos.Where(a => a.Exige is not { } p || Sessao.Pode(p))
            .Select(a => new AtalhoInicio(a.Nome, a.Dica, new RelayCommand(() => _abrir(a.Item)), a.Icone)).ToList();
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
    [RelayCommand] private void AbrirFechamento() => _abrir(ItemMenu.Caixa);
    [RelayCommand] private void AbrirMaisVendidos() => _abrir(ItemMenu.MaisVendidos);
    [RelayCommand] private void AbrirEstoque() => _abrir(ItemMenu.EstoqueDaLoja);
    [RelayCommand] private void AbrirEtiquetas() => _abrir(ItemMenu.Etiquetas);
    [RelayCommand] private void AbrirClientes() => _abrir(ItemMenu.Clientes);
    [RelayCommand] private void AbrirRelatorios() => _abrir(ItemMenu.Relatorios);
    [RelayCommand] private void AbrirCatalogoOnline() => _abrir(ItemMenu.CatalogoOnline);
    [RelayCommand] private void AbrirConfiguracoes() => _abrir(ItemMenu.Configuracoes);
    [RelayCommand] private void AbrirAgenda() => _abrir(ItemMenu.Agenda);
    [RelayCommand] private void AbrirFinanceiro() => _abrir(ItemMenu.Financeiro);
}

/// <summary>Uma barra do gráfico "últimos 7 dias". <see cref="Fracao"/> vai de 0 a 1 (1 = o melhor dia da janela).</summary>
public sealed record BarraDiaInicio(string Rotulo, string Data, string ValorTexto, string Dica, long TotalCentavos, double Fracao, bool EhHoje)
{
    public GridLength Cheio => new(Fracao, GridUnitType.Star);
    public GridLength Vazio => new(1 - Fracao, GridUnitType.Star);
}

/// <summary>Um produto do ranking da semana na tela Início.</summary>
public sealed record LinhaRankingInicio(int Posicao, string Nome, string PecasTexto, string ValorTexto, double Fracao)
{
    public GridLength Cheio => new(Fracao, GridUnitType.Star);
    public GridLength Vazio => new(1 - Fracao, GridUnitType.Star);
}

/// <summary>Uma conta a pagar na tela Início.</summary>
public sealed record LinhaContaInicio(string Descricao, string Detalhe, string ValorTexto, bool Atrasada);

/// <summary>Um compromisso de hoje na tela Início. <see cref="JaPassou"/> = o horário já passou e ainda não foi concluído.</summary>
public sealed record LinhaAgendaInicio(string Hora, string Titulo, bool JaPassou);

/// <summary>Um botão grande de atalho na tela Início.</summary>
public sealed record AtalhoInicio(string Nome, string Dica, System.Windows.Input.ICommand Abrir, string Icone = "Apps24");
