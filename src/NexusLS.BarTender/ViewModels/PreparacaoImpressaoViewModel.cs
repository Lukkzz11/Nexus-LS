using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public sealed partial class PreviaEtiquetaPreparacao : ObservableObject
{
    public string Tamanho { get; }

    [ObservableProperty]
    private int _quantidade;

    public string CodigoBarras { get; }

    public string Dimensoes { get; }

    public BitmapSource Imagem { get; }

    public LinhaSelecaoViewModel? Linha { get; }

    public PreviaEtiquetaPreparacao(
        string tamanho,
        int quantidade,
        string codigoBarras,
        string dimensoes,
        BitmapSource imagem,
        LinhaSelecaoViewModel? linha = null)
    {
        Tamanho = tamanho;
        _quantidade = quantidade;
        CodigoBarras = codigoBarras;
        Dimensoes = dimensoes;
        Imagem = imagem;
        Linha = linha;
    }
}

public sealed record CelulaPreviaPreparacao(
    BitmapSource? Imagem,
    string Rotulo,
    string Detalhe,
    bool Vazia);

public sealed record LinhaConferenciaPreparacao(string Tamanho, int Quantidade);

/// <summary>Opção do seletor de sensor/tipo de mídia (nome em português e resumo visual).</summary>
public sealed record OpcaoTipoMidia(string Nome, TipoMidiaEtiqueta Tipo, string NomeCurto = "", string Descricao = "");

/// <summary>
/// Página filha de preparação: recebe produto, tamanhos, quantidades e o modelo vinculado,
/// mostra a prévia real, configura mídia/impressora e envia para o Windows.
/// Não grava nada no banco e não altera o modelo do produto.
/// </summary>
public partial class PreparacaoImpressaoViewModel : ObservableObject
{
    private static readonly HashSet<string> CamposDeMidia = new(StringComparer.Ordinal)
    {
        nameof(NomeMidia),
        nameof(TipoMidia),
        nameof(LarguraEtiquetaMm),
        nameof(AlturaEtiquetaMm),
        nameof(EspacoHorizontalMm),
        nameof(EspacoVerticalMm),
        nameof(PaginaLarguraMm),
        nameof(PaginaAlturaMm),
        nameof(MargemEsquerdaMm),
        nameof(MargemSuperiorMm),
        nameof(Colunas),
        nameof(Linhas)
    };

    private readonly IDialogoService _dialogo;
    private readonly IImpressoraEtiquetas _impressora;
    private readonly Dictionary<string, BitmapSource> _cacheImagens = new(StringComparer.OrdinalIgnoreCase);
    private SelecaoImpressaoViewModel? _selecaoObservada;
    private bool _carregandoMidia;

    public Func<SelecaoImpressaoViewModel, AcaoProdutoCatalogo>? AbrirModalSelecao { get; set; }

    public Action? IrParaEstudio { get; set; }

    public Action? IrParaCatalogo { get; set; }

    public IReadOnlyList<PerfilImpressora> PerfisImpressora { get; } = PerfilImpressora.Perfis;

    public IReadOnlyList<OpcaoTipoMidia> TiposMidia { get; } = new[]
    {
        new OpcaoTipoMidia("Rolo com gap (espaço entre etiquetas)", TipoMidiaEtiqueta.RoloComGap, "Rolo com gap", "Espaço vazio entre etiquetas"),
        new OpcaoTipoMidia("Rolo com marca preta (black mark)", TipoMidiaEtiqueta.RoloMarcaPreta, "Marca preta", "Tarja preta atrás da etiqueta"),
        new OpcaoTipoMidia("Rolo contínuo (sem gap nem marca)", TipoMidiaEtiqueta.RoloContinuo, "Rolo contínuo", "Corte pela altura configurada"),
        new OpcaoTipoMidia("Folha A4 adesiva", TipoMidiaEtiqueta.Folha, "Folha A4", "Várias etiquetas por folha")
    };

