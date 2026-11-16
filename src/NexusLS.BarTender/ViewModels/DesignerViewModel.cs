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
        "Sem tamanho", "P", "M", "G", "GG"
    };

    partial void OnTamanhoParaTesteChanged(string value) => AtualizarPreview();

    partial void OnProdutoAtualChanged(Produto? value)
    {
        TamanhosParaTesteDisponiveis.Clear();
        if (value?.Tamanhos != null && value.Tamanhos.Count > 0)
        {
            foreach (var t in value.Tamanhos)
            {
                if (!string.IsNullOrWhiteSpace(t.Tamanho) && !TamanhosParaTesteDisponiveis.Contains(t.Tamanho))
                {
                    TamanhosParaTesteDisponiveis.Add(t.Tamanho);
                }
            }
        }

        if (TamanhosParaTesteDisponiveis.Count == 0)
        {
            TamanhosParaTesteDisponiveis.Add("Sem tamanho");
            TamanhosParaTesteDisponiveis.Add("P");
            TamanhosParaTesteDisponiveis.Add("M");
            TamanhosParaTesteDisponiveis.Add("G");
            TamanhosParaTesteDisponiveis.Add("GG");
        }

        if (!TamanhosParaTesteDisponiveis.Contains(TamanhoParaTeste))
        {
            TamanhoParaTeste = TamanhosParaTesteDisponiveis.Contains("M") ? "M" : TamanhosParaTesteDisponiveis.First();
        }
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
                    InscreverEventosModelo();
                }
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

    public void AtualizarPreview()
    {
        try
        {
            var tam = !string.IsNullOrWhiteSpace(TamanhoParaTeste) && TamanhoParaTeste != "Sem tamanho"
                ? TamanhoParaTeste
                : null;
            PreviewImagem = EditorDesenhoService.RenderizarBitmap(Modelo, ProdutoAtual, tam, NomeLoja, escalaDpi: 2.5);
            ZplCodigo = EditorDesenhoService.GerarZpl(Modelo, ProdutoAtual, NomeLoja, 1, tam);
        }
        catch { }
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

    [RelayCommand]
    public void Copiar()
    {
        if (ElementoSelecionado == null) return;
        _areaTransferencia.Clear();
        _areaTransferencia.Add(ElementoSelecionado.Clonar());
    }

    [RelayCommand]
    public void Recortar()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        _areaTransferencia.Clear();
        _areaTransferencia.Add(ElementoSelecionado.Clonar());
        Modelo.Elementos.Remove(ElementoSelecionado);
        ElementoSelecionado = null;
        NotificarCamadasAlteradas();
        AtualizarPreview();
    }

    [RelayCommand]
    public void Colar()
    {
        if (_areaTransferencia.Count == 0) return;
        RegistrarSnapshot();
        ElementoDesign? ultimoColado = null;
        foreach (var item in _areaTransferencia)
        {
            var novo = item.Clonar();
            novo.X = Math.Min(Modelo.LarguraMm - novo.Largura, novo.X + 2.0);
            novo.Y = Math.Min(Modelo.AlturaMm - novo.Altura, novo.Y + 2.0);
            novo.Nome = novo.Nome.Replace(" (Cópia)", "") + " (Colado)";
            Modelo.Elementos.Add(novo);
            ultimoColado = novo;
        }
        NotificarCamadasAlteradas();
        if (ultimoColado != null)
        {
            SelecionarElemento(ultimoColado);
        }
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
