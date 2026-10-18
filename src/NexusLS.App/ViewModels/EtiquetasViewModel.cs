using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha da fila de etiquetas: um produto num tamanho e quantas cópias imprimir.</summary>
public partial class LinhaEtiquetaViewModel : ObservableObject
{
    private readonly Action _aoMudar;

    public LinhaEtiquetaViewModel(int produtoId, string nome, string tamanho, long precoVarejo, long precoAtacado,
        string? codigoBarras, int quantidade, Action aoMudar)
    {
        ProdutoId = produtoId;
        Nome = nome;
        Tamanho = tamanho;
        PrecoVarejoCentavos = precoVarejo;
        PrecoAtacadoCentavos = precoAtacado;
        CodigoBarras = codigoBarras;
        _quantidade = quantidade;
        _aoMudar = aoMudar;
    }

    public int ProdutoId { get; }
    public string Nome { get; }

    /// <summary>"Sem tamanho" quando o produto não tem grade.</summary>
    public string Tamanho { get; }
    public long PrecoVarejoCentavos { get; }
    public long PrecoAtacadoCentavos { get; }
    public string? CodigoBarras { get; }

    public bool TemCodigo => Code128.PodeCodificar(CodigoBarras);

    [ObservableProperty] private int _quantidade;

    partial void OnQuantidadeChanged(int value)
    {
        if (value < 1) Quantidade = 1; // no mínimo uma etiqueta; para tirar a linha, use Remover
        else _aoMudar();
    }

    public long PrecoPara(bool atacado) =>
        atacado && PrecoAtacadoCentavos > 0 ? PrecoAtacadoCentavos : PrecoVarejoCentavos;
}

/// <summary>Monta e imprime etiquetas de produto: nome, preço, tamanho e código de barras.</summary>
public partial class EtiquetasViewModel : ViewModelBase
{
    private readonly ProdutoService _produtos;
    private CancellationTokenSource? _buscaCts;
    private Produto? _produtoAtual;
    private bool _carregando; // true enquanto a lista de tamanhos é refeita

    private readonly ModeloEtiquetaStore? _modelos;

    public EtiquetasViewModel(ProdutoService produtos, ModeloEtiquetaStore? modelos = null)
    {
        _produtos = produtos;
        _modelos = modelos;
        foreach (var f in FormatoEtiqueta.Todos) Formatos.Add(f);
        if (modelos is not null)
            foreach (var f in modelos.Carregar()) Formatos.Add(f);
        _formatoSelecionado = FormatoEtiqueta.Bobina50x30;
        Fila.CollectionChanged += (_, _) => AtualizarResumo();
        AtualizarResumo();
    }

    /// <summary>A tela imprime: recebe as etiquetas, o formato e a primeira posição; devolve false se a pessoa cancelou.</summary>
    public Func<IReadOnlyList<ItemEtiqueta>, FormatoEtiqueta, int, bool> ImprimirEtiquetas { get; set; } = (_, _, _) => false;

    /// <summary>Avisa a tela que a prévia precisa ser desenhada de novo.</summary>
    public event Action? PreviaMudou;

    public ObservableCollection<FormatoEtiqueta> Formatos { get; } = new();
    public ObservableCollection<Produto> Resultados { get; } = new();
    public ObservableCollection<string> OpcoesDeTamanho { get; } = new();
    public ObservableCollection<LinhaEtiquetaViewModel> Fila { get; } = new();