    public ObservableCollection<PerfilMidia> MidiasDisponiveis { get; } = new(PerfisMidia.ListaPadrao);

    public ObservableCollection<string> ImpressorasInstaladas { get; } = new();

    public ObservableCollection<PreviaEtiquetaPreparacao> PreviaItens { get; } = new();

    public ObservableCollection<CelulaPreviaPreparacao> MapaPagina { get; } = new();

    public ObservableCollection<LinhaConferenciaPreparacao> Conferencia { get; } = new();

    [ObservableProperty]
    private SelecaoImpressaoViewModel? _selecao;

    [ObservableProperty]
    private Produto? _produto;

    [ObservableProperty]
    private ModeloEtiquetaDesign? _modeloOriginal;

    [ObservableProperty]
    private ModeloEtiquetaDesign? _modeloParaMidia;

    [ObservableProperty]
    private string _modeloNome = "Modelo do Estúdio";

    [ObservableProperty]
    private string _modeloDimensoes = "";

    [ObservableProperty]
    private string _modeloAjuste = "";

    [ObservableProperty]
    private bool _modeloPersonalizado;

    [ObservableProperty]
    private bool _modeloFoiAjustado;

    [ObservableProperty]
    private string _nomeLoja = "NEXUS LS";

    [ObservableProperty]
    private string _nomeMidia = "Rolo 50 × 30 mm (gap 3 mm)";

    [ObservableProperty]
    private TipoMidiaEtiqueta _tipoMidia = TipoMidiaEtiqueta.RoloComGap;

    [ObservableProperty]
    private OpcaoTipoMidia? _tipoMidiaSelecionado;

    [ObservableProperty]
    private double _larguraEtiquetaMm = 50;

    [ObservableProperty]
    private double _alturaEtiquetaMm = 30;

    [ObservableProperty]
    private double _espacoHorizontalMm;

    [ObservableProperty]
    private double _espacoVerticalMm = 3;

    [ObservableProperty]
    private double _paginaLarguraMm = 50;

    [ObservableProperty]
    private double _paginaAlturaMm = 30;

    [ObservableProperty]
    private double _margemEsquerdaMm;

    [ObservableProperty]
    private double _margemSuperiorMm;

    [ObservableProperty]
    private int _colunas = 1;

    [ObservableProperty]
    private int _linhas = 1;

    [ObservableProperty]
    private PerfilMidia? _midiaSelecionada;

    [ObservableProperty]
    private PerfilImpressora _perfilImpressoraSelecionado = PerfilImpressora.Perfis[0];

    [ObservableProperty]
    private double _larguraMaximaImpressoraMm = 104;

    [ObservableProperty]
    private string? _impressoraSelecionada;

    [ObservableProperty]
    private int _mapaColunas = 1;

    [ObservableProperty]
    private double _mapaLarguraMaxima = double.PositiveInfinity;

    [ObservableProperty]
    private string _mapaResumo = "";

    [ObservableProperty]
    private string _mensagemStatus = "";

    [ObservableProperty]
    private bool _envioConcluido;

    [ObservableProperty]
    private bool _temSelecao;

