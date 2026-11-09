using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma opção do combo de tipo de ajuste: o valor interno e o texto que o operador lê.</summary>
public record OpcaoAjuste(TipoMovimentacao Tipo, string Nome);

/// <summary>Visão da lista do Estoque da loja.</summary>
public enum VisaoEstoque { Todos, Normal, Sobras }

/// <summary>
/// Estoque da loja: numa tela só, a lista de produtos, o cadastro (nome, preços, mínimo), a grade de tamanhos
/// com o estoque de cada um e o ajuste de estoque com motivo. Todo ajuste gera uma movimentação no histórico.
/// </summary>
public partial class EstoqueLojaViewModel : ViewModelBase
{
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private int _versaoBusca;
    private bool _silenciar; // true enquanto a seleção da lista é refeita pelo programa (não pelo operador)

    private readonly FotoProdutoService? _fotos;
    private readonly Func<DateTime> _hoje;
    private readonly NexusLS.Core.Vitrine.AjustesProdutoStore? _ajustesCatalogo;

    private readonly ConfiguracaoService? _configuracao;
    private int _estoqueMinimoPadrao;
    private int _margemPadrao;

    public EstoqueLojaViewModel(ProdutoService produtos, EstoqueService estoque, FotoProdutoService? fotos = null, ConfiguracaoService? configuracao = null, Func<DateTime>? relogio = null, NexusLS.Core.Vitrine.AjustesProdutoStore? ajustesCatalogo = null)
    {
        _ajustesCatalogo = ajustesCatalogo;
        _hoje = relogio ?? (() => DateTime.Today);
        _fotos = fotos;
        _configuracao = configuracao;
        _produtos = produtos;
        _estoque = estoque;
        _tipoSelecionado = Opcoes[0];
        Novo();
        _ = BuscarAsync();
        _ = CarregarPadroesAsync();
    }

    /// <summary>Lê o estoque mínimo e a margem sugeridos nas Configurações e aplica se o formulário ainda é um produto novo em branco.</summary>
    private async Task CarregarPadroesAsync()
    {
        if (_configuracao is null) return;
        try
        {
            var c = await _configuracao.ObterAsync();
            _estoqueMinimoPadrao = c.EstoqueMinimoPadrao;
            _margemPadrao = c.MargemPadraoPercentual;
            if (IdEditando is null && EstoqueMinimo == "0" && string.IsNullOrEmpty(MargemDesejada)) AplicarPadroes();
        }
        catch (Exception) { /* sem os padrões o cadastro continua normal */ }
    }

    private void AplicarPadroes()
    {
        EstoqueMinimo = _estoqueMinimoPadrao.ToString(CultureInfo.InvariantCulture);
        MargemDesejada = _margemPadrao > 0 ? _margemPadrao.ToString(CultureInfo.InvariantCulture) : "";
    }

    // ----- Exportar o estoque em CSV -----
    /// <summary>A tela define a caixa "Salvar como" do Windows (nulo = cancelou).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    /// <summary>Gera o CSV do estoque inteiro (uma linha por tamanho; inclui desativados, marcados na coluna Situação).</summary>
    [RelayCommand]
    private Task ExportarCsvAsync() => ExecutarAsync(async () =>
    {
        var lista = await _produtos.ListarComTamanhosAsync(incluirInativos: true);
        if (lista.Count == 0)
            throw new RegraDeNegocioException("Não há produtos no estoque para exportar.");

        var caminho = EscolherArquivoParaSalvar($"estoque-{_hoje():yyyy-MM-dd}.csv");
        if (caminho is null) return;

        ExportadorCsv.Salvar(caminho, ExportadorCsv.Estoque(lista));
        MostrarSucesso($"Estoque exportado: {lista.Count} produto(s). Arquivo salvo: {caminho}");
    });

