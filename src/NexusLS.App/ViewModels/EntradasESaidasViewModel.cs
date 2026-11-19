using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha da lista de entradas e saídas, com os textos prontos.</summary>
public sealed record LinhaLancamentoEmpresa(int Id, string Data, string Tipo, string Descricao, string Categoria, string Forma, string Valor,
    string Anexos, bool EhEntrada);

/// <summary>Um arquivo anexado ao lançamento escolhido.</summary>
public sealed record LinhaAnexo(int Id, string Nome, string Detalhe, bool Existe, string CaminhoRelativo)
{
    public string Aviso => Existe ? "" : "O arquivo não está mais na pasta de anexos.";
}

/// <summary>
/// Entradas e saídas da empresa: lista com filtros, lançamento novo ou editado, exclusão e anexos (foto ou documento).
/// Só quem tem a permissão Financeiro usa. Excluir um lançamento com anexos pede confirmação e apaga os arquivos junto.
/// </summary>
public partial class EntradasESaidasViewModel : PeriodoViewModelBase
{
    private const string Todas = "Todas";
    private static readonly string[] SugestoesDeSaida = { "Aluguel", "Energia", "Água", "Internet", "Salários", "Impostos", "Manutenção", "Fornecedores", "Outras saídas" };
    private static readonly string[] SugestoesDeEntrada = { "Aporte do dono", "Empréstimo", "Outras entradas" };

    private readonly LancamentoEmpresaService _servico;
    private readonly AnexoService _anexos;
    private readonly Func<bool> _podeVer;
    private readonly Func<DateTime> _agora;
    private int _versao;
    private bool _carregandoFormulario;
    private bool _preenchendoCategorias;

    public EntradasESaidasViewModel(LancamentoEmpresaService servico, AnexoService anexos, Func<DateTime>? hoje = null, Func<bool>? podeVerValores = null)
        : base(hoje ?? (() => DateTime.Now))
    {
        _servico = servico;
        _anexos = anexos;
        _agora = hoje ?? (() => DateTime.Now);
        _podeVer = podeVerValores ?? (() => Sessao.Pode(Permissao.Financeiro));
        _formData = _agora().Date;
        Carregamento = AtualizarAsync();
    }

    // ----- janelas do Windows (a tela liga; sem isso nada acontece) -----

    /// <summary>Abre o seletor de arquivo e devolve o caminho (nulo = cancelou).</summary>
    public Func<string?> EscolherArquivoAnexo { get; set; } = () => null;
    /// <summary>Abre um arquivo no programa padrão do Windows.</summary>
    public Action<string> AbrirArquivo { get; set; } = _ => { };
    /// <summary>Pergunta sim ou não. O padrão é "não": sem tela, nada é apagado.</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => false;

    // ----- lista e filtros -----

    public ObservableCollection<LinhaLancamentoEmpresa> Linhas { get; } = new();
    public ObservableCollection<string> OpcoesCategoria { get; } = new() { Todas };
    public ObservableCollection<string> CategoriasDoFormulario { get; } = new();

    public IReadOnlyList<OpcaoFiltro<TipoMovimentoEmpresa>> OpcoesTipo { get; } = new[]
    {
        new OpcaoFiltro<TipoMovimentoEmpresa>(null, "Entradas e saídas"),
        new OpcaoFiltro<TipoMovimentoEmpresa>(TipoMovimentoEmpresa.Entrada, "Só entradas"),
        new OpcaoFiltro<TipoMovimentoEmpresa>(TipoMovimentoEmpresa.Saida, "Só saídas")
    };

    public IReadOnlyList<string> OpcoesAnexo { get; } = new[] { "Com e sem anexo", "Só com anexo", "Só sem anexo" };

    public IReadOnlyList<OpcaoFiltro<FormaPagamento>> OpcoesForma { get; } = new[]
    {
        new OpcaoFiltro<FormaPagamento>(null, "Não informar"),
        new OpcaoFiltro<FormaPagamento>(FormaPagamento.Dinheiro, "Dinheiro"),
        new OpcaoFiltro<FormaPagamento>(FormaPagamento.Pix, "Pix"),
        new OpcaoFiltro<FormaPagamento>(FormaPagamento.CartaoDebito, "Cartão de débito"),
        new OpcaoFiltro<FormaPagamento>(FormaPagamento.CartaoCredito, "Cartão de crédito"),
        new OpcaoFiltro<FormaPagamento>(FormaPagamento.Cheque, "Cheque")
    };

    [ObservableProperty] private OpcaoFiltro<TipoMovimentoEmpresa> _filtroTipo = new(null, "Entradas e saídas");
    [ObservableProperty] private string _filtroCategoria = Todas;
    [ObservableProperty] private string _filtroAnexo = "Com e sem anexo";
    [ObservableProperty] private string _filtroTexto = "";
    [ObservableProperty] private LinhaLancamentoEmpresa? _selecionada;

