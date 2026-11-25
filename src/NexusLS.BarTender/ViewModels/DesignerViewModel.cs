using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class DesignerViewModel : ObservableObject
{
    [ObservableProperty]
    private ModeloEtiquetaDesign _modelo;

    [ObservableProperty]
    private ElementoDesign? _elementoSelecionado;

    [ObservableProperty]
    private BitmapSource? _previewImagem;

    [ObservableProperty]
    private string _zplCodigo = "";

    [ObservableProperty]
    private double _zoomNivel = 2.0;

    [ObservableProperty]
    private bool _emOperacaoInterativa = false;

    [ObservableProperty]
    private string _nomeLoja = "NEXUS LS";

    [ObservableProperty]
    private Produto? _produtoAtual;

    [ObservableProperty]
    private string _tamanhoParaTeste = "M";

    public ObservableCollection<string> TamanhosParaTesteDisponiveis { get; } = new()
    {
        "P", "M", "G", "GG"
    };

    public Action? SolicitarIrParaImpressao { get; set; }

    public Action? SolicitarPadronizarEtiquetas { get; set; }

    [RelayCommand]
    public void IrParaImpressaoRapida() => SolicitarIrParaImpressao?.Invoke();

    [RelayCommand]
    public void AbrirPadronizarEtiqueta() => SolicitarPadronizarEtiquetas?.Invoke();

    partial void OnTamanhoParaTesteChanged(string value)
    {
        AplicarRegrasInteligentesDeGrade();
        AtualizarPreview();
        if (ModalCodigoAberto)
        {
            AtualizarModalCodigoPreview();
        }
    }

    /// <summary>Opção da lista de grades que mostra só os tamanhos cadastrados no produto.</summary>
    public const string GradeDoCadastro = "Do cadastro";

    public IReadOnlyList<string> GradesDisponiveis { get; } =
        new[] { GradeDoCadastro }.Concat(GradesDeTamanho.Todas.Select(g => g.Nome)).ToArray();

    [ObservableProperty]
    private string _gradeSelecionada = GradeDoCadastro;

    /// <summary>Arquivo onde a grade de cada produto fica guardada (nulo = o padrão do BarTender).</summary>
    public string? CaminhoPreferencias { get; set; }

    private bool _restaurandoGrade;

    /// <summary>O tamanho em teste não está no cadastro: aparece na etiqueta, mas o código continua o do produto.</summary>
    public bool TamanhoForaDoCadastro =>
        ProdutoAtual != null && TemTamanhoEmTeste &&
        ProdutoAtual.Tamanhos?.Any(t => string.Equals(t.Tamanho?.Trim(), TamanhoParaTeste, StringComparison.OrdinalIgnoreCase)) != true;

    /// <summary>Há um tamanho escolhido, mas o desenho não tem onde mostrá-lo.</summary>
    public bool FaltaTamanhoNaEtiqueta =>
        TemTamanhoEmTeste && Modelo?.Elementos.Any(e =>
            e.Variavel == VariavelEtiqueta.Tamanho || e.Texto?.Contains("{Tamanho}") == true) == false;

    private bool TemTamanhoEmTeste =>
        !string.IsNullOrWhiteSpace(TamanhoParaTeste) &&
        !string.Equals(TamanhoParaTeste, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase);

    private void NotificarAvisosDeTamanho()
    {
        OnPropertyChanged(nameof(TamanhoForaDoCadastro));
        OnPropertyChanged(nameof(FaltaTamanhoNaEtiqueta));
    }

    partial void OnProdutoAtualChanged(Produto? value)
    {
        var salva = value is null
            ? null
            : PreferenciasBarTender.Carregar(CaminhoPreferencias).GradePorProduto.GetValueOrDefault(value.Id);

        _restaurandoGrade = true;
        GradeSelecionada = salva != null && GradesDisponiveis.Contains(salva) ? salva : GradeDoCadastro;
        _restaurandoGrade = false;

        MontarTamanhosParaTeste();
    }

    partial void OnGradeSelecionadaChanged(string value)
    {
        if (_restaurandoGrade || string.IsNullOrEmpty(value)) return;

        if (ProdutoAtual != null)
        {
            var preferencias = PreferenciasBarTender.Carregar(CaminhoPreferencias);
            if (value == GradeDoCadastro) preferencias.GradePorProduto.Remove(ProdutoAtual.Id);
            else preferencias.GradePorProduto[ProdutoAtual.Id] = value;
            preferencias.Salvar(CaminhoPreferencias);
        }

        MontarTamanhosParaTeste();
        AtualizarPreview();
    }

    private void MontarTamanhosParaTeste()
    {
        var anterior = TamanhoParaTeste;
        var lista = TamanhosDaGradeEscolhida();

        TamanhosParaTesteDisponiveis.Clear();
        foreach (var t in lista) TamanhosParaTesteDisponiveis.Add(t);

        TamanhoParaTeste = lista.Contains(anterior) ? anterior : lista.Contains("M") ? "M" : lista[0];
        NotificarAvisosDeTamanho();
    }

    private List<string> TamanhosDaGradeEscolhida()
    {
        var grade = GradesDeTamanho.Todas.FirstOrDefault(g => g.Nome == GradeSelecionada);
        if (grade != null) return grade.Tamanhos.ToList();

        var cadastrados = ProdutoAtual?.Tamanhos?
            .Where(t => !string.IsNullOrWhiteSpace(t.Tamanho))
            .OrderBy(t => t.Ordem)
            .ToList() ?? new List<ProdutoTamanho>();

        bool EhSemTamanho(ProdutoTamanho t) =>
            string.Equals(t.Tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase);

        // Com grade real, a linha "Sem tamanho" zerada só atrapalha.
        var temTamanhosReais = cadastrados.Any(t => !EhSemTamanho(t));
        var lista = cadastrados
            .Where(t => !(temTamanhosReais && t.Estoque <= 0 && EhSemTamanho(t)))
            .Select(t => t.Tamanho.Trim())
            .Distinct()
            .ToList();

        return lista.Count > 0 ? lista : new List<string> { "P", "M", "G", "GG" };
    }

    [ObservableProperty]
    private string _presetSelecionado = "Padrão Gôndola Varejo (50 × 30 mm)";

    [ObservableProperty]
    private bool _galeriaModelosAberta = false;

    [ObservableProperty]
    private string _filtroCategoriaGaleria = "Todos";

    [ObservableProperty]
    private PredefinicaoEtiqueta? _predefinicaoSelecionadaNaGaleria;

    // =========================================================================
    // NAVEGAÇÃO & FERRAMENTAS CAD / COREL / PHOTOSHOP
    // =========================================================================
    [ObservableProperty]
    private bool _ferramentaMaoAtiva = false;

    [ObservableProperty]
    private string _abaInspetorAtiva = "Propriedades"; // "Propriedades" ou "Camadas"

    [ObservableProperty]
    private string? _caminhoArquivoAtual;

    // Clipboard interno para Copiar / Recortar / Colar
    private static readonly List<ElementoDesign> _areaTransferencia = new();

    public IEnumerable<ElementoDesign> CamadasOrdenadas => Modelo?.Elementos.Reverse() ?? Enumerable.Empty<ElementoDesign>();
    public int TotalCamadas => Modelo?.Elementos.Count ?? 0;

    // =========================================================================
    // PROPRIEDADES DE CONTROLE DIRETO DA FOLHA / ETIQUETA (ESTILO CORELDRAW)
    // =========================================================================
    public double FolhaLarguraMm
    {
        get => Modelo?.LarguraMm ?? 50.0;
        set
        {
            if (Modelo != null && Math.Abs(Modelo.LarguraMm - value) > 0.05 && value >= 5.0)
            {
                RegistrarSnapshot();
                Modelo.LarguraMm = Math.Round(value, 1);
                OnPropertyChanged();
                ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
                AtualizarPreview();
            }
        }
    }

    public double FolhaAlturaMm
    {
        get => Modelo?.AlturaMm ?? 30.0;
        set
        {
            if (Modelo != null && Math.Abs(Modelo.AlturaMm - value) > 0.05 && value >= 5.0)
            {
                RegistrarSnapshot();
                Modelo.AlturaMm = Math.Round(value, 1);
                OnPropertyChanged();
                ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
                AtualizarPreview();
            }
        }
    }

    [RelayCommand]
    public void AlternarOrientacaoFolha()
    {
        if (Modelo == null) return;
        RegistrarSnapshot();
        var temp = Modelo.LarguraMm;
        Modelo.LarguraMm = Modelo.AlturaMm;
        Modelo.AlturaMm = temp;
        OnPropertyChanged(nameof(FolhaLarguraMm));
        OnPropertyChanged(nameof(FolhaAlturaMm));
        ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
        AtualizarPreview();
    }

    [RelayCommand]
    public void DefinirTamanhoFolha(string dimensoes)
    {
        if (Modelo == null || string.IsNullOrWhiteSpace(dimensoes)) return;
        var partes = dimensoes.Split('x', 'X');
        if (partes.Length == 2 && double.TryParse(partes[0].Trim(), out var larg) && double.TryParse(partes[1].Trim(), out var alt))
        {
            RegistrarSnapshot();
            Modelo.LarguraMm = larg;
            Modelo.AlturaMm = alt;
            OnPropertyChanged(nameof(FolhaLarguraMm));
            OnPropertyChanged(nameof(FolhaAlturaMm));
            ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
            AtualizarPreview();
        }
    }

    // =========================================================================
    // MODAL GERADOR DE CÓDIGO DE BARRAS & QR CODE
    // =========================================================================
    [ObservableProperty]
    private bool _modalCodigoAberto = false;

    [ObservableProperty]
    private string _modalCodigoTitulo = "Gerador de Código de Barras & QR Code";

    [ObservableProperty]
    private TipoSimboloCodigo _modalCodigoTipo = TipoSimboloCodigo.Code128;

    [ObservableProperty]
    private bool _modalCodigoEhQrCode = false;

    [ObservableProperty]
    private bool _modalCodigoOrigemProduto = true; // true = Produto, false = Fixo

    [ObservableProperty]
    private VariavelEtiqueta _modalCodigoVariavel = VariavelEtiqueta.CodigoBarras;

    [ObservableProperty]
    private string _modalCodigoValorFixo = "7891234567890";

    [ObservableProperty]
    private bool _modalCodigoMostrarLegivel = true;

    [ObservableProperty]
    private double _modalCodigoLarguraMm = 38.0;

    [ObservableProperty]
    private double _modalCodigoAlturaMm = 12.0;

    [ObservableProperty]
    private BitmapSource? _modalCodigoPreview;

    [ObservableProperty]
    private string _modalCodigoAviso = "";

    [ObservableProperty]
    private string _modalCodigoValorResolvido = "";

    partial void OnModalCodigoTipoChanged(TipoSimboloCodigo value)
    {
        ModalCodigoEhQrCode = value == TipoSimboloCodigo.QrCode;
        if (ModalCodigoEhQrCode)
        {
            ModalCodigoMostrarLegivel = false;
            if (ModalCodigoLarguraMm != ModalCodigoAlturaMm)
            {
                ModalCodigoLarguraMm = 15.0;
                ModalCodigoAlturaMm = 15.0;
            }
        }
        else
        {
            if (ModalCodigoAlturaMm > 25.0 || ModalCodigoLarguraMm == ModalCodigoAlturaMm)
            {
                ModalCodigoLarguraMm = Math.Min(38.0, Math.Max(20.0, Modelo.LarguraMm - 4.0));
                ModalCodigoAlturaMm = 12.0;
            }
        }
        AtualizarModalCodigoPreview();
    }

    partial void OnModalCodigoOrigemProdutoChanged(bool value) => AtualizarModalCodigoPreview();
    partial void OnModalCodigoVariavelChanged(VariavelEtiqueta value) => AtualizarModalCodigoPreview();
    partial void OnModalCodigoValorFixoChanged(string value) => AtualizarModalCodigoPreview();
    partial void OnModalCodigoMostrarLegivelChanged(bool value) => AtualizarModalCodigoPreview();
    partial void OnModalCodigoLarguraMmChanged(double value) => AtualizarModalCodigoPreview();
    partial void OnModalCodigoAlturaMmChanged(double value) => AtualizarModalCodigoPreview();

    // =========================================================================
    // PROPRIEDADES DO HUB DE NOVO DOCUMENTO (ESTILO ILLUSTRATOR / CORELDRAW)
    // =========================================================================
    [ObservableProperty]
    private bool _novoDocumentoAberto = false;

    [ObservableProperty]
    private string _novoDocNome = "Nova Etiqueta";

    [ObservableProperty]
    private double _novoDocLargura = 50.0;

    [ObservableProperty]
    private double _novoDocAltura = 30.0;

    [ObservableProperty]
    private string _novoDocUnidade = "mm";

    [ObservableProperty]
    private bool _novoDocIsPaisagem = true;

    [ObservableProperty]
    private string _novoDocTipo = "Normal"; // "Normal", "Composicao", "EmBranco"

    [ObservableProperty]
    private string _novoDocCategoriaFiltro = "Todos";

    [ObservableProperty]
    private PredefinicaoEtiqueta? _novoDocPredefinicaoSelecionada;

    public ObservableCollection<PredefinicaoEtiqueta> CatalogoModelos { get; } =
        new(PredefinicaoEtiqueta.ObterCatalogoCompleto());

    public ObservableCollection<string> PresetsDisponiveis { get; } = new();

    public ObservableCollection<string> CategoriasGaleria { get; } = new()
    {
        "Todos",
        "Varejo & Gôndola",
        "Composição Têxtil",
        "Vestuário & Moda",
        "Promoções & Ofertas",
        "Joias & Óculos",
        "Logística & Caixas",
        "Folhas A4",
        "Personalizado (Em Branco)"
    };

    public ObservableCollection<string> CategoriasNovoDoc { get; } = new()
    {
        "Todos",
        "Varejo & Gôndola",
        "Composição Têxtil",
        "Vestuário & Moda",
        "Promoções & Ofertas",
        "Joias & Óculos",
        "Logística & Caixas",
        "Folhas A4",
        "Personalizado (Em Branco)"
    };

    public IEnumerable<PredefinicaoEtiqueta> ModelosFiltradosNaGaleria =>
        FiltroCategoriaGaleria == "Todos"
            ? CatalogoModelos
            : CatalogoModelos.Where(m => m.Categoria == FiltroCategoriaGaleria);

    public IEnumerable<PredefinicaoEtiqueta> ModelosFiltradosNovoDoc =>
        NovoDocCategoriaFiltro == "Todos"
            ? CatalogoModelos
            : CatalogoModelos.Where(m => m.Categoria == NovoDocCategoriaFiltro);

    public IReadOnlyList<TipoSimboloCodigo> TiposCodigoDisponiveis { get; } =
        Enum.GetValues<TipoSimboloCodigo>();

    public IReadOnlyList<VariavelEtiqueta> VariaveisDisponiveis { get; } =
        Enum.GetValues<VariavelEtiqueta>();

    public IReadOnlyList<TextAlignment> AlinhamentosDisponiveis { get; } =
        Enum.GetValues<TextAlignment>();

    public ObservableCollection<string> FontesDisponiveis { get; } = new();

    public IReadOnlyList<double> RotacoesDisponiveis { get; } = new double[] { 0, 90, 180, 270 };

    public ObservableCollection<ProdutoItemViewModel> ListaProdutosCatalog { get; } = new();

    // =========================================================================
    // VÍNCULO E CADASTRO DE ETIQUETA POR PRODUTO
    // =========================================================================
    [ObservableProperty]
    private bool _modoProdutoAtivo = false;

    [ObservableProperty]
    private int? _produtoIdAtivo;

    [ObservableProperty]
    private string _nomeProdutoAtivo = "";

    [ObservableProperty]
    private string _refProdutoAtivo = "";

    [ObservableProperty]
    private bool _produtoTemEtiquetaExclusiva = false;

    public event EventHandler? ElementoPropriedadeAlterada;

    [ObservableProperty]
    private int _indiceProdutoAtual = -1;

    [ObservableProperty]
    private bool _mostrarDadosReais = true;

    [ObservableProperty]
    private bool _mostrarGrade = true;

    [ObservableProperty]
    private bool _snapGrade = true;

    public void SincronizarProdutos(IEnumerable<ProdutoItemViewModel> produtos)
    {
        ListaProdutosCatalog.Clear();
        foreach (var p in produtos)
        {
            ListaProdutosCatalog.Add(p);
        }
        if (ProdutoAtual == null && ListaProdutosCatalog.Count > 0)
        {
            IndiceProdutoAtual = 0;
            ProdutoAtual = ListaProdutosCatalog[0].Produto;
        }
    }

    [RelayCommand]
    public void ProximoProduto()
    {
        if (ListaProdutosCatalog.Count == 0) return;
        IndiceProdutoAtual = (IndiceProdutoAtual + 1) % ListaProdutosCatalog.Count;
        ProdutoAtual = ListaProdutosCatalog[IndiceProdutoAtual].Produto;
        AtualizarPreview();
    }

    [RelayCommand]
    public void ProdutoAnterior()
    {
        if (ListaProdutosCatalog.Count == 0) return;
        IndiceProdutoAtual = (IndiceProdutoAtual - 1 + ListaProdutosCatalog.Count) % ListaProdutosCatalog.Count;
        ProdutoAtual = ListaProdutosCatalog[IndiceProdutoAtual].Produto;
        AtualizarPreview();
    }

    [RelayCommand]
    public void InserirChipTexto(string chip)
    {
        if (ElementoSelecionado == null || ElementoSelecionado.Tipo != TipoElemento.Texto) return;
        ElementoSelecionado.Texto = string.IsNullOrWhiteSpace(ElementoSelecionado.Texto)
            ? chip
            : $"{ElementoSelecionado.Texto} {chip}";
        AtualizarPreview();
    }

    [RelayCommand]
    public void DefinirCorTexto(string hex)
    {
        if (ElementoSelecionado == null) return;
        ElementoSelecionado.CorTexto = hex;
        AtualizarPreview();
    }

    [RelayCommand]
    public void DefinirRotacao(string anguloStr)
    {
        if (ElementoSelecionado == null) return;
        if (double.TryParse(anguloStr, System.Globalization.CultureInfo.InvariantCulture, out var angulo))
        {
            ElementoSelecionado.Rotacao = angulo;
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void AlternarMostrarDadosReais()
    {
        MostrarDadosReais = !MostrarDadosReais;
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlternarGrade()
    {
        MostrarGrade = !MostrarGrade;
    }

    [RelayCommand]
    public void AlternarSnap()
    {
        SnapGrade = !SnapGrade;
    }

    [RelayCommand]
    public void Zoom100() => ZoomNivel = 1.0;

    [RelayCommand]
    public void Zoom200() => ZoomNivel = 2.0;

    [RelayCommand]
    public void Zoom300() => ZoomNivel = 3.0;

    private readonly IDialogoService _dialogo;

    public DesignerViewModel(IDialogoService? dialogo = null)
    {
        _dialogo = dialogo ?? DialogoService.Instancia;
        foreach (var m in CatalogoModelos)
        {
            PresetsDisponiveis.Add(m.Nome);
        }

        CarregarFontesDoSistema();

        _modelo = ModeloEtiquetaDesign.CriarPadrao50x30();
        InscreverEventosModelo();
        AtualizarPreview();
    }

    private void CarregarFontesDoSistema()
    {
        try
        {
            FontesDisponiveis.Clear();
            var fontes = System.Windows.Media.Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .OrderBy(s => s)
                .Distinct()
                .ToList();

            foreach (var f in fontes)
            {
                FontesDisponiveis.Add(f);
            }
        }
        catch
        {
            FontesDisponiveis.Add("Segoe UI");
            FontesDisponiveis.Add("Arial");
            FontesDisponiveis.Add("Consolas");
            FontesDisponiveis.Add("Times New Roman");
        }
    }

    private void InscreverEventosModelo()
    {
        Modelo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModeloEtiquetaDesign.LarguraMm) ||
                e.PropertyName == nameof(ModeloEtiquetaDesign.AlturaMm))
            {
                OnPropertyChanged(nameof(FolhaLarguraMm));
                OnPropertyChanged(nameof(FolhaAlturaMm));
                ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
            }
            AtualizarPreview();
        };
        Modelo.Elementos.CollectionChanged += (_, _) =>
        {
            InscreverElementos();
            NotificarCamadasAlteradas();
            AtualizarPreview();
            ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
        };
        InscreverElementos();
        NotificarCamadasAlteradas();
    }

    private void InscreverElementos()
    {
        foreach (var el in Modelo.Elementos)
        {
            el.PropertyChanged -= Elemento_PropertyChanged;
            el.PropertyChanged += Elemento_PropertyChanged;
        }
    }

    private void Elemento_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ElementoDesign.IsSelected))
        {
            if (!EmOperacaoInterativa)
            {
                AtualizarPreview();
                ElementoPropriedadeAlterada?.Invoke(sender, EventArgs.Empty);
            }
        }
    }

    public void DefinirProduto(Produto? p, string? loja = null, bool carregarEtiquetaSalvaSeHouver = true)
    {
        ProdutoAtual = p;
        if (!string.IsNullOrWhiteSpace(loja))
        {
            NomeLoja = loja;
        }

        if (p != null)
        {
            ModoProdutoAtivo = true;
            ProdutoIdAtivo = p.Id;
            NomeProdutoAtivo = p.Nome;
            RefProdutoAtivo = p.Referencia ?? "";
            ProdutoTemEtiquetaExclusiva = GerenciadorEtiquetasProdutoService.TemEtiquetaPersonalizada(p.Id);

            if (carregarEtiquetaSalvaSeHouver && ProdutoTemEtiquetaExclusiva)
            {
                var salva = GerenciadorEtiquetasProdutoService.CarregarEtiquetaProduto(p.Id);
                if (salva != null)
                {
                    Modelo = salva;
                    PresetSelecionado = salva.Nome;
                    AplicarRegrasInteligentesDeGrade();
                    InscreverEventosModelo();
                }
            }
            else
            {
                AplicarRegrasInteligentesDeGrade();
            }
        }
        else
        {
            ModoProdutoAtivo = false;
            ProdutoIdAtivo = null;
            NomeProdutoAtivo = "";
            RefProdutoAtivo = "";
            ProdutoTemEtiquetaExclusiva = false;
        }

        AtualizarPreview();
    }

    /// <summary>
    /// Regra inteligente de grade: garante que elementos de tamanho (mesmo digitados como texto livre, ex: "42")
    /// e códigos de barras fiquem dinâmicos por tamanho (Referência + Ordem do Tamanho), mantendo a Referência original intacta.
    /// </summary>
    public void AplicarRegrasInteligentesDeGrade()
    {
        if (PadronizarModeloParaGrade(Modelo, ProdutoAtual, TamanhosParaTesteDisponiveis))
        {
            ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
        }
    }

    public static bool PadronizarModeloParaGrade(
        ModeloEtiquetaDesign? modelo,
        Produto? produtoReferencia = null,
        IEnumerable<string>? tamanhosExtras = null)
    {
        if (modelo?.Elementos == null) return false;

        var refProduto = produtoReferencia?.Referencia?.Trim();
        var tamanhosGrade = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (tamanhosExtras != null)
        {
            foreach (var t in tamanhosExtras)
            {
                if (!string.IsNullOrWhiteSpace(t) && !string.Equals(t, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
                    tamanhosGrade.Add(t.Trim());
            }
        }

        if (produtoReferencia?.Tamanhos != null)
        {
            foreach (var t in produtoReferencia.Tamanhos)
            {
                if (!string.IsNullOrWhiteSpace(t.Tamanho) && !string.Equals(t.Tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
                    tamanhosGrade.Add(t.Tamanho.Trim());
            }
        }

        var tamanhosPadraoVestuario = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PP", "P", "M", "G", "GG", "XG", "XXG", "EXG", "EG", "EGG", "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "G9", "G10", "U", "UN",
            "34", "36", "38", "40", "42", "44", "46", "48", "50", "52", "54", "56", "58", "60"
        };

        bool alterou = false;

        foreach (var el in modelo.Elementos)
        {
            if (el.Tipo == TipoElemento.Texto)
            {
                var txt = el.Texto?.Trim() ?? "";

                if (el.Variavel == VariavelEtiqueta.Nenhuma && !string.IsNullOrEmpty(txt))
                {
                    // Se o texto livre é exatamente a referência do produto, vincula à variável Referência (mantendo-a fixa no cadastro)
                    if (!string.IsNullOrEmpty(refProduto) && string.Equals(txt, refProduto, StringComparison.OrdinalIgnoreCase))
                    {
                        el.Variavel = VariavelEtiqueta.Referencia;
                        el.Texto = "{Ref}";
                        alterou = true;
                    }
                    // Se o texto livre é um tamanho da grade do produto (ex: digitou "42" na etiqueta da Bermuda Cargo)
                    else if (tamanhosGrade.Contains(txt) || (el.TamanhoFonte >= 16.0 && tamanhosPadraoVestuario.Contains(txt)))
                    {
                        el.Variavel = VariavelEtiqueta.Tamanho;
                        el.Texto = "{Tamanho}";
                        if (string.IsNullOrWhiteSpace(el.Nome) || el.Nome.StartsWith("Texto", StringComparison.OrdinalIgnoreCase))
                        {
                            el.Nome = "Tamanho";
                        }
                        alterou = true;
                    }
                    // Se é um número de 3 a 5 dígitos ao lado de "REF" (ex: "1402" salvo num modelo de outro produto) e não é tamanho
                    else if (txt.Length is >= 3 and <= 6 && txt.All(char.IsLetterOrDigit) && !tamanhosPadraoVestuario.Contains(txt) &&
                             modelo.Elementos.Any(o => o != el && o.Tipo == TipoElemento.Texto &&
                                 (o.Texto?.Contains("REF", StringComparison.OrdinalIgnoreCase) == true) &&
                                 Math.Abs(o.Y - el.Y) < 6.0))
                    {
                        el.Variavel = VariavelEtiqueta.Referencia;
                        el.Texto = "{Ref}";
                        alterou = true;
                    }
                    // Se o texto na base é o nome fixo do produto de origem, vincula ao NomeProduto
                    else if (produtoReferencia != null &&
                             !string.IsNullOrWhiteSpace(produtoReferencia.Nome) &&
                             string.Equals(txt, produtoReferencia.Nome.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        el.Variavel = VariavelEtiqueta.NomeProduto;
                        el.Texto = "{Nome}";
                        alterou = true;
                    }
                }

                // Se é o elemento de Tamanho e a caixa ultrapassava a folha (ex: Largura 74.7 numa folha de 35mm), centraliza na folha
                if (el.Variavel == VariavelEtiqueta.Tamanho && (el.Largura > modelo.LarguraMm || el.X + el.Largura > modelo.LarguraMm + 2.0))
                {
                    el.X = 2.0;
                    el.Largura = Math.Max(10.0, modelo.LarguraMm - 4.0);
                    el.Alinhamento = TextAlignment.Center;
                    alterou = true;
                }
            }
            else if (el.Tipo == TipoElemento.CodigoBarras && el.TipoCodigo != TipoSimboloCodigo.QrCode)
            {
                // Garante que o código de barras dependa de Referência + Tamanho quando não for um código externo fixo
                if (el.Variavel == VariavelEtiqueta.Nenhuma)
                {
                    el.Variavel = VariavelEtiqueta.CodigoBarras;
                    el.ValorCodigo = null;
                    alterou = true;
                }

                if (el.TipoCodigo == TipoSimboloCodigo.Ean13 && (string.IsNullOrEmpty(refProduto) || refProduto.Length < 12))
                {
                    el.TipoCodigo = TipoSimboloCodigo.Code128;
                    alterou = true;
                }
            }
        }

        return alterou;
    }

    public void AtualizarPreview()
    {
        NotificarAvisosDeTamanho();
        try
        {
            var tam = !string.IsNullOrWhiteSpace(TamanhoParaTeste) && TamanhoParaTeste != "Sem tamanho"
                ? TamanhoParaTeste
                : null;
            PreviewImagem = EditorDesenhoService.RenderizarBitmap(Modelo, ProdutoAtual, tam, NomeLoja, escalaDpi: 2.5);
            ZplCodigo = EditorDesenhoService.GerarZpl(Modelo, ProdutoAtual, NomeLoja, 1, tam);
        }
        catch (Exception exLog) { NexusLS.BarTender.App.Log($"DesignerViewModel #1: {exLog.Message}"); }
    }

    [RelayCommand]
    public void SelecionarElemento(ElementoDesign? el)
    {
        foreach (var item in Modelo.Elementos)
        {
            item.IsSelected = (item == el);
        }
        ElementoSelecionado = el;
    }

    public void NotificarCamadasAlteradas()
    {
        OnPropertyChanged(nameof(CamadasOrdenadas));
        OnPropertyChanged(nameof(TotalCamadas));
    }

    [RelayCommand]
    public void AlternarFerramentaMao()
    {
        FerramentaMaoAtiva = !FerramentaMaoAtiva;
        if (FerramentaMaoAtiva)
        {
            DesmarcarSelecao();
        }
    }

    [RelayCommand]
    public void MudarAbaInspetor(string aba)
    {
        AbaInspetorAtiva = aba;
    }

    /// <summary>Os elementos que Copiar/Recortar/Duplicar usam: todos os marcados (na ordem das camadas) ou, sem marcação, o principal.</summary>
    private List<ElementoDesign> AlvosDaSelecao()
    {
        var alvos = Modelo.Elementos.Where(e => e.IsSelected).ToList();
        if (alvos.Count == 0 && ElementoSelecionado != null) alvos.Add(ElementoSelecionado);
        return alvos;
    }

    /// <summary>Marca exatamente estes elementos (o último vira o principal).</summary>
    private void SelecionarVarios(IReadOnlyList<ElementoDesign> elementos)
    {
        foreach (var item in Modelo.Elementos) item.IsSelected = elementos.Contains(item);
        ElementoSelecionado = elementos.Count > 0 ? elementos[^1] : null;
    }

    [RelayCommand]
    public void Copiar()
    {
        var alvos = AlvosDaSelecao();
        if (alvos.Count == 0) return;
        _areaTransferencia.Clear();
        foreach (var e in alvos) _areaTransferencia.Add(e.Clonar());
    }

    [RelayCommand]
    public void Recortar()
    {
        var alvos = AlvosDaSelecao();
        if (alvos.Count == 0) return;
        RegistrarSnapshot();
        _areaTransferencia.Clear();
        foreach (var e in alvos) _areaTransferencia.Add(e.Clonar());
        foreach (var e in alvos) Modelo.Elementos.Remove(e);
        ElementoSelecionado = null;
        NotificarCamadasAlteradas();
        AtualizarPreview();
    }

    [RelayCommand]
    public void Colar()
    {
        if (_areaTransferencia.Count == 0) return;
        RegistrarSnapshot();
        var colados = new List<ElementoDesign>();
        foreach (var item in _areaTransferencia)
        {
            var novo = item.Clonar();
            novo.X = Math.Max(0, Math.Min(Modelo.LarguraMm - novo.Largura, novo.X + 2.0));
            novo.Y = Math.Max(0, Math.Min(Modelo.AlturaMm - novo.Altura, novo.Y + 2.0));
            novo.Nome = novo.Nome.Replace(" (Cópia)", "") + " (Colado)";
            Modelo.Elementos.Add(novo);
            colados.Add(novo);
        }
        NotificarCamadasAlteradas();
        SelecionarVarios(colados);
        AtualizarPreview();
    }

    [RelayCommand]
    public void SelecionarTudo()
    {
        var primeiro = Modelo.Elementos.FirstOrDefault(e => e.Visivel && !e.Bloqueado);
        foreach (var item in Modelo.Elementos)
        {
            item.IsSelected = (item.Visivel && !item.Bloqueado);
        }
        ElementoSelecionado = primeiro;
    }

    // =========================================================================
    // PRESETS & GALERIA
    // =========================================================================
    [RelayCommand]
    public void AplicarPreset(string? nomePreset)
    {
        nomePreset ??= PresetSelecionado;
        var def = CatalogoModelos.FirstOrDefault(p => p.Nome == nomePreset);
        if (def != null)
        {
            Modelo = def.Fabrica();
        }
        else
        {
            Modelo = ModeloEtiquetaDesign.CriarPadrao50x30();
        }

        InscreverEventosModelo();
        ElementoSelecionado = null;
        AtualizarPreview();
    }

    [RelayCommand]
    public void AbrirGaleriaModelos()
    {
        AbrirNovoDocumento();
    }

    [RelayCommand]
    public void FecharGaleriaModelos()
    {
        NovoDocumentoAberto = false;
        GaleriaModelosAberta = false;
    }

    [RelayCommand]
    public void FiltrarCategoriaGaleria(string categoria)
    {
        FiltrarCategoriaNovoDoc(categoria);
    }

    [RelayCommand]
    public void AplicarPredefinicaoGaleria(PredefinicaoEtiqueta predefinicao)
    {
        SelecionarPresetNovoDoc(predefinicao);
        ConfirmarCriarNovoDocumento();
    }

    [RelayCommand]
    public void ZoomMais()
    {
        if (ZoomNivel < 4.0) ZoomNivel = Math.Round(ZoomNivel + 0.25, 2);
    }

    [RelayCommand]
    public void ZoomMenos()
    {
        if (ZoomNivel > 0.5) ZoomNivel = Math.Round(ZoomNivel - 0.25, 2);
    }

    [RelayCommand]
    public void ZoomPadrao()
    {
        ZoomNivel = 2.0;
    }

    [RelayCommand]
    public void ZoomAjustar()
    {
        var maiorDimensao = Math.Max(Modelo.LarguraMm, Modelo.AlturaMm);
        if (maiorDimensao <= 40) ZoomNivel = 3.0;
        else if (maiorDimensao <= 70) ZoomNivel = 2.0;
        else if (maiorDimensao <= 120) ZoomNivel = 1.5;
        else ZoomNivel = 1.0;
    }
}