    // ----- Importar produtos de um arquivo CSV -----
    /// <summary>Abre a caixa "Abrir arquivo" do Windows (a View preenche). Devolve o caminho ou nulo se cancelou.</summary>
    public Func<string?> EscolherArquivoParaAbrir { get; set; } = () => null;
    /// <summary>Mostra o resumo e pergunta se pode importar (a View preenche com uma caixa de mensagem).</summary>
    public Func<string, bool> ConfirmarImportacao { get; set; } = _ => true;
    /// <summary>Caixa de confirmação de ações em lote.</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;
    /// <summary>Mostra o relatório final (a View preenche).</summary>
    public Action<string> InformarImportacao { get; set; } = _ => { };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeImportar))]
    private bool _importando;

    public bool PodeImportar => !Importando;

    [RelayCommand]
    private Task ImportarCsvAsync() => ExecutarAsync(async () =>
    {
        var caminho = EscolherArquivoParaAbrir();
        if (caminho is null) return;

        Importando = true;
        try
        {
            var texto = ImportadorProdutos.Decodificar(await File.ReadAllBytesAsync(caminho));
            var analise = ImportadorProdutos.Analisar(texto);
            if (analise.ErroDoArquivo is not null) { MostrarErro(analise.ErroDoArquivo); return; }
            if (!analise.PodeImportar)
            {
                MostrarErro("O arquivo não tem nenhum produto válido." + Detalhes(analise.Problemas));
                return;
            }

            var resumo = $"{analise.Produtos.Count} produto(s) serão cadastrados." +
                         $"{Environment.NewLine}Colunas reconhecidas: {string.Join(", ", analise.ColunasReconhecidas)}." +
                         (analise.Problemas.Count > 0 ? $"{Environment.NewLine}{Environment.NewLine}{analise.Problemas.Count} linha(s) com problema NÃO serão importadas:{Detalhes(analise.Problemas)}" : "") +
                         $"{Environment.NewLine}{Environment.NewLine}Produtos que já existem não são alterados. Importar agora?";
            if (!ConfirmarImportacao(resumo)) { MostrarSucesso("Importação cancelada. Nada foi cadastrado."); return; }

            var resultado = await new ImportadorProdutos(_produtos, _estoque).ImportarAsync(analise);
            await BuscarAsync();

            var linhas = analise.Problemas.Concat(resultado.Ignorados).ToList();
            MostrarSucesso($"{resultado.Importados} produto(s) importado(s)" + (linhas.Count > 0 ? $"; {linhas.Count} linha(s) não entraram." : "."));
            if (linhas.Count > 0)
                InformarImportacao($"{resultado.Importados} produto(s) importado(s).{Environment.NewLine}Não entraram:{Detalhes(linhas)}");
        }
        finally
        {
            Importando = false;
        }
    });

    private static string Detalhes(IReadOnlyList<ProblemaImportacao> problemas)
    {
        var mostrar = problemas.Take(12).Select(p => Environment.NewLine + "• " + p);
        var resto = problemas.Count > 12 ? $"{Environment.NewLine}• ... e mais {problemas.Count - 12}." : "";
        return string.Concat(mostrar) + resto;
    }

    public IReadOnlyList<OpcaoAjuste> Opcoes { get; } = new[]
    {
        new OpcaoAjuste(TipoMovimentacao.Entrada, "Entrada (soma ao estoque)"),
        new OpcaoAjuste(TipoMovimentacao.Perda, "Perda (tira do estoque)"),
        new OpcaoAjuste(TipoMovimentacao.Correcao, "Correção (informar a contagem)")
    };

    public ObservableCollection<Produto> Produtos { get; } = new();
    public ObservableCollection<ProdutoTamanho> Grade { get; } = new();
    public ObservableCollection<MovimentacaoEstoque> Movimentacoes { get; } = new();

    // ----- Lista e filtros -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _mostrarInativos;
    [ObservableProperty] private bool _somenteAbaixoDoMinimo;

    /// <summary>Quais produtos a lista mostra: todos, só o estoque normal ou só as sobras da loja (como no ERP).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisaoTodos))]
    [NotifyPropertyChangedFor(nameof(VisaoEstoqueNormal))]
    [NotifyPropertyChangedFor(nameof(VisaoSobras))]
    private VisaoEstoque _visao = VisaoEstoque.Todos;

    public bool VisaoTodos { get => Visao == VisaoEstoque.Todos; set { if (value) Visao = VisaoEstoque.Todos; } }
    public bool VisaoEstoqueNormal { get => Visao == VisaoEstoque.Normal; set { if (value) Visao = VisaoEstoque.Normal; } }
    public bool VisaoSobras { get => Visao == VisaoEstoque.Sobras; set { if (value) Visao = VisaoEstoque.Sobras; } }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoSobras))]
    private int _quantidadeSobras;

    public string TextoSobras => $"Sobras ({QuantidadeSobras})";

    partial void OnVisaoChanged(VisaoEstoque value) => _ = BuscarAsync();
    [ObservableProperty] private Produto? _selecionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAlerta))]
    [NotifyPropertyChangedFor(nameof(TextoAlerta))]
    private int _quantidadeAbaixoDoMinimo;

    public bool TemAlerta => QuantidadeAbaixoDoMinimo > 0;
    public string TextoAlerta => $"Atenção: {QuantidadeAbaixoDoMinimo} produto(s) abaixo do estoque mínimo.";

    // ----- Cadastro do produto (tudo como texto: é o que o operador digita) -----
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _codigoBarras = "";
    [ObservableProperty] private string _referencia = "";
    [ObservableProperty] private string _precoVenda = "";
    [ObservableProperty] private string _precoAtacado = "";
    [ObservableProperty] private string _precoCusto = "";
    [ObservableProperty] private string _categoria = "";
    [ObservableProperty] private string _codigoInterno = "";
    [ObservableProperty] private bool _venderSemBaixa;
    [ObservableProperty] private bool _sobra;
    [ObservableProperty] private string _margemDesejada = "";
    [ObservableProperty] private string _textoMargem = "";
    [ObservableProperty] private string _estoqueMinimo = "0";
    [ObservableProperty] private string _estoqueInicial = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaEditando))]
    [NotifyPropertyChangedFor(nameof(EhNovo))]
    [NotifyPropertyChangedFor(nameof(TituloFormulario))]
    private int? _idEditando; // null = cadastrando um produto novo

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoBotaoAtivo))]
    private bool _produtoAtivo = true;

    [ObservableProperty] private string _nomeEditando = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstoqueTotalTexto))]
    private int _estoqueTotalEditando;

    public bool EstaEditando => IdEditando is not null;
    public bool EhNovo => !EstaEditando;
    public string TituloFormulario => EstaEditando ? "Editar produto" : "Novo produto";
    public string TextoBotaoAtivo => ProdutoAtivo ? "Desativar" : "Reativar";
    public string EstoqueTotalTexto => $"Estoque total: {EstoqueTotalEditando}";

    // ----- Grade de tamanhos e ajuste -----
    [ObservableProperty] private string _novoTamanho = "";

    /// <summary>Cores do produto, separadas por vírgula (informativas).</summary>
    [ObservableProperty] private string _cores = "";

    /// <summary>As grades prontas (letras, calça/bermuda, camisa de botão) e a escolhida para cadastrar de uma vez.</summary>
    public IReadOnlyList<GradeDeTamanho> GradesProntas => GradesDeTamanho.Todas;
    [ObservableProperty] private GradeDeTamanho? _gradeEscolhida;
    [ObservableProperty] private ProdutoTamanho? _tamanhoSelecionado;
    [ObservableProperty] private OpcaoAjuste _tipoSelecionado;
    [ObservableProperty] private string _quantidadeTexto = "";
    [ObservableProperty] private string _motivo = "";

    public string TextoTamanhoAjuste => TamanhoSelecionado is null
        ? "Escolha um tamanho na lista acima."
        : TamanhoSelecionado.EhSemTamanho
            ? $"Ajustando: Sem tamanho (estoque {TamanhoSelecionado.Estoque})"
            : $"Ajustando o tamanho {TamanhoSelecionado.Tamanho} (estoque {TamanhoSelecionado.Estoque})";

    public bool PodeRemoverTamanho => TamanhoSelecionado is { EhSemTamanho: false };

    public string RotuloQuantidade =>
        TipoSelecionado.Tipo == TipoMovimentacao.Correcao ? "Quantidade contada (novo estoque)" : "Quantidade";

    partial void OnBuscaChanged(string value) => _ = BuscarAsync();
    partial void OnMostrarInativosChanged(bool value) => _ = BuscarAsync();
    partial void OnSomenteAbaixoDoMinimoChanged(bool value) => _ = BuscarAsync();

    partial void OnTipoSelecionadoChanged(OpcaoAjuste value) => OnPropertyChanged(nameof(RotuloQuantidade));

    partial void OnTamanhoSelecionadoChanged(ProdutoTamanho? value)
    {
        OnPropertyChanged(nameof(TextoTamanhoAjuste));
        OnPropertyChanged(nameof(PodeRemoverTamanho));
    }

    /// <summary>O operador escolheu um produto na lista: abre o cadastro, a grade e o histórico dele.</summary>
    partial void OnSelecionadoChanged(Produto? value)
    {
        if (_silenciar || value is null) return; // a lista foi recarregada: não apaga o que está sendo editado
        _ = ExecutarAsync(() => AbrirProdutoAsync(value));
    }

    // ================= Lista =================

    [RelayCommand]
    private Task BuscarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versaoBusca; // se o operador digita rápido, só a busca mais recente atualiza a lista
        var lista = await _produtos.BuscarAsync(Busca, MostrarInativos);
        var abaixo = await _estoque.ListarAbaixoDoMinimoAsync();
        if (versao != _versaoBusca) return;

        var sobras = await _produtos.ContarSobrasAsync();
        if (SomenteAbaixoDoMinimo) lista = lista.Where(p => p.EstoqueBaixo).ToList();
        lista = Visao switch
        {
            VisaoEstoque.Normal => lista.Where(p => !p.Sobra).ToList(),
            VisaoEstoque.Sobras => lista.Where(p => p.Sobra).ToList(),
            _ => lista
        };

        Produtos.Clear();
        foreach (var p in lista) Produtos.Add(p);
        QuantidadeAbaixoDoMinimo = abaixo.Count;
        QuantidadeSobras = sobras;

        if (IdEditando is int id) Reselecionar(id);
    });

    private void Reselecionar(int id)
    {
        _silenciar = true;
        try { Selecionado = Produtos.FirstOrDefault(p => p.Id == id); }
        finally { _silenciar = false; }
    }

    // ================= Cadastro do produto =================

    partial void OnPrecoVendaChanged(string value) => AtualizarTextoMargem();
    partial void OnPrecoCustoChanged(string value) => AtualizarTextoMargem();

    /// <summary>Mostra a margem e o markup do preço digitado, para o operador ver se o produto dá lucro.</summary>
    private void AtualizarTextoMargem()
    {
        if (!Dinheiro.TentarConverter(PrecoVenda, out var venda) || !Dinheiro.TentarConverter(PrecoCusto, out var custo) || custo == 0)
        {
            TextoMargem = "";
            return;
        }
        var margem = CalculadoraPreco.Margem(venda, custo);
        var markup = CalculadoraPreco.Markup(venda, custo);
        TextoMargem = $"Margem {margem:0.##}% (lucro sobre o preço) · Markup {markup:0.##}% (lucro sobre o custo)"
                      + (venda < custo ? " · ATENÇÃO: preço abaixo do custo!" : "");
    }

    /// <summary>Calcula o preço de varejo que dá a margem desejada sobre o custo e põe no campo (sem salvar).</summary>
    [RelayCommand]
    private void CalcularPreco()
    {
        try
        {
            if (!Dinheiro.TentarConverter(PrecoCusto, out var custo) || custo <= 0)
                throw new RegraDeNegocioException("Informe o preço de custo para calcular o preço de venda.");
            if (!decimal.TryParse(MargemDesejada.Replace("%", "").Trim(), NumberStyles.AllowDecimalPoint,
                    new CultureInfo("pt-BR"), out var margem))
                throw new RegraDeNegocioException("Informe a margem desejada em %, por exemplo 40.");

            PrecoVenda = Dinheiro.FormatarParaEdicao(CalculadoraPreco.PrecoPorMargem(custo, margem));
            MostrarSucesso("Preço de varejo calculado. Confira e salve.");
        }
        catch (RegraDeNegocioException ex)
        {
            MostrarErro(ex.Message);
        }
    }

    // ----- Foto do produto -----
    /// <summary>Abre a caixa "Abrir arquivo" para escolher a foto (a View preenche). Nulo = cancelou.</summary>
    public Func<string?> EscolherFoto { get; set; } = () => null;

    [ObservableProperty] private System.Windows.Media.ImageSource? _fotoImagem;
    [ObservableProperty] private string _situacaoFoto = "";
    public bool FotoDisponivel => _fotos is not null;
    public bool TemFoto => FotoImagem is not null;
    partial void OnFotoImagemChanged(System.Windows.Media.ImageSource? value) => OnPropertyChanged(nameof(TemFoto));

    private void MostrarFotoDe(Produto? p)
    {
        FotoImagem = p is null || _fotos is null ? null : Imagens.ProcessadorImagemWpf.Carregar(_fotos.CaminhoDe(p.FotoArquivo));
        SituacaoFoto = p?.FotoArquivo is null ? "Sem foto"
            : p.FotoUrl is null ? "Foto guardada · será enviada na próxima atualização do catálogo online"
            : "Foto enviada ao catálogo online";
    }

    [RelayCommand]
    private Task TrocarFotoAsync() => ExecutarAsync(async () =>
    {
        if (_fotos is null) return;
        if (IdEditando is not int id) throw new RegraDeNegocioException("Salve o produto antes de escolher a foto.");
        var arquivo = EscolherFoto();
        if (arquivo is null) return;
        await _fotos.DefinirAsync(id, await System.IO.File.ReadAllBytesAsync(arquivo));
        MostrarFotoDe(await _produtos.ObterAsync(id));
        MostrarSucesso("Foto salva.");
    });

    [RelayCommand]
    private Task RemoverFotoAsync() => ExecutarAsync(async () =>
    {
        if (_fotos is null || IdEditando is not int id) return;
        await _fotos.RemoverAsync(id);
        MostrarFotoDe(await _produtos.ObterAsync(id));
        MostrarSucesso("Foto removida.");
    });

    /// <summary>Preenche o campo com o próximo código de barras interno (EAN-13, prefixo 2) livre.</summary>
    [RelayCommand]
    private Task GerarCodigoBarrasAsync() => ExecutarAsync(async () =>
    {
        CodigoBarras = await _produtos.GerarCodigoBarrasInternoAsync();
        MostrarSucesso("Código de barras gerado. Salve o produto para reservá-lo.");
    });

    /// <summary>
    /// Abre o cadastro já como sobra da loja. Como no ERP, a sobra nova nasce fora do catálogo online
    /// (o ajuste "aparece no catálogo" fica desligado ao salvar); dá para ligar depois no Editor do catálogo.
    /// </summary>
    [RelayCommand]
    private void AdicionarSobra()
    {
        Novo();
        Sobra = true;
        Mensagem = "Nova sobra: continua sendo estoque da loja e vende no PDV; só fica separada na lista de Sobras e fora do catálogo online.";
    }

    /// <summary>
    /// Marca todos os produtos ativos da loja para 'Vender sem baixa' de uma só vez.
    /// </summary>
    [RelayCommand]
    private Task AtivarVenderSemBaixaEmTodosAsync() => ExecutarAsync(async () =>
    {
        if (!Confirmar("Deseja marcar TODOS os produtos da loja como 'Vender sem baixa de estoque'?\n\nIsso permite que todos os itens sejam vendidos no PDV sem exigir saldo físico de estoque."))
            return;

        var alterados = await _produtos.MarcarTodosVenderSemBaixaAsync(true);
        await BuscarAsync();
        if (IdEditando is int id) await AtualizarDetalhesAsync(id);
        MostrarSucesso($"{alterados} produto(s) marcado(s) para vender sem baixa de estoque.");
    });

    /// <summary>Abre a caixa de escolha dos produtos (a View preenche; é modal).</summary>
    public Action<ProdutoService> AbrirSeletorSemBaixa { get; set; } = _ => { };

    [RelayCommand]
    private Task EscolherProdutosSemBaixaAsync() => ExecutarAsync(async () =>
    {
        AbrirSeletorSemBaixa(_produtos);
        await BuscarAsync();
        if (IdEditando is int id) await AtualizarDetalhesAsync(id);
    });

    // ----- Tela de produto novo -----
    /// <summary>A View abre a tela de cadastro (modal) e devolve o produto criado, ou nulo se o operador cancelou.</summary>
    public Func<ProdutoService, EstoqueService, int, int, bool, Produto?> AbrirNovoProduto { get; set; } = (_, _, _, _, _) => null;

    [RelayCommand]
    private Task NovoProdutoTelaAsync() => AbrirTelaDeNovoAsync(false);

    [RelayCommand]
    private Task NovaSobraTelaAsync() => AbrirTelaDeNovoAsync(true);

    private Task AbrirTelaDeNovoAsync(bool sobra) => ExecutarAsync(async () =>
    {
        var criado = AbrirNovoProduto(_produtos, _estoque, _margemPadrao, _estoqueMinimoPadrao, sobra);
        if (criado is null) return;
        if (criado.Sobra) DeixarForaDoCatalogo(criado);
        await BuscarAsync();
        var atual = await _produtos.ObterAsync(criado.Id);
        if (atual is not null) await AbrirProdutoAsync(atual);
        MostrarSucesso($"Produto {criado.Nome} cadastrado.");
    });

    // ----- Nova referência a partir de uma base -----
    [ObservableProperty] private string _referenciaBase = "";
    private List<string> _tamanhosACopiar = new();

    /// <summary>
    /// Abre um cadastro novo já com os dados da referência base (nome, categoria, preços, cores, mínimo e a grade de tamanhos) e a
    /// próxima referência livre do mesmo modelo. O código de barras sugerido é o número da referência; os tamanhos usam esse número + 1, 2, 3...
    /// </summary>
    [RelayCommand]
    private Task CriarDaReferenciaBaseAsync() => ExecutarAsync(async () =>
    {
        var modelo = await _produtos.ModeloDaReferenciaAsync(ReferenciaBase);
        var b = modelo.Base;
        Novo();
        Nome = b.Nome;
        Referencia = modelo.NovaReferencia;
        CodigoBarras = modelo.NovaReferencia;
        Categoria = b.Categoria ?? "";
        Cores = b.Cores ?? "";
        PrecoVenda = Dinheiro.FormatarParaEdicao(b.PrecoVendaCentavos);
        PrecoAtacado = b.PrecoAtacadoCentavos > 0 ? Dinheiro.FormatarParaEdicao(b.PrecoAtacadoCentavos) : "";
        PrecoCusto = Dinheiro.FormatarParaEdicao(b.PrecoCustoCentavos);
        EstoqueMinimo = b.EstoqueMinimo.ToString(CultureInfo.InvariantCulture);
        VenderSemBaixa = b.VenderSemBaixa;
        _tamanhosACopiar = modelo.Tamanhos.ToList();
        MostrarSucesso($"Nova referência {modelo.NovaReferencia} criada a partir da {b.Referencia}. Confira o nome e salve" +
                       (_tamanhosACopiar.Count > 0 ? $"; os tamanhos ({string.Join(", ", _tamanhosACopiar)}) entram junto." : "."));
    });

    [RelayCommand]
    private void Novo()
    {
        _tamanhosACopiar = new();
        _silenciar = true;
        try { Selecionado = null; }
        finally { _silenciar = false; }

        IdEditando = null;
        MostrarFotoDe(null);
        NomeEditando = "";
        Nome = "";
        CodigoBarras = "";
        Referencia = "";
        PrecoVenda = "";
        PrecoAtacado = "";
        PrecoCusto = "";
        Categoria = "";
        Cores = "";
        CodigoInterno = "";
        VenderSemBaixa = false;
        Sobra = false;
        MargemDesejada = "";
        EstoqueMinimo = "0";
        AplicarPadroes();
        EstoqueInicial = "0";
        ProdutoAtivo = true;
        EstoqueTotalEditando = 0;
        NovoTamanho = "";
        QuantidadeTexto = "";
        Motivo = "";
        Grade.Clear();
        Movimentacoes.Clear();
        TamanhoSelecionado = null;
        Mensagem = "";
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var (dados, estoqueInicial) = LerFormulario();

        if (IdEditando is int id)
        {
            await _produtos.EditarAsync(id, dados);
            await BuscarAsync();
            await AtualizarDetalhesAsync(id);
            MostrarSucesso("Produto atualizado.");
        }
        else
        {
            var novo = await _produtos.CadastrarAsync(dados, estoqueInicial);
            if (_tamanhosACopiar.Count > 0) await _produtos.AdicionarGradeAsync(novo.Id, _tamanhosACopiar);
            _tamanhosACopiar = new();
            if (dados.Sobra) DeixarForaDoCatalogo(novo);
            await BuscarAsync();
            await AbrirProdutoAsync(novo);
            MostrarSucesso("Produto cadastrado. Na aba \"Tamanhos e estoque\" você cadastra a grade de tamanhos.");
        }
    });

    [RelayCommand]
    private Task AlternarAtivoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id) return;

        var estavaAtivo = ProdutoAtivo;
        if (estavaAtivo) await _produtos.DesativarAsync(id);
        else await _produtos.ReativarAsync(id);

        Novo();
        await BuscarAsync();
        MostrarSucesso(estavaAtivo ? "Produto desativado." : "Produto reativado.");
    });

    /// <summary>Sobra nova não aparece no catálogo online: grava o ajuste "visível = não" desse produto.</summary>
    private void DeixarForaDoCatalogo(Produto produto)
    {
        if (_ajustesCatalogo is null) return;
        try
        {
            var id = produto.Guid.ToString("D").ToLowerInvariant();
            var todos = _ajustesCatalogo.Carregar();
            todos[id] = (todos.GetValueOrDefault(id) ?? new NexusLS.Core.Vitrine.AjusteProdutoCatalogo()) with { Visivel = false };
            _ajustesCatalogo.Salvar(todos);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* o produto já foi salvo; o ajuste se refaz no Editor do catálogo */ }
    }

    private (DadosProduto dados, int estoqueInicial) LerFormulario()
    {
        if (!Dinheiro.TentarConverter(PrecoVenda, out var venda))
            throw new RegraDeNegocioException("Preço de venda inválido. Exemplo: 12,50");

        long custo = 0;
        if (!string.IsNullOrWhiteSpace(PrecoCusto) && !Dinheiro.TentarConverter(PrecoCusto, out custo))
            throw new RegraDeNegocioException("Preço de custo inválido. Exemplo: 8,00");

        long atacado = 0; // vazio = sem preço de atacado (vale o de varejo)
        if (!string.IsNullOrWhiteSpace(PrecoAtacado) && !Dinheiro.TentarConverter(PrecoAtacado, out atacado))
            throw new RegraDeNegocioException("Preço de atacado inválido. Exemplo: 9,00");

        var minimo = LerInteiro(EstoqueMinimo, "Estoque mínimo");
        var inicial = EhNovo ? LerInteiro(EstoqueInicial, "Estoque inicial") : 0;

        return (new DadosProduto(Nome, CodigoBarras, venda, custo, minimo, Referencia, atacado, Categoria, VenderSemBaixa, Sobra, Cores), inicial);
    }

    private static int LerInteiro(string texto, string campo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var valor))
            throw new RegraDeNegocioException($"{campo} inválido. Use um número inteiro, sem sinal.");
        return valor;
    }

    /// <summary>Preenche o cadastro com o produto escolhido e carrega a grade e o histórico dele.</summary>
    private async Task AbrirProdutoAsync(Produto produto)
    {
        IdEditando = produto.Id;
        MostrarFotoDe(produto);
        NomeEditando = produto.Nome;
        Nome = produto.Nome;
        CodigoBarras = produto.CodigoBarras ?? "";
        Referencia = produto.Referencia ?? "";
        Categoria = produto.Categoria ?? "";
        Cores = produto.Cores ?? "";
        CodigoInterno = produto.CodigoInterno ?? "";
        VenderSemBaixa = produto.VenderSemBaixa;
        Sobra = produto.Sobra;
        PrecoVenda = Dinheiro.FormatarParaEdicao(produto.PrecoVendaCentavos);
        PrecoAtacado = produto.PrecoAtacadoCentavos > 0 ? Dinheiro.FormatarParaEdicao(produto.PrecoAtacadoCentavos) : "";
        PrecoCusto = Dinheiro.FormatarParaEdicao(produto.PrecoCustoCentavos);
        EstoqueMinimo = produto.EstoqueMinimo.ToString(CultureInfo.InvariantCulture);
        ProdutoAtivo = produto.Ativo;
        NovoTamanho = "";
        QuantidadeTexto = "";
        Motivo = "";
        Mensagem = "";

        await AtualizarDetalhesAsync(produto.Id, escolherTamanhoPadrao: true);
        Reselecionar(produto.Id);
    }

    // ================= Grade de tamanhos =================

    [RelayCommand]
    private Task AdicionarTamanhoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id)
            throw new RegraDeNegocioException("Salve o produto antes de cadastrar os tamanhos.");

        var criado = await _produtos.AdicionarTamanhoAsync(id, NovoTamanho);
        NovoTamanho = "";
        await AtualizarDetalhesAsync(id, nomeParaSelecionar: criado.Tamanho);
        MostrarSucesso($"Tamanho {criado.Tamanho} adicionado. Agora lance o estoque dele em \"Ajuste de estoque\".");
    });

    /// <summary>Cadastra de uma vez todos os tamanhos da grade escolhida (os que já existem ficam como estão).</summary>
    [RelayCommand]
    private Task AdicionarGradeProntaAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id)
            throw new RegraDeNegocioException("Salve o produto antes de cadastrar os tamanhos.");
        if (GradeEscolhida is not { } grade)
            throw new RegraDeNegocioException("Escolha a grade (letras, calça e bermuda ou camisa de botão).");

        var criados = await _produtos.AdicionarGradeAsync(id, grade.Tamanhos);
        await AtualizarDetalhesAsync(id, nomeParaSelecionar: criados.FirstOrDefault()?.Tamanho);
        MostrarSucesso(criados.Count == 0
            ? "Todos os tamanhos dessa grade já estavam cadastrados."
            : $"{criados.Count} tamanho(s) adicionado(s) ({grade.Nome}). Lance o estoque de cada um em \"Ajuste de estoque\".");
    });

    [RelayCommand]
    private Task RemoverTamanhoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id || TamanhoSelecionado is not { EhSemTamanho: false } tamanho)
            throw new RegraDeNegocioException("Escolha, na lista, o tamanho que quer remover (\"Sem tamanho\" não pode sair).");

        await _produtos.RemoverTamanhoAsync(id, tamanho.Tamanho);
        await AtualizarDetalhesAsync(id);
        MostrarSucesso($"Tamanho {tamanho.Tamanho} removido.");
    });

    // ================= Ajuste de estoque =================

    /// <summary>
    /// Aplica o ajuste no tamanho escolhido. Na Correção o operador informa o que CONTOU; o sistema
    /// calcula a diferença. Todo ajuste vira uma linha no histórico, com o motivo.
    /// </summary>
    [RelayCommand]
    private Task AplicarAjusteAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id)
            throw new RegraDeNegocioException("Escolha um produto na lista.");
        if (TamanhoSelecionado is not { } tamanho)
            throw new RegraDeNegocioException("Escolha, na lista de tamanhos, qual tamanho será ajustado.");
        if (!int.TryParse(QuantidadeTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var quantidade))
            throw new RegraDeNegocioException("Quantidade inválida. Use um número inteiro, sem sinal.");

        var tipo = TipoSelecionado.Tipo;
        var variacao = quantidade;
        if (tipo == TipoMovimentacao.Correcao)
        {
            variacao = quantidade - tamanho.Estoque;
            if (variacao == 0)
                throw new RegraDeNegocioException("A contagem é igual ao estoque atual. Nada a corrigir.");
        }

        var nomeTamanho = tamanho.Tamanho;
        var produto = await _estoque.AjustarAsync(id, tipo, variacao, Motivo, nomeTamanho);

        QuantidadeTexto = "";
        Motivo = "";
        await BuscarAsync();
        await AtualizarDetalhesAsync(id, nomeParaSelecionar: nomeTamanho);

        var agora = Grade.FirstOrDefault(t => t.Tamanho == nomeTamanho)?.Estoque ?? 0;
        var onde = nomeTamanho == Tamanhos.SemTamanho ? "" : $" (tamanho {nomeTamanho})";
        MostrarSucesso($"Estoque de \"{produto.Nome}\"{onde} atualizado: agora {agora}.");
    });


    // ================= Apoio =================

    /// <summary>Recarrega grade, total e histórico do produto aberto (sem mexer no que o operador digitou no cadastro).</summary>
    private async Task AtualizarDetalhesAsync(int produtoId, string? nomeParaSelecionar = null, bool escolherTamanhoPadrao = false)
    {
        var anterior = nomeParaSelecionar ?? TamanhoSelecionado?.Tamanho;

        var grade = await _produtos.ListarTamanhosAsync(produtoId);
        var historico = await _estoque.ListarMovimentacoesAsync(produtoId);

        Grade.Clear();
        foreach (var t in grade) Grade.Add(t);
        Movimentacoes.Clear();
        foreach (var m in historico) Movimentacoes.Add(m);

        EstoqueTotalEditando = grade.Sum(t => t.Estoque);

        // mantém o tamanho que estava escolhido; se não houver, começa pelo primeiro tamanho de verdade (ou "Sem tamanho")
        TamanhoSelecionado = Grade.FirstOrDefault(t => t.Tamanho == anterior && !escolherTamanhoPadrao)
                             ?? Grade.FirstOrDefault(t => !t.EhSemTamanho)
                             ?? Grade.FirstOrDefault();
    }
}