    [ObservableProperty] private string _totalEntradas = "";
    [ObservableProperty] private string _totalSaidas = "";
    [ObservableProperty] private string _saldo = "";
    [ObservableProperty] private bool _saldoNegativo;
    [ObservableProperty] private string _contagem = "";
    [ObservableProperty] private bool _semPermissao;
    [ObservableProperty] private bool _vazio;

    partial void OnFiltroTipoChanged(OpcaoFiltro<TipoMovimentoEmpresa> value) => FiltroMudou();
    partial void OnFiltroCategoriaChanged(string value) => FiltroMudou();
    partial void OnFiltroAnexoChanged(string value) => FiltroMudou();
    partial void OnFiltroTextoChanged(string value) => FiltroMudou();
    private void FiltroMudou() { if (!_preenchendoCategorias) Carregamento = AtualizarAsync(); }
    protected override void PeriodoMudou() => Carregamento = AtualizarAsync();

    [RelayCommand]
    private void LimparFiltros()
    {
        _preenchendoCategorias = true;
        FiltroTipo = OpcoesTipo[0]; FiltroCategoria = Todas; FiltroAnexo = OpcoesAnexo[0]; FiltroTexto = "";
        _preenchendoCategorias = false;
        Carregamento = AtualizarAsync();
    }

    private FiltroLancamentoEmpresa MontarFiltro(DesempenhoVendas.Intervalo p) => new(
        p.Inicio, p.Fim, FiltroTipo?.Valor,
        string.IsNullOrWhiteSpace(FiltroCategoria) || FiltroCategoria == Todas ? null : FiltroCategoria,
        FiltroAnexo == OpcoesAnexo[1] ? true : FiltroAnexo == OpcoesAnexo[2] ? false : null,
        string.IsNullOrWhiteSpace(FiltroTexto) ? null : FiltroTexto);

    [RelayCommand]
    private Task AtualizarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (!_podeVer())
        {
            Linhas.Clear(); SemPermissao = true; Vazio = false;
            TotalEntradas = TotalSaidas = Saldo = Contagem = "";
            MostrarErro("Você não tem permissão para ver os valores financeiros. Peça a um gerente ou administrador.");
            return;
        }
        SemPermissao = false;
        var periodo = PeriodoEscolhido();
        var lista = await _servico.ListarAsync(MontarFiltro(periodo));
        var categorias = await _servico.CategoriasAsync();
        if (versao != _versao) return;

        PreencherCategorias(categorias);
        var idSelecionado = Selecionada?.Id;
        Linhas.Clear();
        foreach (var l in lista) Linhas.Add(Montar(l));
        if (idSelecionado is int id) SelecionarSemRecarregar(Linhas.FirstOrDefault(x => x.Id == id));

