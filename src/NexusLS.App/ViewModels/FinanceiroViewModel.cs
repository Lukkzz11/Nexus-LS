using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoFormaFinanceira(FormaPagamento Forma, string Nome);

public record OpcaoFiltro<T>(T? Valor, string Nome) where T : struct;

/// <summary>Como o lançamento conta no resultado, com o texto de ajuda para o formulário.</summary>
public record OpcaoClassificacao(ClassificacaoLancamento Valor, string Nome, string Ajuda);

/// <summary>Uma categoria na lista de configuração.</summary>
public record LinhaCategoriaFinanceira(int Id, string Nome, string TipoTexto, bool Ativa)
{
    public string SituacaoTexto => Ativa ? "Em uso" : "Desativada";
    public string TextoAlternar => Ativa ? "Desativar" : "Reativar";
}

/// <summary>Uma linha da lista de contas, com os textos prontos.</summary>
public record LinhaLancamento(Lancamento Lancamento, SituacaoLancamento Situacao)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public int Id => Lancamento.Id;
    public string Vencimento => Lancamento.Vencimento.ToString("dd/MM/yyyy", PtBr);
    public string Descricao => Lancamento.Descricao;
    public string Detalhe => string.Join(" · ", new[] { Lancamento.Categoria, Lancamento.Contraparte, ClassificacaoTexto }.Where(t => !string.IsNullOrEmpty(t)));
    private string? ClassificacaoTexto => Lancamento.Classificacao == ClassificacaoLancamento.Padrao
        ? null : RegrasFinanceiras.NomeClassificacao(Lancamento.Tipo, Lancamento.Classificacao);
    public string TipoTexto => Lancamento.Tipo == TipoLancamento.Despesa ? "A pagar" : "A receber";
    public string ValorTexto => (Lancamento.Tipo == TipoLancamento.Despesa ? "−" : "+") + Dinheiro.Formatar(Lancamento.ValorCentavos);
    public string SituacaoTexto => Situacao switch
    {
        SituacaoLancamento.Pago => Lancamento.Tipo == TipoLancamento.Despesa ? "Pago" : "Recebido",
        SituacaoLancamento.Atrasado => "Atrasado",
        SituacaoLancamento.Cancelado => "Cancelado",
        _ => "Em aberto"
    };
    public bool Atrasado => Situacao == SituacaoLancamento.Atrasado;
}

/// <summary>
/// Tela "Financeiro": contas a pagar e a receber (com parcelamento), pagar/estornar/cancelar e o resumo do período
/// (vendas do PDV + lançamentos). Nada é apagado: cancela-se com motivo.
/// </summary>
public partial class FinanceiroViewModel : ViewModelBase
{
    private readonly FinanceiroService _financeiro;
    private readonly CategoriaFinanceiraService? _categoriasServico;
    private readonly Func<DateTime> _agora;
    private IReadOnlyList<string> _categoriasDespesa = RegrasFinanceiras.CategoriasDespesa;
    private IReadOnlyList<string> _categoriasReceita = RegrasFinanceiras.CategoriasReceita;
    private int _versao;
    private bool _definindoPeriodo;
    private IReadOnlyList<Lancamento> _ultimaLista = Array.Empty<Lancamento>();

    public FinanceiroViewModel(FinanceiroService financeiro, Func<DateTime>? agora = null, CategoriaFinanceiraService? categorias = null)
    {
        _financeiro = financeiro;
        _categoriasServico = categorias;
        _agora = agora ?? (() => DateTime.Now);
        var hoje = _agora().Date;
        _de = new DateTime(hoje.Year, hoje.Month, 1);
        _ate = _de.Value.AddMonths(1).AddDays(-1);
        _formVencimento = hoje;
        _formForma = Formas[0];
        _pagarForma = Formas[0];
        _filtroTipo = FiltrosTipo[0];
        _filtroSituacao = FiltrosSituacao[0];
        _formClassificacao = OpcoesClassificacao(true)[0];
        Carregamento = CarregarAsync();
    }