    // ----- Escolha do produto -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private Produto? _resultadoSelecionado;
    [ObservableProperty] private string? _tamanhoEscolhido;
    [ObservableProperty] private string _quantidadeTexto = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemTamanhosReais))]
    private int _quantidadeDeTamanhosReais;

    public bool TemTamanhosReais => QuantidadeDeTamanhosReais > 0;

    // ----- Como imprimir -----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarPosicao))]
    [NotifyPropertyChangedFor(nameof(PodeRemoverModelo))]
    private FormatoEtiqueta _formatoSelecionado;

    // ----- Modelos próprios (bobina de tamanho livre) -----
    [ObservableProperty] private string _novoModeloNome = "";
    [ObservableProperty] private string _novoModeloLargura = "";
    [ObservableProperty] private string _novoModeloAltura = "";

    /// <summary>Só os modelos criados pelo usuário podem ser removidos; os de fábrica ficam.</summary>
    public bool PodeRemoverModelo => !FormatoEtiqueta.Todos.Contains(FormatoSelecionado);

    /// <summary>Avisa quais etiquetas da fila têm código de barras que não ficaria legível neste papel (vazio = tudo certo).</summary>
    [ObservableProperty] private string _avisoLegibilidade = "";

    [RelayCommand]
    private Task SalvarModeloAsync() => ExecutarAsync(() =>
    {
        if (_modelos is null) throw new RegraDeNegocioException("Não foi possível salvar modelos neste momento.");
        if (!double.TryParse(NovoModeloLargura.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var largura)
            || !double.TryParse(NovoModeloAltura.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var altura))
            throw new RegraDeNegocioException("Digite a largura e a altura da etiqueta em milímetros, por exemplo 60 e 40.");

        var modelo = _modelos.AdicionarBobina(NovoModeloNome, largura, altura);
        Formatos.Add(modelo);
        FormatoSelecionado = modelo;
        NovoModeloNome = NovoModeloLargura = NovoModeloAltura = "";
        MostrarSucesso("Modelo salvo.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task RemoverModeloAsync() => ExecutarAsync(() =>
    {
        if (!PodeRemoverModelo || _modelos is null) return Task.CompletedTask;

        var modelo = FormatoSelecionado;
        _modelos.Remover(modelo);
        FormatoSelecionado = FormatoEtiqueta.Bobina50x30;
        Formatos.Remove(modelo);
        MostrarSucesso("Modelo removido.");
        return Task.CompletedTask;
    });

    [ObservableProperty] private string _primeiraPosicaoTexto = "1";
    [ObservableProperty] private bool _precoAtacado;
    [ObservableProperty] private string _resumo = "";

    public bool MostrarPosicao => FormatoSelecionado.EtiquetasPorPagina > 1;
    public bool FilaVazia => Fila.Count == 0;

    partial void OnFormatoSelecionadoChanged(FormatoEtiqueta value) => AtualizarResumo();
    partial void OnPrimeiraPosicaoTextoChanged(string value) => AtualizarResumo();
    partial void OnPrecoAtacadoChanged(bool value) => AtualizarResumo();
    partial void OnBuscaChanged(string value) => _ = AtualizarResultadosAsync(value);

    partial void OnResultadoSelecionadoChanged(Produto? value)
    {
        if (value is not null) _ = ExecutarAsync(() => CarregarTamanhosAsync(value));
    }

    // ================= Escolher o produto =================

    private async Task AtualizarResultadosAsync(string texto)
    {
        _buscaCts?.Cancel();
        var cts = _buscaCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token); // espera a pessoa parar de digitar
            texto = texto.Trim();
            var lista = texto.Length == 0 ? new List<Produto>() : await _produtos.BuscarAsync(texto);
            if (cts.IsCancellationRequested) return;

            Resultados.Clear();
            foreach (var p in lista.Take(12)) Resultados.Add(p);
        }
        catch (OperationCanceledException)
        {
            // chegou uma digitação mais nova; esta busca não vale mais
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao buscar: " + ex.Message);
        }
    }

    /// <summary>Enter na busca: escolhe o primeiro resultado (ou código de barras exato) e passa para os tamanhos.</summary>
    [RelayCommand]
    private Task EscolherPrimeiroAsync() => ExecutarAsync(async () =>
    {
        var produto = await _produtos.EscolherParaVendaAsync(Busca, null);
        if (produto is null)
            throw new RegraDeNegocioException($"Nenhum produto encontrado para \"{Busca.Trim()}\".");
        await CarregarTamanhosAsync(produto);
    });

    private async Task CarregarTamanhosAsync(Produto produto)
    {
        _produtoAtual = produto;
        var grade = await _produtos.ListarTamanhosAsync(produto.Id);

        _carregando = true;
        OpcoesDeTamanho.Clear();
        foreach (var t in grade.Where(t => !t.EhSemTamanho)) OpcoesDeTamanho.Add(t.Tamanho);
        QuantidadeDeTamanhosReais = OpcoesDeTamanho.Count;
        OpcoesDeTamanho.Add(Tamanhos.SemTamanho);
        _carregando = false;

        TamanhoEscolhido = OpcoesDeTamanho[0]; // o primeiro tamanho de verdade, ou "Sem tamanho" se não há grade
        Mensagem = "";
    }

    // ================= Fila =================

    [RelayCommand]
    private void Adicionar()
    {
        if (!TentarLerQuantidade(out var quantidade)) return;
        if (_produtoAtual is null || TamanhoEscolhido is null)
        {
            MostrarErro("Escolha um produto na busca.");
            return;
        }

        AdicionarLinha(_produtoAtual, TamanhoEscolhido, quantidade);
        AvisarSeCodigoNaoCabe(_produtoAtual);
    }

    /// <summary>Uma linha para cada tamanho do produto, todas com a quantidade informada.</summary>
    [RelayCommand]
    private void AdicionarTodosOsTamanhos()
    {
        if (!TentarLerQuantidade(out var quantidade)) return;
        if (_produtoAtual is null)
        {
            MostrarErro("Escolha um produto na busca.");
            return;
        }

        var nomes = TemTamanhosReais ? OpcoesDeTamanho.Where(t => t != Tamanhos.SemTamanho).ToList() : OpcoesDeTamanho.ToList();
        foreach (var t in nomes) AdicionarLinha(_produtoAtual, t, quantidade);
        AvisarSeCodigoNaoCabe(_produtoAtual);
    }

    [RelayCommand]
    private void Remover(LinhaEtiquetaViewModel? linha)
    {
        if (linha is not null) Fila.Remove(linha);
    }

    [RelayCommand]
    private void Limpar()
    {
        Fila.Clear();
        Mensagem = "";
    }

    private void AdicionarLinha(Produto produto, string tamanho, int quantidade)
    {
        var existente = Fila.FirstOrDefault(l => l.ProdutoId == produto.Id && l.Tamanho == tamanho);
        if (existente is not null)
        {
            existente.Quantidade += quantidade; // mesma etiqueta: soma as cópias
        }
        else
        {
            Fila.Add(new LinhaEtiquetaViewModel(produto.Id, produto.Nome, tamanho, produto.PrecoVendaCentavos,
                produto.PrecoAtacadoCentavos, produto.CodigoBarras, quantidade, AtualizarResumo));
        }
        Mensagem = "";
        QuantidadeTexto = "1";
    }

    private void AvisarSeCodigoNaoCabe(Produto produto)
    {
        if (!string.IsNullOrWhiteSpace(produto.CodigoBarras) && !Code128.PodeCodificar(produto.CodigoBarras))
            MostrarErro($"\"{produto.Nome}\": o código de barras tem caracteres que o Code 128 não aceita (como acentos). A etiqueta sairá sem código.");
    }

    private bool TentarLerQuantidade(out int quantidade)
    {
        if (int.TryParse(QuantidadeTexto, NumberStyles.None, CultureInfo.InvariantCulture, out quantidade) && quantidade >= 1)
            return true;

        MostrarErro("Quantidade de etiquetas inválida. Use um número inteiro de 1 em diante.");
        return false;
    }

    // ================= Impressão =================

    /// <summary>As etiquetas da fila prontas para desenhar (preço de varejo ou de atacado, conforme a escolha).</summary>
    public IReadOnlyList<ItemEtiqueta> ItensParaImprimir() => Fila
        .Select(l => new ItemEtiqueta(
            l.ProdutoId, l.Nome,
            l.Tamanho == Tamanhos.SemTamanho ? "" : l.Tamanho,
            l.PrecoPara(PrecoAtacado), l.CodigoBarras, l.Quantidade))
        .ToList();

    /// <summary>A primeira posição digitada, ajustada para caber na folha (1 se estiver vazia ou inválida).</summary>
    public int PrimeiraPosicaoValida =>
        int.TryParse(PrimeiraPosicaoTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var p)
            ? Math.Clamp(p, 1, FormatoSelecionado.EtiquetasPorPagina)
            : 1;

    [RelayCommand]
    private Task ImprimirAsync() => ExecutarAsync(() =>
    {
        if (Fila.Count == 0)
            throw new RegraDeNegocioException("A fila está vazia. Adicione produtos antes de imprimir.");

        var posicao = 1;
        if (MostrarPosicao)
        {
            if (!int.TryParse(PrimeiraPosicaoTexto, NumberStyles.None, CultureInfo.InvariantCulture, out posicao)
                || posicao < 1 || posicao > FormatoSelecionado.EtiquetasPorPagina)
                throw new RegraDeNegocioException(
                    $"A primeira etiqueta deve ficar entre 1 e {FormatoSelecionado.EtiquetasPorPagina}.");
        }

        var ilegiveis = ItensIlegiveis();
        if (ilegiveis.Count > 0)
            throw new RegraDeNegocioException(MensagemIlegivel(ilegiveis));

        var total = DiagramadorEtiquetas.TotalDeEtiquetas(ItensParaImprimir());
        if (ImprimirEtiquetas(ItensParaImprimir(), FormatoSelecionado, posicao))
            MostrarSucesso($"{total} etiqueta(s) enviada(s) para a impressora.");
        return Task.CompletedTask;
    });

    private void AtualizarResumo()
    {
        if (_carregando) return;

        var total = DiagramadorEtiquetas.TotalDeEtiquetas(ItensParaImprimir());
        var porPagina = FormatoSelecionado.EtiquetasPorPagina;
        var paginas = total == 0 ? 0 : (PrimeiraPosicaoValida - 1 + total + porPagina - 1) / porPagina;

        Resumo = total == 0
            ? "Nenhuma etiqueta na fila."
            : porPagina == 1
                ? $"{total} etiqueta(s)"
                : $"{total} etiqueta(s) em {paginas} folha(s)";
        OnPropertyChanged(nameof(FilaVazia));

        var ilegiveis = ItensIlegiveis();
        AvisoLegibilidade = ilegiveis.Count == 0 ? "" : MensagemIlegivel(ilegiveis);
        PreviaMudou?.Invoke();
    }

    /// <summary>Linhas da fila cujo código de barras ficaria com barras finas demais no papel escolhido.</summary>
    private List<LinhaEtiquetaViewModel> ItensIlegiveis() => Fila
        .Where(l => !LegibilidadeCodigo.Legivel(l.CodigoBarras, FormatoSelecionado.EtiquetaLarguraMm))
        .ToList();

    private string MensagemIlegivel(List<LinhaEtiquetaViewModel> linhas) =>
        "Código de barras ilegível neste papel (as barras ficariam finas demais para o leitor): "
        + string.Join("; ", linhas.Select(l => $"{l.Nome} (precisa de etiqueta com pelo menos {LegibilidadeCodigo.LarguraMinimaMm(l.CodigoBarras!):0} mm)"))
        + ". Escolha um papel mais largo ou remova esses itens.";
}