        var (e, s, saldo) = LancamentoEmpresaService.Totais(lista);
        TotalEntradas = Dinheiro.Formatar(e);
        TotalSaidas = Dinheiro.Formatar(s);
        Saldo = Dinheiro.Formatar(saldo);
        SaldoNegativo = saldo < 0;
        Contagem = lista.Count == 1 ? "1 lançamento" : $"{lista.Count} lançamentos";
        Vazio = lista.Count == 0;
    });

    private void PreencherCategorias(List<string> usadas)
    {
        _preenchendoCategorias = true;
        var atual = FiltroCategoria;
        OpcoesCategoria.Clear();
        OpcoesCategoria.Add(Todas);
        foreach (var c in usadas) OpcoesCategoria.Add(c);
        if (atual != Todas && !OpcoesCategoria.Contains(atual)) OpcoesCategoria.Add(atual);
        FiltroCategoria = atual;
        _preenchendoCategorias = false;

        CategoriasDoFormulario.Clear();
        foreach (var c in usadas.Concat(FormEhEntrada ? SugestoesDeEntrada : SugestoesDeSaida).Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
            CategoriasDoFormulario.Add(c);
    }

    private static LinhaLancamentoEmpresa Montar(LancamentoEmpresa l)
    {
        var entrada = l.Tipo == TipoMovimentoEmpresa.Entrada;
        return new LinhaLancamentoEmpresa(l.Id, l.Data.ToString("dd/MM/yyyy", PtBr), entrada ? "Entrada" : "Saída", l.Descricao, l.Categoria,
            l.Forma is FormaPagamento f ? SelecaoPagamento.Nome(f) : "", (entrada ? "+ " : "− ") + Dinheiro.Formatar(l.ValorCentavos),
            l.Anexos.Count == 0 ? "" : l.Anexos.Count == 1 ? "1 anexo" : $"{l.Anexos.Count} anexos", entrada);
    }

    // ----- formulário -----

    [ObservableProperty] private int? _editandoId;
    [ObservableProperty] private bool _formEhEntrada;
    [ObservableProperty] private DateTime? _formData;
    [ObservableProperty] private string _formValorTexto = "";
    [ObservableProperty] private string _formCategoria = "";
    [ObservableProperty] private string _formDescricao = "";
    [ObservableProperty] private string _formObservacao = "";
    [ObservableProperty] private OpcaoFiltro<FormaPagamento> _formForma = new(null, "Não informar");

    public bool FormEhSaida
    {
        get => !FormEhEntrada;
        set { if (value) FormEhEntrada = false; OnPropertyChanged(); }
    }

    public string TituloFormulario => EditandoId is null ? "Novo lançamento" : $"Editando o lançamento nº {EditandoId}";
    public bool EstaEditando => EditandoId is not null;
    public bool PodeAnexar => EditandoId is not null;
    public string DicaAnexo => PodeAnexar
        ? "JPG, PNG, PDF e outros documentos, até 10 MB cada. O arquivo é copiado para a pasta de anexos do Nexus e entra no backup."
        : "Salve o lançamento primeiro. Depois você pode anexar a foto da nota ou o comprovante.";

    partial void OnEditandoIdChanged(int? value)
    {
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(EstaEditando));
        OnPropertyChanged(nameof(PodeAnexar));
        OnPropertyChanged(nameof(DicaAnexo));
    }

    partial void OnFormEhEntradaChanged(bool value)
    {
        OnPropertyChanged(nameof(FormEhSaida));
        if (_carregandoFormulario) return;
        // As sugestões de categoria acompanham o tipo escolhido.
        var usadas = CategoriasDoFormulario.Where(c => !SugestoesDeEntrada.Contains(c) && !SugestoesDeSaida.Contains(c));
        var novas = usadas.Concat(value ? SugestoesDeEntrada : SugestoesDeSaida).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        CategoriasDoFormulario.Clear();
        foreach (var c in novas) CategoriasDoFormulario.Add(c);
    }

    private bool _semRecarregar;

    /// <summary>Troca a linha escolhida sem reler o formulário (quando a lista só foi refeita).</summary>
    private void SelecionarSemRecarregar(LinhaLancamentoEmpresa? linha)
    {
        _semRecarregar = true;
        Selecionada = linha;
        _semRecarregar = false;
    }

    partial void OnSelecionadaChanged(LinhaLancamentoEmpresa? value)
    {
        if (value is not null && !_semRecarregar) _ = AbrirLancamentoAsync(value.Id);
    }

    /// <summary>Carrega um lançamento no formulário para editar (e mostra os anexos dele).</summary>
    public Task AbrirLancamentoAsync(int id) => ExecutarAsync(() => CarregarParaEdicaoAsync(id));

    [RelayCommand]
    private void Novo()
    {
        Selecionada = null;
        LimparFormulario();
    }

    private void LimparFormulario()
    {
        _carregandoFormulario = true;
        EditandoId = null;
        FormEhEntrada = false;
        FormData = _agora().Date;
        FormValorTexto = ""; FormCategoria = ""; FormDescricao = ""; FormObservacao = "";
        FormForma = OpcoesForma[0];
        _carregandoFormulario = false;
        OnPropertyChanged(nameof(FormEhSaida));
        AnexosDoLancamento.Clear();
        OnPropertyChanged(nameof(TemAnexos));
    }

    private async Task CarregarParaEdicaoAsync(int id)
    {
        var l = await _servico.ObterAsync(id);
        if (l is null) { MostrarErro("Esse lançamento não existe mais."); await AtualizarAsync(); return; }
        _carregandoFormulario = true;
        EditandoId = l.Id;
        FormEhEntrada = l.Tipo == TipoMovimentoEmpresa.Entrada;
        FormData = l.Data.Date;
        FormValorTexto = Dinheiro.FormatarParaEdicao(l.ValorCentavos);
        FormCategoria = l.Categoria;
        FormDescricao = l.Descricao;
        FormObservacao = l.Observacao ?? "";
        FormForma = OpcoesForma.FirstOrDefault(o => o.Valor == l.Forma) ?? OpcoesForma[0];
        _carregandoFormulario = false;
        OnPropertyChanged(nameof(FormEhSaida));
        PreencherAnexos(l.Anexos);
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        if (!_podeVer()) throw new RegraDeNegocioException("Você não tem permissão para lançar valores financeiros.");
        if (!Dinheiro.TentarConverter(FormValorTexto, out var centavos))
            throw new RegraDeNegocioException("Digite o valor em reais, por exemplo 150,00.");
        if (FormData is not DateTime data) throw new RegraDeNegocioException("Escolha a data do lançamento.");
        var dados = new DadosLancamentoEmpresa(data, FormEhEntrada ? TipoMovimentoEmpresa.Entrada : TipoMovimentoEmpresa.Saida,
            FormCategoria, FormDescricao, centavos, FormForma?.Valor, string.IsNullOrWhiteSpace(FormObservacao) ? null : FormObservacao);

        var novo = EditandoId is null;
        var salvo = novo ? await _servico.CriarAsync(dados, _agora()) : await _servico.EditarAsync(EditandoId!.Value, dados, _agora());
        await AtualizarAsync();
        SelecionarSemRecarregar(Linhas.FirstOrDefault(x => x.Id == salvo.Id)); // pode estar escondido pelo filtro: edita mesmo assim
        await CarregarParaEdicaoAsync(salvo.Id);
        MostrarSucesso(novo ? "Lançamento salvo. Se quiser, anexe agora a foto da nota ou o comprovante." : "Lançamento atualizado.");
    });

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async () =>
    {
        if (EditandoId is not int id) throw new RegraDeNegocioException("Escolha um lançamento na lista para excluir.");
        var temAnexos = AnexosDoLancamento.Count > 0;
        var pergunta = temAnexos
            ? $"Excluir este lançamento e apagar também {AnexosDoLancamento.Count} arquivo(s) anexado(s)? Isso não pode ser desfeito."
            : "Excluir este lançamento? Isso não pode ser desfeito.";
        if (!Alcada.Solicitar("Excluir um lançamento de entrada ou saída da empresa.")) return;
        if (!Confirmar(pergunta)) return;
        await _servico.ExcluirAsync(id, removerAnexos: temAnexos);
        SelecionarSemRecarregar(null);
        LimparFormulario();
        await AtualizarAsync();
        MostrarSucesso("Lançamento excluído.");
    });

    // ----- anexos -----

    public ObservableCollection<LinhaAnexo> AnexosDoLancamento { get; } = new();
    public bool TemAnexos => AnexosDoLancamento.Count > 0;

    private void PreencherAnexos(IEnumerable<AnexoLancamento> lista)
    {
        AnexosDoLancamento.Clear();
        foreach (var a in lista.OrderBy(a => a.CriadoEm).ThenBy(a => a.Id))
            AnexosDoLancamento.Add(new LinhaAnexo(a.Id, a.NomeOriginal, $"{TextoTamanho(a.TamanhoBytes)} · {a.CriadoEm:dd/MM/yyyy}", _anexos.Existe(a), a.CaminhoRelativo));
        OnPropertyChanged(nameof(TemAnexos));
    }

    private static string TextoTamanho(long bytes) =>
        bytes >= 1024 * 1024 ? (bytes / 1048576m).ToString("0.0", PtBr) + " MB" : Math.Max(1, bytes / 1024) + " KB";

    private async Task RecarregarAnexosAsync(int lancamentoId)
    {
        PreencherAnexos(await _anexos.ListarAsync(lancamentoId));
        await AtualizarAsync(); // a coluna "Anexos" da lista
    }

    [RelayCommand]
    private Task AnexarArquivoAsync() => ExecutarAsync(async () =>
    {
        if (EditandoId is not int id) throw new RegraDeNegocioException("Salve o lançamento primeiro e depois anexe o arquivo.");
        var caminho = EscolherArquivoAnexo();
        if (caminho is null) return;
        var anexo = await _anexos.AdicionarAsync(id, caminho, _agora());
        await RecarregarAnexosAsync(id);
        MostrarSucesso($"Arquivo anexado: {anexo.NomeOriginal}");
    });

    [RelayCommand]
    private void AbrirAnexo(LinhaAnexo? anexo)
    {
        if (anexo is null) return;
        try
        {
            if (!anexo.Existe) throw new RegraDeNegocioException("O arquivo não está mais na pasta de anexos. Remova este anexo e anexe de novo.");
            var caminho = _anexos.CaminhoCompleto(anexo.CaminhoRelativo);
            if (!File.Exists(caminho)) throw new RegraDeNegocioException("O arquivo não está mais na pasta de anexos. Remova este anexo e anexe de novo.");
            AbrirArquivo(caminho);
        }
        catch (RegraDeNegocioException ex) { MostrarErro(ex.Message); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            MostrarErro("Não consegui abrir o arquivo: " + ex.Message);
        }
    }

    [RelayCommand]
    private Task RemoverAnexoAsync(LinhaAnexo? anexo) => ExecutarAsync(async () =>
    {
        if (anexo is null || EditandoId is not int id) return;
        if (!Confirmar($"Remover o anexo \"{anexo.Nome}\"? O arquivo será apagado e isso não pode ser desfeito.")) return;
        await _anexos.RemoverAsync(anexo.Id);
        await RecarregarAnexosAsync(id);
        MostrarSucesso("Anexo removido.");
    });
}