    /// <summary>A primeira carga (os testes esperam por ela).</summary>
    public Task Carregamento { get; }

    // As caixas de janela ficam na View.
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;
    public Func<string, string?> PedirMotivo { get; set; } = _ => null;
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    public IReadOnlyList<OpcaoFormaFinanceira> Formas { get; } = SelecaoPagamento.Formas
        .Where(f => f != FormaPagamento.APrazo).Select(f => new OpcaoFormaFinanceira(f, SelecaoPagamento.Nome(f))).ToList();

    public IReadOnlyList<OpcaoFiltro<TipoLancamento>> FiltrosTipo { get; } = new OpcaoFiltro<TipoLancamento>[]
    {
        new(null, "Tudo"), new(TipoLancamento.Despesa, "A pagar"), new(TipoLancamento.Receita, "A receber")
    };

    public IReadOnlyList<OpcaoFiltro<SituacaoLancamento>> FiltrosSituacao { get; } = new OpcaoFiltro<SituacaoLancamento>[]
    {
        new(null, "Todas"), new(SituacaoLancamento.Aberto, "Em aberto"), new(SituacaoLancamento.Atrasado, "Atrasadas"),
        new(SituacaoLancamento.Pago, "Pagas / recebidas"), new(SituacaoLancamento.Cancelado, "Canceladas")
    };

    public ObservableCollection<LinhaLancamento> Linhas { get; } = new();
    public ObservableCollection<string> DespesasPorCategoria { get; } = new();

    public IReadOnlyList<string> Categorias => FormEhDespesa ? _categoriasDespesa : _categoriasReceita;

    /// <summary>As classificações que valem para o tipo escolhido (a primeira é a comum, que conta no resultado).</summary>
    public IReadOnlyList<OpcaoClassificacao> Classificacoes => OpcoesClassificacao(FormEhDespesa);

    private static IReadOnlyList<OpcaoClassificacao> OpcoesClassificacao(bool despesa)
    {
        var tipo = despesa ? TipoLancamento.Despesa : TipoLancamento.Receita;
        return RegrasFinanceiras.ClassificacoesDe(tipo)
            .Select(c => new OpcaoClassificacao(c, RegrasFinanceiras.NomeClassificacao(tipo, c), RegrasFinanceiras.AjudaClassificacao(tipo, c))).ToList();
    }

    public ObservableCollection<LinhaCategoriaFinanceira> CategoriasGerenciadas { get; } = new();
    [ObservableProperty] private string _novaCategoria = "";
    [ObservableProperty] private bool _novaCategoriaEhDespesa = true;

    // ----- Período e filtros -----
    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;
    [ObservableProperty] private OpcaoFiltro<TipoLancamento>? _filtroTipo;
    [ObservableProperty] private OpcaoFiltro<SituacaoLancamento>? _filtroSituacao;
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private LinhaLancamento? _selecionada;

    partial void OnDeChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnAteChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnFiltroTipoChanged(OpcaoFiltro<TipoLancamento>? value) => _ = CarregarAsync();
    partial void OnFiltroSituacaoChanged(OpcaoFiltro<SituacaoLancamento>? value) => _ = CarregarAsync();
    partial void OnBuscaChanged(string value) => _ = CarregarAsync();

    // ----- Resumo -----
    [ObservableProperty] private string _vendasTexto = "";
    [ObservableProperty] private string _lucroBrutoTexto = "";
    [ObservableProperty] private string _receitasTexto = "";
    [ObservableProperty] private string _despesasTexto = "";
    [ObservableProperty] private string _resultadoTexto = "";
    [ObservableProperty] private bool _resultadoNegativo;
    [ObservableProperty] private string _aPagarTexto = "";
    [ObservableProperty] private string _aReceberTexto = "";
    [ObservableProperty] private string _entradasForaTexto = "";
    [ObservableProperty] private string _saidasForaTexto = "";
    [ObservableProperty] private bool _temForaDoResultado;
    [ObservableProperty] private string _atrasadosTexto = "";
    [ObservableProperty] private bool _temAtrasados;