    public PreparacaoImpressaoViewModel(IDialogoService? dialogo = null, IImpressoraEtiquetas? impressora = null)
    {
        _dialogo = dialogo ?? DialogoService.Instancia;
        _impressora = impressora ?? new ImpressoraWindowsEtiquetas();

        _midiaSelecionada = PerfisMidia.Rolo50x30Gap;
        _tipoMidiaSelecionado = TiposMidia[0];
        CarregarMidia(PerfisMidia.Rolo50x30Gap);

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { Length: > 0 } nome && CamposDeMidia.Contains(nome))
                AtualizarMidia();
        };
    }

    // ---------------------------------------------------------------- entrada

    /// <summary>Recebe a seleção do modal (a mesma instância é mantida para não perder as quantidades).</summary>
    public void Carregar(SelecaoImpressaoViewModel selecao, ModeloEtiquetaDesign modelo, string nomeLoja)
    {
        // Trocar o tipo de etiqueta no modal muda as medidas do modelo: a mídia sugerida acompanha.
        var primeiraCarga = !ReferenceEquals(Selecao, selecao) ||
            ModeloOriginal is null ||
            Math.Abs(ModeloOriginal.LarguraMm - modelo.LarguraMm) > 0.05 ||
            Math.Abs(ModeloOriginal.AlturaMm - modelo.AlturaMm) > 0.05;

        Selecao = selecao;
        Produto = selecao.Produto;
        ModeloOriginal = modelo;
        NomeLoja = string.IsNullOrWhiteSpace(nomeLoja) ? "NEXUS LS" : nomeLoja;

        ModeloNome = modelo.Nome;
        ModeloPersonalizado = GerenciadorEtiquetasProdutoService.TemEtiquetaPersonalizada(selecao.Produto.Id);
        TemSelecao = true;
        EnvioConcluido = false;
        MensagemStatus = "";

        // Primeira abertura deste produto: parte de uma mídia parecida com o modelo vinculado.
        // Ao reabrir o mesmo produto, preserva a mídia que o operador já escolheu.
        if (primeiraCarga)
            SugerirMidiaParaModelo(modelo);

        AtualizarMidia();
        AtualizarTudo();
    }

    /// <summary>
    /// Escolhe o perfil de mídia com as mesmas medidas do modelo. Se não houver perfil,
    /// mantém o tipo atual e adota as medidas do modelo (o operador pode ajustar depois).
    /// </summary>
    private void SugerirMidiaParaModelo(ModeloEtiquetaDesign modelo)
    {
        var perfil = MidiasDisponiveis.FirstOrDefault(m =>
            Math.Abs(m.LarguraEtiquetaMm - modelo.LarguraMm) < 0.1 &&
            Math.Abs(m.AlturaEtiquetaMm - modelo.AlturaMm) < 0.1);

        if (perfil is not null)
        {
            MidiaSelecionada = perfil;
            return;
        }

        _carregandoMidia = true;
        try
        {
            MidiaSelecionada = null;
            LarguraEtiquetaMm = modelo.LarguraMm;
            AlturaEtiquetaMm = modelo.AlturaMm;
            EspacoHorizontalMm = 0;
            EspacoVerticalMm = TipoMidia == TipoMidiaEtiqueta.RoloComGap ? 3 : 0;
            PaginaLarguraMm = modelo.LarguraMm;
            PaginaAlturaMm = modelo.AlturaMm;
            Colunas = 1;
            Linhas = 1;
            MargemEsquerdaMm = 0;
            MargemSuperiorMm = 0;
            NomeMidia = $"Do modelo ({modelo.LarguraMm:0.#} × {modelo.AlturaMm:0.#} mm)";
        }
        finally
        {
            _carregandoMidia = false;
        }
    }

    public void DefinirImpressoras(IEnumerable<string> nomes, string? selecionada)
    {
        ImpressorasInstaladas.Clear();
        foreach (var nome in nomes) ImpressorasInstaladas.Add(nome);

        ImpressoraSelecionada = !string.IsNullOrWhiteSpace(selecionada) && ImpressorasInstaladas.Contains(selecionada)
            ? selecionada
            : ImpressorasInstaladas.FirstOrDefault();
    }

    // ---------------------------------------------------------------- mídia

    /// <summary>Mídia que será usada: os campos editáveis viram um perfil novo.</summary>
    public PerfilMidia MidiaAtual => CriarMidia(NomeMidiaAtual);

    public string NomeMidiaAtual =>
        MidiaSelecionada is null || MedidasIguais(MidiaSelecionada, CriarMidia(NomeMidia))
            ? NomeMidia
            : $"{NomeMidia} (ajustado)";

    private PerfilMidia CriarMidia(string nome) => new(
        nome,
        TipoMidia,
        LarguraEtiquetaMm,
        AlturaEtiquetaMm,
        EspacoHorizontalMm,
        EspacoVerticalMm,
        Colunas,
        Linhas,
        PaginaLarguraMm,
        PaginaAlturaMm,
        MargemEsquerdaMm,
        MargemSuperiorMm);

    public int EtiquetasPorPagina => Math.Max(1, MidiaAtual.EtiquetasPorPagina);

    public int PaginasNecessarias
    {
        get
        {
            if (Selecao is null || Selecao.Total <= 0) return 0;
            return (int)Math.Ceiling(Selecao.Total / (double)EtiquetasPorPagina);
        }
    }

    public string PaginasTexto
    {
        get
        {
            if (Selecao is null || Selecao.Total <= 0) return "Nenhuma etiqueta";
            return TipoMidia == TipoMidiaEtiqueta.Folha
                ? $"{PaginasNecessarias} folha(s) A4 · {EtiquetasPorPagina} etiqueta(s) por folha"
                : $"{PaginasNecessarias} página(s) no rolo · {EtiquetasPorPagina} por vez";
        }
    }

    public string ResumoMidia => MidiaAtual.Resumo;

    public bool PerfilPersonalizado =>
        PerfilImpressoraSelecionado.Nome.StartsWith("Outra", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AvisosMidia
    {
        get
        {
            var avisos = new List<string>();

            if (!PerfilImpressoraSelecionado.Aceita(TipoMidia))
                avisos.Add($"O perfil \"{PerfilImpressoraSelecionado.Nome}\" não aceita {DescreverTipo(TipoMidia)}. Troque a mídia ou escolha a impressora correta.");

            avisos.AddRange(MidiaAtual.Validar(LarguraMaximaImpressoraMm));
            return avisos.Distinct(StringComparer.Ordinal).ToList();
        }
    }

    public string AvisoMidia => string.Join(" ", AvisosMidia);

    public bool TemAvisoMidia => AvisosMidia.Count > 0;

    public bool PodeImprimir =>
        Selecao is { Total: > 0 } && !Selecao.TemErro && AvisosMidia.Count == 0;

    private bool MedidasIguais(PerfilMidia a, PerfilMidia b) =>
        a.Tipo == b.Tipo &&
        Math.Abs(a.LarguraEtiquetaMm - b.LarguraEtiquetaMm) < 0.05 &&
        Math.Abs(a.AlturaEtiquetaMm - b.AlturaEtiquetaMm) < 0.05 &&
        Math.Abs(a.EspacoHorizontalMm - b.EspacoHorizontalMm) < 0.05 &&
        Math.Abs(a.EspacoVerticalMm - b.EspacoVerticalMm) < 0.05 &&
        a.Colunas == b.Colunas &&
        a.Linhas == b.Linhas &&
        Math.Abs(a.PaginaLarguraMm - b.PaginaLarguraMm) < 0.05 &&
        Math.Abs(a.PaginaAlturaMm - b.PaginaAlturaMm) < 0.05 &&
        Math.Abs(a.MargemEsquerdaMm - b.MargemEsquerdaMm) < 0.05 &&
        Math.Abs(a.MargemSuperiorMm - b.MargemSuperiorMm) < 0.05;

    partial void OnMidiaSelecionadaChanged(PerfilMidia? value)
    {
        if (value is null) return;
        CarregarMidia(value);
        AtualizarMidia();
    }

    partial void OnTipoMidiaSelecionadoChanged(OpcaoTipoMidia? value)
    {
        if (value is not null && value.Tipo != TipoMidia)
            TipoMidia = value.Tipo;
    }

    partial void OnTipoMidiaChanged(TipoMidiaEtiqueta value)
    {
        var opcao = TiposMidia.FirstOrDefault(o => o.Tipo == value);
        if (opcao is not null && !ReferenceEquals(opcao, TipoMidiaSelecionado))
            TipoMidiaSelecionado = opcao;

        if (_carregandoMidia) return;

        OnPropertyChanged(nameof(PerfilPersonalizado));
        AtualizarMidia();
    }

    private void CarregarMidia(PerfilMidia midia)
    {
        _carregandoMidia = true;
        try
        {
            NomeMidia = midia.Nome;
            TipoMidia = midia.Tipo;
            LarguraEtiquetaMm = midia.LarguraEtiquetaMm;
            AlturaEtiquetaMm = midia.AlturaEtiquetaMm;
            EspacoHorizontalMm = midia.EspacoHorizontalMm;
            EspacoVerticalMm = midia.EspacoVerticalMm;
            PaginaLarguraMm = midia.PaginaLarguraMm;
            PaginaAlturaMm = midia.PaginaAlturaMm;
            MargemEsquerdaMm = midia.MargemEsquerdaMm;
            MargemSuperiorMm = midia.MargemSuperiorMm;
            Colunas = midia.Colunas;
            Linhas = midia.Linhas;
        }
        finally
        {
            _carregandoMidia = false;
        }
    }

    private void AtualizarMidia()
    {
        if (_carregandoMidia) return;

        OnPropertyChanged(nameof(MidiaAtual));
        OnPropertyChanged(nameof(NomeMidiaAtual));
        OnPropertyChanged(nameof(EtiquetasPorPagina));
        OnPropertyChanged(nameof(PaginasNecessarias));
        OnPropertyChanged(nameof(PaginasTexto));
        OnPropertyChanged(nameof(ResumoMidia));
        OnPropertyChanged(nameof(AvisosMidia));
        OnPropertyChanged(nameof(AvisoMidia));
        OnPropertyChanged(nameof(TemAvisoMidia));
        OnPropertyChanged(nameof(PodeImprimir));

        AtualizarModeloParaMidia();
        GerarPrevia();
    }

    partial void OnPerfilImpressoraSelecionadoChanged(PerfilImpressora value)
    {
        LarguraMaximaImpressoraMm = value.LarguraMaximaMm;
        OnPropertyChanged(nameof(PerfilPersonalizado));
        OnPropertyChanged(nameof(AvisosMidia));
        OnPropertyChanged(nameof(AvisoMidia));
        OnPropertyChanged(nameof(TemAvisoMidia));
        OnPropertyChanged(nameof(PodeImprimir));
    }

    partial void OnLarguraMaximaImpressoraMmChanged(double value)
    {
        OnPropertyChanged(nameof(AvisosMidia));
        OnPropertyChanged(nameof(AvisoMidia));
        OnPropertyChanged(nameof(TemAvisoMidia));
        OnPropertyChanged(nameof(PodeImprimir));
    }

    partial void OnSelecaoChanged(SelecaoImpressaoViewModel? value)
    {
        if (_selecaoObservada is not null)
            _selecaoObservada.QuantidadesAlteradas -= Selecao_QuantidadesAlteradas;

        _selecaoObservada = value;

        if (_selecaoObservada is not null)
            _selecaoObservada.QuantidadesAlteradas += Selecao_QuantidadesAlteradas;

        _cacheImagens.Clear();
        OnPropertyChanged(nameof(TotalEtiquetas));
        OnPropertyChanged(nameof(ResumoConferencia));
        OnPropertyChanged(nameof(OrdemImpressao));
        OnPropertyChanged(nameof(PodeImprimir));
        AtualizarTudo();
    }

    private void Selecao_QuantidadesAlteradas(object? sender, EventArgs e)
    {
        AtualizarQuantidadesAoVivo();
    }

    /// <summary>
    /// Recalcula totais, páginas, conferência e mapa ao editar as quantidades direto na tela de preparação,
    /// reaproveitando os bitmaps já desenhados e mantendo os cartões existentes para não perder o foco do teclado.
    /// </summary>
    public void AtualizarQuantidadesAoVivo()
    {
        OnPropertyChanged(nameof(TotalEtiquetas));
        OnPropertyChanged(nameof(ResumoConferencia));
        OnPropertyChanged(nameof(OrdemImpressao));
        OnPropertyChanged(nameof(PaginasNecessarias));
        OnPropertyChanged(nameof(PaginasTexto));
        OnPropertyChanged(nameof(PodeImprimir));
        AtualizarConferencia();
        SincronizarPreviaSemPerderFoco();
    }

    private static string DescreverTipo(TipoMidiaEtiqueta tipo) => tipo switch
    {
        TipoMidiaEtiqueta.RoloComGap => "rolo com gap",
        TipoMidiaEtiqueta.RoloMarcaPreta => "rolo com marca preta",
        TipoMidiaEtiqueta.RoloContinuo => "rolo contínuo",
        TipoMidiaEtiqueta.Folha => "folha A4",
        _ => "esta mídia"
    };

    // ---------------------------------------------------------------- modelo e prévia

    private void AtualizarModeloParaMidia()
    {
        _cacheImagens.Clear();

        if (ModeloOriginal is null)
        {
            ModeloParaMidia = null;
            ModeloFoiAjustado = false;
            ModeloAjuste = "";
            return;
        }

        ModeloDimensoes = $"{LarguraEtiquetaMm:0.#} × {AlturaEtiquetaMm:0.#} mm";

        var ajuste = ModeloMidiaService.Ajustar(ModeloOriginal, LarguraEtiquetaMm, AlturaEtiquetaMm);
        ModeloParaMidia = ajuste.Modelo;
        ModeloFoiAjustado = ajuste.Alterado;
        ModeloAjuste = ajuste.Descricao;
    }

    private void AtualizarTudo()
    {
        OnPropertyChanged(nameof(TotalEtiquetas));
        OnPropertyChanged(nameof(ResumoConferencia));
        OnPropertyChanged(nameof(OrdemImpressao));
        OnPropertyChanged(nameof(PaginasNecessarias));
        OnPropertyChanged(nameof(PaginasTexto));
        OnPropertyChanged(nameof(PodeImprimir));
        OnPropertyChanged(nameof(TemAvisoMidia));
        AtualizarConferencia();
        AtualizarModeloParaMidia();
        GerarPrevia();
    }

    private void AtualizarConferencia()
    {
        Conferencia.Clear();
        if (Selecao is null) return;

        foreach (var linha in Selecao.Linhas.Where(l => l.TemQuantidade))
            Conferencia.Add(new LinhaConferenciaPreparacao(linha.TamanhoExibicao, linha.Quantidade));
    }

    private BitmapSource ObterOuRenderizarImagem(LinhaSelecaoViewModel linha)
    {
        var chave = linha.Tamanho;
        if (!_cacheImagens.TryGetValue(chave, out var imagem) && ModeloParaMidia is not null && Produto is not null)
        {
            var tamanho = linha.EhSemTamanho ? null : linha.Tamanho;
            imagem = EditorDesenhoService.RenderizarBitmap(ModeloParaMidia, Produto, tamanho, NomeLoja, escalaDpi: 2.5);
            _cacheImagens[chave] = imagem;
        }
        return imagem!;
    }

    private void SincronizarPreviaSemPerderFoco()
    {
        MapaPagina.Clear();

        if (Selecao is null || ModeloParaMidia is null || Produto is null || Selecao.Total <= 0)
        {
            PreviaItens.Clear();
            MapaResumo = "";
            return;
        }

        var dimensoes = $"{ModeloParaMidia.LarguraMm:0.#} × {ModeloParaMidia.AlturaMm:0.#} mm";
        var comQtd = Selecao.Linhas.Where(l => l.TemQuantidade).ToList();

        // Remove os que zeraram
        for (var i = PreviaItens.Count - 1; i >= 0; i--)
        {
            var card = PreviaItens[i];
            if (!comQtd.Any(l => string.Equals(l.TamanhoExibicao, card.Tamanho, StringComparison.OrdinalIgnoreCase)))
                PreviaItens.RemoveAt(i);
        }

        // Atualiza os existentes ou insere os novos na ordem das linhas
        for (var idx = 0; idx < comQtd.Count; idx++)
        {
            var linha = comQtd[idx];
            var existente = PreviaItens.FirstOrDefault(p =>
                string.Equals(p.Tamanho, linha.TamanhoExibicao, StringComparison.OrdinalIgnoreCase));

            var imagem = ObterOuRenderizarImagem(linha);
            if (existente is not null)
            {
                existente.Quantidade = linha.Quantidade;
            }
            else
            {
                var codigo = CodigoBarrasTamanho.Resolver(Produto, linha.EhSemTamanho ? null : linha.Tamanho) ?? "—";
                var novo = new PreviaEtiquetaPreparacao(
                    linha.TamanhoExibicao,
                    linha.Quantidade,
                    codigo,
                    dimensoes,
                    imagem,
                    linha);

                if (idx <= PreviaItens.Count)
                    PreviaItens.Insert(idx, novo);
                else
                    PreviaItens.Add(novo);
            }
        }

        MontarMapaPagina(_cacheImagens);
    }

    /// <summary>Desenha uma imagem real por tamanho e monta o mapa da primeira página/rolo.</summary>
    public void GerarPrevia()
    {
        PreviaItens.Clear();
        MapaPagina.Clear();

        if (Selecao is null || ModeloParaMidia is null || Produto is null || Selecao.Total <= 0)
        {
            MapaResumo = "";
            return;
        }

        var dimensoes = $"{ModeloParaMidia.LarguraMm:0.#} × {ModeloParaMidia.AlturaMm:0.#} mm";

        foreach (var linha in Selecao.Linhas.Where(l => l.TemQuantidade))
        {
            var imagem = ObterOuRenderizarImagem(linha);
            var codigo = CodigoBarrasTamanho.Resolver(Produto, linha.EhSemTamanho ? null : linha.Tamanho) ?? "—";
            PreviaItens.Add(new PreviaEtiquetaPreparacao(
                linha.TamanhoExibicao,
                linha.Quantidade,
                codigo,
                dimensoes,
                imagem,
                linha));
        }

        MontarMapaPagina(_cacheImagens);
    }

    [RelayCommand]
    public void SelecionarMidia(PerfilMidia? midia)
    {
        if (midia is not null)
            MidiaSelecionada = midia;
    }

    [RelayCommand]
    public void SelecionarTipoMidia(OpcaoTipoMidia? opcao)
    {
        if (opcao is not null)
            TipoMidiaSelecionado = opcao;
    }

    private void MontarMapaPagina(IReadOnlyDictionary<string, BitmapSource> imagens)
    {
        if (Selecao is null) return;

        var ehFolha = TipoMidia == TipoMidiaEtiqueta.Folha;
        var limite = ehFolha ? EtiquetasPorPagina : Math.Min(6, Selecao.Total);
        MapaColunas = ehFolha ? Math.Max(1, Colunas) : 1;

        // No rolo, uma "página" é a própria etiqueta: a célula mantém a proporção real da mídia
        // em vez de esticar pela largura toda.
        const double alturaCelula = 74;
        MapaLarguraMaxima = ehFolha || PaginaAlturaMm <= 0
            ? double.PositiveInfinity
            : Math.Max(60, alturaCelula * (PaginaLarguraMm / PaginaAlturaMm));

        var sequencia = new List<LinhaSelecaoViewModel>();
        foreach (var linha in Selecao.Linhas.Where(l => l.TemQuantidade))
            for (var i = 0; i < Math.Min(linha.Quantidade, limite); i++)
                sequencia.Add(linha);

        for (var i = 0; i < limite; i++)
        {
            if (i < sequencia.Count)
            {
                var linha = sequencia[i];
                imagens.TryGetValue(linha.Tamanho, out var imagem);
                MapaPagina.Add(new CelulaPreviaPreparacao(imagem, linha.TamanhoExibicao, $"#{i + 1}", false));
            }
            else
            {
                MapaPagina.Add(new CelulaPreviaPreparacao(null, "", "", true));
            }
        }

        MapaResumo = ehFolha
            ? $"Primeira folha · {MapaPagina.Count(c => !c.Vazia)} de {EtiquetasPorPagina} posições"
            : $"Primeiras {Math.Min(limite, Selecao.Total)} de {Selecao.Total} etiquetas (uma por vez no rolo)";
    }

    // ---------------------------------------------------------------- ações

    public int TotalEtiquetas => Selecao?.Total ?? 0;

    public string ResumoConferencia => Selecao is null
        ? "Nenhum produto selecionado"
        : $"{Selecao.Nome} · Ref. {Selecao.ReferenciaExibicao} · {Selecao.ResumoSelecao}";

    /// <summary>Ordem em que as etiquetas entram na impressão (tamanho por tamanho).</summary>
    public string OrdemImpressao => Selecao is null
        ? ""
        : string.Join(" → ", Selecao.Linhas
            .Where(l => l.TemQuantidade)
            .Select(l => $"{l.Quantidade} × {l.TamanhoExibicao}"));

    [RelayCommand]
    public void EditarQuantidades()
    {
        if (Selecao is null || AbrirModalSelecao is null) return;

        var acao = AbrirModalSelecao(Selecao);
        if (acao == AcaoProdutoCatalogo.Estudio)
        {
            IrParaEstudio?.Invoke();
            return;
        }

        if (acao == AcaoProdutoCatalogo.Impressao || acao == AcaoProdutoCatalogo.Nenhuma)
            AtualizarTudo();
    }

    [RelayCommand]
    public void VoltarAoCatalogo() => IrParaCatalogo?.Invoke();

    [RelayCommand]
    public void RestaurarMidiaDoPerfil()
    {
        if (MidiaSelecionada is not null)
        {
            CarregarMidia(MidiaSelecionada);
            AtualizarMidia();
        }
    }

    [RelayCommand]
    public void Imprimir() => EnviarParaImpressora();

    /// <summary>Envia de verdade (ou para a impressora de teste). Devolve false se cancelou ou falhou.</summary>
    public bool EnviarParaImpressora()
    {
        if (Selecao is null)
        {
            _dialogo.Informar("Escolha um produto no catálogo antes de preparar a impressão.");
            return false;
        }

        var erros = new List<string>();
        erros.AddRange(Selecao.ValidarParaImprimir());
        erros.AddRange(AvisosMidia);

        if (erros.Count > 0)
        {
            _dialogo.Erro("Confira antes de imprimir:\n\n• " + string.Join("\n• ", erros), "Preparação de Impressão");
            return false;
        }

        if (ModeloParaMidia is null || Produto is null) return false;

        try
        {
            var itens = Selecao.Linhas
                .Where(l => l.TemQuantidade)
                .Select(l => new ItemFila(Produto, l.Quantidade, l.EhSemTamanho ? "" : l.Tamanho))
                .ToList();

            var enviou = _impressora.ImprimirPreparacao(
                itens,
                ModeloParaMidia,
                NomeLoja,
                MidiaAtual,
                ImpressoraSelecionada,
                primeiraPosicao: 1);

            if (!enviou)
            {
                EnvioConcluido = false;
                MensagemStatus = "Envio cancelado no diálogo de impressão do Windows.";
                _dialogo.Informar(MensagemStatus, "Impressão não enviada");
                return false;
            }

            EnvioConcluido = true;
            MensagemStatus = $"Envio concluído: {Selecao.Total} etiqueta(s) para {ImpressoraSelecionada ?? "a impressora padrão"} · {MidiaAtual.Nome}.";
            _dialogo.Sucesso(MensagemStatus);
            return true;
        }
        catch (Exception ex)
        {
            EnvioConcluido = false;
            MensagemStatus = $"Falha no envio: {ex.Message}";
            _dialogo.Erro($"Não foi possível enviar para a impressora:\n{ex.Message}", "Falha na impressão");
            return false;
        }
    }
}