    // ----- Formulário -----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Categorias))]
    [NotifyPropertyChangedFor(nameof(Classificacoes))]
    private bool _formEhDespesa = true;
    [ObservableProperty] private OpcaoClassificacao _formClassificacao;
    partial void OnFormEhDespesaChanged(bool value) => FormClassificacao = OpcoesClassificacao(value)[0];
    [ObservableProperty] private string _formDescricao = "";
    [ObservableProperty] private string _formCategoria = "";
    [ObservableProperty] private string _formContraparte = "";
    [ObservableProperty] private string _formValor = "";
    [ObservableProperty] private DateTime? _formVencimento;
    [ObservableProperty] private string _formParcelas = "1";
    [ObservableProperty] private bool _formJaPago;
    [ObservableProperty] private OpcaoFormaFinanceira _formForma;
    [ObservableProperty] private string _formObservacao = "";
    [ObservableProperty] private int? _editandoId;

    [ObservableProperty] private OpcaoFormaFinanceira _pagarForma;

    public bool Editando => EditandoId is not null;
    public bool PodeParcelar => !Editando;
    public string TituloFormulario => Editando ? "Editar lançamento" : "Novo lançamento";
    partial void OnEditandoIdChanged(int? value)
    {
        OnPropertyChanged(nameof(Editando));
        OnPropertyChanged(nameof(PodeParcelar));
        OnPropertyChanged(nameof(TituloFormulario));
    }

    // ----- Seleção: o que dá para fazer com a linha escolhida -----
    public bool PodePagar => Selecionada is { Situacao: SituacaoLancamento.Aberto or SituacaoLancamento.Atrasado };
    public bool PodeEstornar => Selecionada is { Situacao: SituacaoLancamento.Pago };
    public bool PodeCancelar => PodePagar;
    public bool PodeEditar => PodePagar;
    public string TextoPagar => Selecionada?.Lancamento.Tipo == TipoLancamento.Receita ? "Marcar como recebido" : "Marcar como pago";

    partial void OnSelecionadaChanged(LinhaLancamento? value)
    {
        OnPropertyChanged(nameof(PodePagar));
        OnPropertyChanged(nameof(PodeEstornar));
        OnPropertyChanged(nameof(PodeCancelar));
        OnPropertyChanged(nameof(PodeEditar));
        OnPropertyChanged(nameof(TextoPagar));
    }

    // ================= período (atalhos) =================

    [RelayCommand] private void MesAtual() => DefinirPeriodo(Primeiro(_agora().Date), Primeiro(_agora().Date).AddMonths(1).AddDays(-1));
    [RelayCommand] private void MesAnterior() { var p = Primeiro(_agora().Date).AddMonths(-1); DefinirPeriodo(p, p.AddMonths(1).AddDays(-1)); }
    [RelayCommand] private void ProximosTrintaDias() => DefinirPeriodo(_agora().Date, _agora().Date.AddDays(30));

    private static DateTime Primeiro(DateTime d) => new(d.Year, d.Month, 1);

    private void DefinirPeriodo(DateTime de, DateTime ate)
    {
        _definindoPeriodo = true;
        De = de;
        Ate = ate;
        _definindoPeriodo = false;
        _ = CarregarAsync();
    }

    // ================= carregar =================

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");
        if (ate < de) throw new RegraDeNegocioException("A data final não pode ser antes da inicial.");

        var lista = await _financeiro.ListarAsync(new FiltroLancamentos(de, ate, FiltroTipo?.Valor, FiltroSituacao?.Valor, Busca));
        var resumo = await _financeiro.ResumoAsync(de, ate);
        if (_categoriasServico is not null) await CarregarCategoriasAsync();
        if (versao != _versao) return; // chegou uma consulta mais nova: esta resposta já é velha

        var hoje = _agora();
        var idSelecionado = Selecionada?.Id;
        _ultimaLista = lista;
        Linhas.Clear();
        foreach (var l in lista) Linhas.Add(new LinhaLancamento(l, RegrasFinanceiras.Situacao(l, hoje)));
        Selecionada = Linhas.FirstOrDefault(l => l.Id == idSelecionado);

        VendasTexto = Dinheiro.Formatar(resumo.VendasCentavos);
        LucroBrutoTexto = Dinheiro.Formatar(resumo.LucroBrutoCentavos);
        ReceitasTexto = Dinheiro.Formatar(resumo.ReceitasRecebidasCentavos);
        DespesasTexto = Dinheiro.Formatar(resumo.DespesasPagasCentavos);
        ResultadoTexto = Dinheiro.Formatar(resumo.ResultadoCentavos);
        ResultadoNegativo = resumo.ResultadoCentavos < 0;
        APagarTexto = Dinheiro.Formatar(resumo.APagarCentavos);
        AReceberTexto = Dinheiro.Formatar(resumo.AReceberCentavos);
        TemForaDoResultado = resumo.EntradasForaDoResultadoCentavos + resumo.SaidasForaDoResultadoCentavos > 0;
        EntradasForaTexto = $"Entradas fora do resultado (aportes e outras): {Dinheiro.Formatar(resumo.EntradasForaDoResultadoCentavos)}";
        SaidasForaTexto = $"Saídas fora do resultado (retiradas e outras): {Dinheiro.Formatar(resumo.SaidasForaDoResultadoCentavos)}";
        TemAtrasados = resumo.QuantidadeAtrasados > 0;
        AtrasadosTexto = TemAtrasados
            ? $"{resumo.QuantidadeAtrasados} conta(s) atrasada(s): {Dinheiro.Formatar(resumo.AtrasadosCentavos)}"
            : "Nenhuma conta atrasada";
        DespesasPorCategoria.Clear();
        foreach (var c in resumo.DespesasPorCategoria) DespesasPorCategoria.Add($"{c.Categoria}: {Dinheiro.Formatar(c.TotalCentavos)}");
    });

    // ================= categorias =================

    private async Task CarregarCategoriasAsync()
    {
        var todas = await _categoriasServico!.ListarAsync(incluirInativas: true);
        _categoriasDespesa = todas.Where(c => c.Tipo == TipoLancamento.Despesa && c.Ativa).Select(c => c.Nome).ToList();
        _categoriasReceita = todas.Where(c => c.Tipo == TipoLancamento.Receita && c.Ativa).Select(c => c.Nome).ToList();
        CategoriasGerenciadas.Clear();
        foreach (var c in todas)
            CategoriasGerenciadas.Add(new LinhaCategoriaFinanceira(c.Id, c.Nome, c.Tipo == TipoLancamento.Despesa ? "A pagar" : "A receber", c.Ativa));
        OnPropertyChanged(nameof(Categorias));
    }

    [RelayCommand]
    private Task AdicionarCategoriaAsync() => ExecutarAsync(async () =>
    {
        if (_categoriasServico is null) return;
        await _categoriasServico.CriarAsync(NovaCategoriaEhDespesa ? TipoLancamento.Despesa : TipoLancamento.Receita, NovaCategoria);
        NovaCategoria = "";
        await CarregarCategoriasAsync();
        MostrarSucesso("Categoria salva.");
    });

    [RelayCommand]
    private Task AlternarCategoriaAsync(LinhaCategoriaFinanceira? linha) => ExecutarAsync(async () =>
    {
        if (_categoriasServico is null || linha is null) return;
        await _categoriasServico.DefinirAtivaAsync(linha.Id, !linha.Ativa);
        await CarregarCategoriasAsync();
    });

    // ================= formulário =================

    [RelayCommand]
    private void Novo()
    {
        EditandoId = null;
        FormDescricao = FormCategoria = FormContraparte = FormValor = FormObservacao = "";
        FormParcelas = "1";
        FormJaPago = false;
        FormClassificacao = Classificacoes[0];
        FormVencimento = _agora().Date;
        Mensagem = "";
    }

    [RelayCommand]
    private void EditarSelecionada()
    {
        if (Selecionada is not { } s || !PodeEditar) return;
        var l = s.Lancamento;
        EditandoId = l.Id;
        FormEhDespesa = l.Tipo == TipoLancamento.Despesa;
        FormClassificacao = Classificacoes.First(c => c.Valor == l.Classificacao);
        FormDescricao = l.Descricao;
        FormCategoria = l.Categoria ?? "";
        FormContraparte = l.Contraparte ?? "";
        FormValor = Dinheiro.FormatarParaEdicao(l.ValorCentavos);
        FormVencimento = l.Vencimento;
        FormObservacao = l.Observacao ?? "";
        FormJaPago = false;
        FormParcelas = "1";
        Mensagem = "";
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        if (!Dinheiro.TentarConverter(FormValor, out var valor))
            throw new RegraDeNegocioException("Informe o valor. Exemplo: 150,00");
        if (FormVencimento is not DateTime vencimento)
            throw new RegraDeNegocioException("Informe a data de vencimento.");

        string sucesso;
        if (EditandoId is int id)
        {
            await _financeiro.EditarAsync(id, FormDescricao, valor, vencimento, FormCategoria, FormContraparte, FormObservacao, FormClassificacao.Valor);
            sucesso = "Lançamento atualizado.";
        }
        else
        {
            if (!int.TryParse(FormParcelas, out var parcelas)) throw new RegraDeNegocioException("Informe quantas parcelas (1 para à vista).");
            var criados = await _financeiro.CriarAsync(new DadosLancamento(
                FormEhDespesa ? TipoLancamento.Despesa : TipoLancamento.Receita, FormDescricao, valor, vencimento,
                FormCategoria, FormContraparte, FormObservacao, parcelas, FormJaPago, FormForma.Forma, FormClassificacao.Valor));
            sucesso = criados.Count > 1 ? $"{criados.Count} parcelas lançadas." : "Lançamento salvo.";
        }
        Novo();
        await CarregarAsync();
        MostrarSucesso(sucesso);
    });

    // ================= ações sobre a linha escolhida =================

    [RelayCommand]
    private Task PagarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { } s || !PodePagar) return;
        await _financeiro.PagarAsync(s.Id, PagarForma.Forma);
        MostrarSucesso(s.Lancamento.Tipo == TipoLancamento.Receita ? "Recebimento registrado." : "Pagamento registrado.");
        await CarregarAsync();
    });

    [RelayCommand]
    private Task EstornarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { } s || !PodeEstornar) return;
        if (!Confirmar($"Estornar o pagamento de \"{s.Descricao}\"? Ele volta para \"em aberto\".")) return;
        await _financeiro.EstornarPagamentoAsync(s.Id);
        MostrarSucesso("Pagamento estornado.");
        await CarregarAsync();
    });

    [RelayCommand]
    private Task CancelarLancamentoAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { } s || !PodeCancelar) return;
        var motivo = PedirMotivo($"Por que cancelar \"{s.Descricao}\"?");
        if (motivo is null) return;
        await _financeiro.CancelarAsync(s.Id, motivo);
        MostrarSucesso("Lançamento cancelado (continua no histórico).");
        await CarregarAsync();
    });

    [RelayCommand]
    private Task ExportarAsync() => ExecutarAsync(() =>
    {
        var caminho = EscolherArquivoParaSalvar($"financeiro_{De:yyyy-MM-dd}_{Ate:yyyy-MM-dd}.csv");
        if (caminho is null) return Task.CompletedTask;
        ExportadorCsv.Salvar(caminho, ExportadorCsv.Lancamentos(_ultimaLista, _agora()));
        MostrarSucesso($"Arquivo salvo: {Path.GetFileName(caminho)}");
        return Task.CompletedTask;
    });
}
