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
    // SISTEMA DE HISTÓRICO UNDO / REDO (CTRL+Z E CTRL+SHIFT+Z / CTRL+Y)
    // =========================================================================
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private bool _isRestoringState = false;

    public void RegistrarSnapshot()
    {
        if (_isRestoringState || Modelo == null) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Modelo, ModeloEtiquetaDesign.JsonOpts);
            _undoStack.Push(json);
            if (_undoStack.Count > 60)
            {
                var list = _undoStack.ToList();
                _undoStack.Clear();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (_undoStack.Count < 50) _undoStack.Push(list[i]);
                }
            }
            _redoStack.Clear();
        }
        catch { }
    }

    [RelayCommand]
    public void Desfazer()
    {
        if (_undoStack.Count == 0 || Modelo == null) return;
        _isRestoringState = true;
        try
        {
            var estadoAtual = System.Text.Json.JsonSerializer.Serialize(Modelo, ModeloEtiquetaDesign.JsonOpts);
            _redoStack.Push(estadoAtual);

            var estadoAnterior = _undoStack.Pop();
            RestaurarModelo(estadoAnterior);
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    [RelayCommand]
    public void Refazer()
    {
        if (_redoStack.Count == 0 || Modelo == null) return;
        _isRestoringState = true;
        try
        {
            var estadoAtual = System.Text.Json.JsonSerializer.Serialize(Modelo, ModeloEtiquetaDesign.JsonOpts);
            _undoStack.Push(estadoAtual);

            var proximoEstado = _redoStack.Pop();
            RestaurarModelo(proximoEstado);
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    private void RestaurarModelo(string json)
    {
        try
        {
            var mod = System.Text.Json.JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, ModeloEtiquetaDesign.JsonOpts);
            if (mod != null)
            {
                Modelo = mod;
                InscreverEventosModelo();
                OnPropertyChanged(nameof(FolhaLarguraMm));
                OnPropertyChanged(nameof(FolhaAlturaMm));
                ElementoSelecionado = null;
                ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
                AtualizarPreview();
            }
        }
        catch { }
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

    public DesignerViewModel()
    {
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

    [RelayCommand]
    public void SalvarEtiquetaNoProduto()
    {
        if (ProdutoAtual == null)
        {
            MessageBox.Show("Nenhum produto selecionado no catálogo para cadastrar esta etiqueta.", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var ok = GerenciadorEtiquetasProdutoService.SalvarEtiquetaProduto(ProdutoAtual.Id, Modelo);
            if (ok)
            {
                ProdutoTemEtiquetaExclusiva = true;

                // Atualizar o item correspondente no catálogo sincronizado
                var itemCat = ListaProdutosCatalog.FirstOrDefault(i => i.Id == ProdutoAtual.Id);
                itemCat?.AtualizarInfoEtiqueta();

                MessageBox.Show(
                    $"Etiqueta cadastrada com sucesso para o produto:\n\n{ProdutoAtual.Nome}\n\nModelo: {Modelo.Nome} ({Modelo.LarguraMm:0.#} × {Modelo.AlturaMm:0.#} mm)",
                    "Etiqueta Vinculada com Sucesso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao salvar etiqueta do produto: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void RestaurarEtiquetaPadraoProduto()
    {
        if (ProdutoAtual == null) return;

        var res = MessageBox.Show(
            $"Deseja remover a etiqueta exclusiva e voltar a usar a etiqueta padrão para o produto:\n\n{ProdutoAtual.Nome}?",
            "Restaurar Padrão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            GerenciadorEtiquetasProdutoService.RemoverEtiquetaProduto(ProdutoAtual.Id);
            ProdutoTemEtiquetaExclusiva = false;

            var itemCat = ListaProdutosCatalog.FirstOrDefault(i => i.Id == ProdutoAtual.Id);
            itemCat?.AtualizarInfoEtiqueta();

            Modelo = ModeloEtiquetaDesign.CriarPadrao50x30();
            PresetSelecionado = Modelo.Nome;
            InscreverEventosModelo();
            AtualizarPreview();

            MessageBox.Show("O produto agora usará a etiqueta padrão do sistema.", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
        }
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

    [RelayCommand]
    public void Agrupar()
    {
        var selecionados = Modelo.Elementos.Where(e => e.IsSelected).ToList();
        if (selecionados.Count < 2 && ElementoSelecionado != null)
        {
            selecionados = new List<ElementoDesign> { ElementoSelecionado };
        }
        if (selecionados.Count > 0)
        {
            RegistrarSnapshot();
            var novoGrupoId = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var nomeGrupo = $"Grupo {novoGrupoId}";
            foreach (var el in selecionados)
            {
                el.GrupoId = novoGrupoId;
                el.NomeGrupo = nomeGrupo;
            }
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void Desagrupar()
    {
        var selecionados = Modelo.Elementos.Where(e => e.IsSelected).ToList();
        if (selecionados.Count == 0 && ElementoSelecionado != null)
        {
            selecionados = new List<ElementoDesign> { ElementoSelecionado };
        }
        if (selecionados.Count > 0)
        {
            RegistrarSnapshot();
            var grupoIds = selecionados.Where(e => !string.IsNullOrEmpty(e.GrupoId)).Select(e => e.GrupoId).Distinct().ToList();
            foreach (var el in Modelo.Elementos)
            {
                if (grupoIds.Contains(el.GrupoId))
                {
                    el.GrupoId = null;
                    el.NomeGrupo = null;
                }
            }
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void AlternarVisibilidadeCamada(ElementoDesign? el)
    {
        if (el == null) return;
        RegistrarSnapshot();
        el.Visivel = !el.Visivel;
        if (!el.Visivel && el.IsSelected)
        {
            DesmarcarSelecao();
        }
        NotificarCamadasAlteradas();
        AtualizarPreview();
        ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void AlternarBloqueioCamada(ElementoDesign? el)
    {
        if (el == null) return;
        RegistrarSnapshot();
        el.Bloqueado = !el.Bloqueado;
        if (el.Bloqueado && el.IsSelected)
        {
            DesmarcarSelecao();
        }
        NotificarCamadasAlteradas();
        AtualizarPreview();
        ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void SubirCamada(ElementoDesign? el)
    {
        el ??= ElementoSelecionado;
        if (el == null) return;
        var idx = Modelo.Elementos.IndexOf(el);
        if (idx >= 0 && idx < Modelo.Elementos.Count - 1)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx + 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
            ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    public void DescerCamada(ElementoDesign? el)
    {
        el ??= ElementoSelecionado;
        if (el == null) return;
        var idx = Modelo.Elementos.IndexOf(el);
        if (idx > 0)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx - 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
            ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    public void SalvarProjetoRapido()
    {
        if (!string.IsNullOrEmpty(CaminhoArquivoAtual) && File.Exists(CaminhoArquivoAtual))
        {
            try
            {
                Modelo.Salvar(CaminhoArquivoAtual);
                MessageBox.Show("Projeto salvo com sucesso!", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            catch { }
        }
        SalvarProjetoComo();
    }

    [RelayCommand]
    public void SalvarProjetoComo()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Salvar Modelo de Etiqueta (JSON)",
            Filter = "Modelo de Etiqueta (*.json)|*.json",
            FileName = $"{Modelo.Nome.ToLowerInvariant().Replace(" ", "_").Replace("/", "_").Replace("&", "e")}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                Modelo.Salvar(sfd.FileName);
                CaminhoArquivoAtual = sfd.FileName;
                MessageBox.Show("Modelo de etiqueta salvo com sucesso!", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar modelo: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void DesmarcarSelecao()
    {
        SelecionarElemento(null);
    }

    [RelayCommand]
    public void AdicionarTexto()
    {
        RegistrarSnapshot();
        var el = new ElementoDesign
        {
            Nome = $"Texto {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Texto) + 1}",
            Tipo = TipoElemento.Texto,
            Texto = "Novo Texto",
            X = 2.0,
            Y = Math.Min(Modelo.AlturaMm - 6, 2.0),
            Largura = Math.Min(Modelo.LarguraMm - 4, 30.0),
            Altura = 5.0,
            TamanhoFonte = 9.0
        };
        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void AdicionarImagem()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Selecionar Imagem / Logotipo para Etiqueta",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Todos os Arquivos (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                RegistrarSnapshot();
                var bytes = File.ReadAllBytes(ofd.FileName);
                var base64 = Convert.ToBase64String(bytes);

                var el = new ElementoDesign
                {
                    Nome = $"Imagem {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Imagem) + 1}",
                    Tipo = TipoElemento.Imagem,
                    CaminhoImagem = ofd.FileName,
                    ImagemBase64 = base64,
                    X = 2.0,
                    Y = 2.0,
                    Largura = 12.0,
                    Altura = 12.0,
                    ManterProporcao = true
                };

                Modelo.Elementos.Add(el);
                NotificarCamadasAlteradas();
                SelecionarElemento(el);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar a imagem: {ex.Message}", "Editor de Etiquetas", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void TrocarImagemElemento()
    {
        if (ElementoSelecionado == null || ElementoSelecionado.Tipo != TipoElemento.Imagem) return;

        var ofd = new OpenFileDialog
        {
            Title = "Trocar Imagem / Logotipo",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                RegistrarSnapshot();
                var bytes = File.ReadAllBytes(ofd.FileName);
                ElementoSelecionado.CaminhoImagem = ofd.FileName;
                ElementoSelecionado.ImagemBase64 = Convert.ToBase64String(bytes);
                AtualizarPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar a imagem: {ex.Message}", "Editor de Etiquetas", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void AdicionarCodigoBarras()
    {
        AbrirModalCodigo("1D");
    }

    [RelayCommand]
    public void AdicionarQrCode()
    {
        AbrirModalCodigo("2D");
    }

    [RelayCommand]
    public void AbrirModalCodigo(string? tipo = null)
    {
        bool is2D = string.Equals(tipo, "2D", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tipo, "QR", StringComparison.OrdinalIgnoreCase);

        if (is2D)
        {
            ModalCodigoTitulo = "Gerador de QR Code (2D)";
            ModalCodigoTipo = TipoSimboloCodigo.QrCode;
            ModalCodigoEhQrCode = true;
            ModalCodigoMostrarLegivel = false;
            ModalCodigoLarguraMm = 15.0;
            ModalCodigoAlturaMm = 15.0;
            ModalCodigoOrigemProduto = true;
            ModalCodigoVariavel = VariavelEtiqueta.CodigoBarras;
            if (string.IsNullOrWhiteSpace(ModalCodigoValorFixo) || ModalCodigoValorFixo == "7891234567890")
            {
                ModalCodigoValorFixo = "https://loja.com.br";
            }
        }
        else
        {
            ModalCodigoTitulo = "Gerador de Código de Barras (1D)";
            ModalCodigoTipo = TipoSimboloCodigo.Code128;
            ModalCodigoEhQrCode = false;
            ModalCodigoMostrarLegivel = true;
            ModalCodigoLarguraMm = Math.Min(38.0, Math.Max(20.0, Modelo.LarguraMm - 4.0));
            ModalCodigoAlturaMm = 12.0;
            ModalCodigoOrigemProduto = true;
            ModalCodigoVariavel = VariavelEtiqueta.CodigoBarras;
            if (string.IsNullOrWhiteSpace(ModalCodigoValorFixo) || ModalCodigoValorFixo.StartsWith("http"))
            {
                ModalCodigoValorFixo = "7891234567890";
            }
        }

        AtualizarModalCodigoPreview();
        ModalCodigoAberto = true;
    }

    [RelayCommand]
    public void FecharModalCodigo()
    {
        ModalCodigoAberto = false;
    }

    [RelayCommand]
    public void ConfirmarInserirCodigo()
    {
        RegistrarSnapshot();

        var ehQr = ModalCodigoTipo == TipoSimboloCodigo.QrCode;
        int totalCodigos = Modelo.Elementos.Count(e => e.Tipo == TipoElemento.CodigoBarras) + 1;

        var el = new ElementoDesign
        {
            Nome = ehQr ? $"QR Code {totalCodigos}" : $"Código 1D {totalCodigos}",
            Tipo = TipoElemento.CodigoBarras,
            TipoCodigo = ModalCodigoTipo,
            Variavel = ModalCodigoOrigemProduto ? ModalCodigoVariavel : VariavelEtiqueta.Nenhuma,
            ValorCodigo = !ModalCodigoOrigemProduto ? ModalCodigoValorFixo : null,
            MostrarTextoLegivel = !ehQr && ModalCodigoMostrarLegivel,
            Largura = Math.Max(5.0, ModalCodigoLarguraMm),
            Altura = Math.Max(5.0, ModalCodigoAlturaMm),
            X = Math.Max(1.0, Math.Round((Modelo.LarguraMm - ModalCodigoLarguraMm) / 2.0, 1)),
            Y = Math.Max(1.0, Math.Round((Modelo.AlturaMm - ModalCodigoAlturaMm) / 2.0, 1))
        };

        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
        AtualizarPreview();

        ModalCodigoAberto = false;
    }

    [RelayCommand]
    public void DefinirOrigemCodigo(string origem)
    {
        ModalCodigoOrigemProduto = string.Equals(origem, "Produto", StringComparison.OrdinalIgnoreCase);
        AtualizarModalCodigoPreview();
    }

    [RelayCommand]
    public void DefinirTipoSimbolo(string tipo)
    {
        if (Enum.TryParse<TipoSimboloCodigo>(tipo, true, out var t))
        {
            ModalCodigoTipo = t;
        }
    }

    [RelayCommand]
    public void GerarEan13Nexus()
    {
        ModalCodigoTipo = TipoSimboloCodigo.Ean13;
        ModalCodigoEhQrCode = false;
        ModalCodigoOrigemProduto = false;

        long seq = 1;
        if (ListaProdutosCatalog.Count > 0)
        {
            var codigos = ListaProdutosCatalog
                .Select(p => p.Produto.CodigoBarras)
                .Where(c => c != null && c.StartsWith("2") && c.Length == 13)
                .Select(CodigoBarrasInterno.SequenciaDe)
                .Where(s => s != null)
                .Select(s => s!.Value);

            if (codigos.Any())
            {
                seq = codigos.Max() + 1;
            }
            else
            {
                seq = ListaProdutosCatalog.Count + 1;
            }
        }

        ModalCodigoValorFixo = CodigoBarrasInterno.Montar(seq);
        AtualizarModalCodigoPreview();
    }

    public void AtualizarModalCodigoPreview()
    {
        try
        {
            ModalCodigoEhQrCode = ModalCodigoTipo == TipoSimboloCodigo.QrCode;

            string conteudo;
            if (ModalCodigoOrigemProduto)
            {
                conteudo = ModalCodigoVariavel switch
                {
                    VariavelEtiqueta.CodigoBarras => ProdutoAtual?.CodigoBarras ?? "7891234567890",
                    VariavelEtiqueta.CodigoInterno => ProdutoAtual?.CodigoInterno ?? "INT-0001",
                    VariavelEtiqueta.Referencia => ProdutoAtual?.Referencia ?? "REF-8606",
                    VariavelEtiqueta.NomeProduto => ProdutoAtual?.Nome ?? "CAMISA DRYFIT",
                    VariavelEtiqueta.PrecoVarejo => ProdutoAtual != null ? (ProdutoAtual.PrecoVendaCentavos / 100.0).ToString("F2") : "49.90",
                    VariavelEtiqueta.NomeLoja => !string.IsNullOrWhiteSpace(NomeLoja) ? NomeLoja : "NEXUS LS",
                    _ => ProdutoAtual?.CodigoBarras ?? "7891234567890"
                };

                if (string.IsNullOrWhiteSpace(conteudo))
                {
                    conteudo = "7891234567890";
                }
            }
            else
            {
                conteudo = !string.IsNullOrWhiteSpace(ModalCodigoValorFixo)
                    ? ModalCodigoValorFixo.Trim()
                    : (ModalCodigoEhQrCode ? "https://nexusls.com.br" : "7891234567890");
            }

            ModalCodigoValorResolvido = conteudo;

            // Validações e avisos contextuais baseados nas regras do Nexus
            if (ModalCodigoTipo == TipoSimboloCodigo.Ean13)
            {
                var digitos = new string(conteudo.Where(char.IsDigit).ToArray());
                if (digitos.Length == 13 && digitos.StartsWith("2") && CodigoBarrasInterno.EhEan13Valido(digitos))
                {
                    ModalCodigoAviso = "Código EAN-13 Interno Nexus válido (prefixo 2 oficial da casa com Dígito Verificador Módulo 10). Aceito pelo leitor e PDV.";
                }
                else if (digitos.Length < 12)
                {
                    ModalCodigoAviso = "Dica EAN-13: Requer 12 ou 13 dígitos numéricos. Faltando dígitos, serão completados com zeros à esquerda e cálculo de checksum.";
                }
                else if (digitos.Length > 13)
                {
                    ModalCodigoAviso = "Dica EAN-13: Apenas os primeiros 12 dígitos mais o dígito verificador serão codificados.";
                }
                else
                {
                    ModalCodigoAviso = "Padrão EAN-13 válido (13 dígitos com checksum automático).";
                }
            }
            else if (ModalCodigoOrigemProduto && ModalCodigoVariavel == VariavelEtiqueta.Referencia)
            {
                ModalCodigoAviso = $"Regra Nexus para Referência '{conteudo}': em etiquetas com grade de tamanho, o código receberá o sufixo numérico do tamanho (P→{conteudo}1, M→{conteudo}2, G→{conteudo}3).";
            }
            else if (ModalCodigoTipo == TipoSimboloCodigo.Code39)
            {
                ModalCodigoAviso = "Padrão Code 39 aceita letras maiúsculas (A-Z), números (0-9) e símbolos (- . $ / + % espaço).";
            }
            else if (ModalCodigoTipo == TipoSimboloCodigo.QrCode)
            {
                ModalCodigoAviso = "Padrão QR Code 2D (Suporta URLs, chaves Pix, textos alfanuméricos e identificadores).";
            }
            else
            {
                ModalCodigoAviso = "Padrão Code 128 (Alta densidade, suporta caracteres alfanuméricos completos). Aceita referências e códigos de produto.";
            }

            int larguraPx = (int)Math.Max(80, Math.Min(600, ModalCodigoLarguraMm * 10.0));
            int alturaPx = (int)Math.Max(40, Math.Min(600, ModalCodigoAlturaMm * 10.0));

            ModalCodigoPreview = BarcodeGeneratorService.Gerar(conteudo, ModalCodigoTipo, larguraPx, alturaPx, margem: 1);
        }
        catch (Exception ex)
        {
            ModalCodigoAviso = $"Aviso de geração: {ex.Message}";
            ModalCodigoPreview = null;
        }
    }

    [RelayCommand]
    public void AdicionarRetangulo()
    {
        RegistrarSnapshot();
        var el = new ElementoDesign
        {
            Nome = $"Retângulo {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Retangulo) + 1}",
            Tipo = TipoElemento.Retangulo,
            X = 2.0,
            Y = 2.0,
            Largura = Modelo.LarguraMm - 4.0,
            Altura = Modelo.AlturaMm - 4.0,
            EspessuraBorda = 1.0,
            Preenchido = false
        };
        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void AdicionarLinha()
    {
        RegistrarSnapshot();
        var el = new ElementoDesign
        {
            Nome = $"Linha {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Linha) + 1}",
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 10.0,
            Largura = Modelo.LarguraMm - 4.0,
            Altura = 1.0,
            EspessuraBorda = 0.8
        };
        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void ExcluirSelecionado()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        Modelo.Elementos.Remove(ElementoSelecionado);
        ElementoSelecionado = null;
        NotificarCamadasAlteradas();
        AtualizarPreview();
    }

    [RelayCommand]
    public void DuplicarSelecionado()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        var clone = ElementoSelecionado.Clonar();
        clone.X = Math.Min(Modelo.LarguraMm - clone.Largura, clone.X + 2.0);
        clone.Y = Math.Min(Modelo.AlturaMm - clone.Altura, clone.Y + 2.0);
        Modelo.Elementos.Add(clone);
        NotificarCamadasAlteradas();
        SelecionarElemento(clone);
    }

    // =========================================================================
    // COMANDOS DE ALINHAMENTO (ESTILO CORELDRAW / VECTORCRAFT)
    // =========================================================================
    [RelayCommand]
    public void CentralizarNaPagina()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = Math.Max(0, Math.Round((Modelo.LarguraMm - ElementoSelecionado.Largura) / 2.0, 1));
        ElementoSelecionado.Y = Math.Max(0, Math.Round((Modelo.AlturaMm - ElementoSelecionado.Altura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void CentralizarHorizontalmente()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = Math.Max(0, Math.Round((Modelo.LarguraMm - ElementoSelecionado.Largura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void CentralizarVerticalmente()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.Y = Math.Max(0, Math.Round((Modelo.AlturaMm - ElementoSelecionado.Altura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharEsquerda()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = 2.0;
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharDireita()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = Math.Max(0, Modelo.LarguraMm - ElementoSelecionado.Largura - 2.0);
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharTopo()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.Y = 2.0;
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharBase()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.Y = Math.Max(0, Modelo.AlturaMm - ElementoSelecionado.Altura - 2.0);
        AtualizarPreview();
    }

    [RelayCommand]
    public void AvancarCamada()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx >= 0 && idx < Modelo.Elementos.Count - 1)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx + 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void RecuarCamada()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx > 0)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx - 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [ObservableProperty]
    private double _mouseXMm;

    [ObservableProperty]
    private double _mouseYMm;

    public void AtualizarCoordenadasMouse(double xMm, double yMm)
    {
        MouseXMm = Math.Round(Math.Clamp(xMm, 0, Modelo.LarguraMm), 1);
        MouseYMm = Math.Round(Math.Clamp(yMm, 0, Modelo.AlturaMm), 1);
    }

    [RelayCommand]
    public void TrazerParaFrente()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx >= 0 && idx < Modelo.Elementos.Count - 1)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, Modelo.Elementos.Count - 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void EnviarParaTras()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx > 0)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, 0);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void InserirVariavel(string nomeVariavel)
    {
        if (!Enum.TryParse<VariavelEtiqueta>(nomeVariavel, out var variavel))
        {
            variavel = VariavelEtiqueta.NomeProduto;
        }

        var (textoPadrao, largPadrao, altPadrao, fontePadrao, negrito) = variavel switch
        {
            VariavelEtiqueta.NomeProduto => ("{Nome}", Math.Min(Modelo.LarguraMm - 4, 46.0), 7.0, 9.5, true),
            VariavelEtiqueta.PrecoVarejo => ("R$ {Preco}", 28.0, 6.0, 13.0, true),
            VariavelEtiqueta.PrecoAtacado => ("ATACADO: R$ {PrecoAtacado}", 30.0, 4.0, 7.5, false),
            VariavelEtiqueta.Referencia => ("REF: {Ref}", 25.0, 3.5, 7.5, false),
            VariavelEtiqueta.Tamanho => ("TAM: {Tamanho}", 15.0, 4.5, 8.5, true),
            VariavelEtiqueta.CodigoBarras => ("{Codigo}", Math.Min(Modelo.LarguraMm - 4, 40.0), 8.0, 8.0, false),
            VariavelEtiqueta.CodigoInterno => ("CÓD: {CodigoInterno}", 25.0, 3.5, 7.5, false),
            VariavelEtiqueta.NomeLoja => ("NEXUS LS", Math.Min(Modelo.LarguraMm - 4, 46.0), 4.5, 8.5, true),
            _ => ("Novo Campo", 25.0, 5.0, 9.0, false)
        };

        var el = new ElementoDesign
        {
            Nome = $"{nomeVariavel} {Modelo.Elementos.Count + 1}",
            Tipo = variavel == VariavelEtiqueta.CodigoBarras ? TipoElemento.CodigoBarras : TipoElemento.Texto,
            Texto = textoPadrao,
            Variavel = variavel,
            X = 2.0,
            Y = Math.Max(2.0, Math.Round((Modelo.AlturaMm / 2 - altPadrao / 2) * 2) / 2.0),
            Largura = largPadrao,
            Altura = altPadrao,
            TamanhoFonte = fontePadrao,
            Negrito = negrito
        };

        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    // =========================================================================
    // HUB DE NOVO DOCUMENTO (ESTILO ILLUSTRATOR / CORELDRAW)
    // =========================================================================
    [RelayCommand]
    public void AbrirNovoDocumento()
    {
        NovoDocCategoriaFiltro = "Todos";
        OnPropertyChanged(nameof(ModelosFiltradosNovoDoc));

        if (NovoDocPredefinicaoSelecionada == null && CatalogoModelos.Count > 0)
        {
            SelecionarPresetNovoDoc(CatalogoModelos[0]);
        }
        else if (NovoDocPredefinicaoSelecionada != null)
        {
            SelecionarPresetNovoDoc(NovoDocPredefinicaoSelecionada);
        }

        NovoDocumentoAberto = true;
    }

    [RelayCommand]
    public void FecharNovoDocumento()
    {
        NovoDocumentoAberto = false;
    }

    [RelayCommand]
    public void FiltrarCategoriaNovoDoc(string categoria)
    {
        NovoDocCategoriaFiltro = categoria;
        OnPropertyChanged(nameof(ModelosFiltradosNovoDoc));
    }

    [RelayCommand]
    public void SelecionarPresetNovoDoc(PredefinicaoEtiqueta preset)
    {
        if (preset == null) return;
        NovoDocPredefinicaoSelecionada = preset;
        NovoDocNome = preset.Nome;
        NovoDocLargura = preset.LarguraMm;
        NovoDocAltura = preset.AlturaMm;
        NovoDocIsPaisagem = preset.LarguraMm >= preset.AlturaMm;
        NovoDocTipo = preset.TipoSugerido;
    }

    [RelayCommand]
    public void AlternarOrientacaoNovoDoc(string modo)
    {
        var querPaisagem = string.Equals(modo, "Paisagem", StringComparison.OrdinalIgnoreCase);

        if (querPaisagem && NovoDocLargura < NovoDocAltura)
        {
            var temp = NovoDocLargura;
            NovoDocLargura = NovoDocAltura;
            NovoDocAltura = temp;
        }
        else if (!querPaisagem && NovoDocAltura < NovoDocLargura)
        {
            var temp = NovoDocLargura;
            NovoDocLargura = NovoDocAltura;
            NovoDocAltura = temp;
        }

        NovoDocIsPaisagem = querPaisagem;
    }

    [RelayCommand]
    public void AlternarTipoNovoDoc(string tipo)
    {
        NovoDocTipo = tipo;
    }

    [RelayCommand]
    public void ConfirmarCriarNovoDocumento()
    {
        try
        {
            if (NovoDocLargura <= 0) NovoDocLargura = 50.0;
            if (NovoDocAltura <= 0) NovoDocAltura = 30.0;
            if (string.IsNullOrWhiteSpace(NovoDocNome)) NovoDocNome = "Nova Etiqueta";

            ModeloEtiquetaDesign novoModelo;

            if (NovoDocTipo == "EmBranco")
            {
                novoModelo = ModeloEtiquetaDesign.CriarEmBranco(NovoDocLargura, NovoDocAltura, NovoDocNome);
            }
            else if (NovoDocTipo == "Composicao")
            {
                if (NovoDocPredefinicaoSelecionada != null &&
                    NovoDocPredefinicaoSelecionada.TipoSugerido == "Composicao" &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.LarguraMm - NovoDocLargura) < 0.1 &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.AlturaMm - NovoDocAltura) < 0.1)
                {
                    novoModelo = NovoDocPredefinicaoSelecionada.Fabrica();
                    novoModelo.Nome = NovoDocNome;
                }
                else
                {
                    novoModelo = ModeloEtiquetaDesign.CriarEtiquetaComposicaoGenerica(NovoDocLargura, NovoDocAltura, NovoDocNome);
                }
            }
            else
            {
                // Normal
                if (NovoDocPredefinicaoSelecionada != null &&
                    NovoDocPredefinicaoSelecionada.TipoSugerido == "Normal" &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.LarguraMm - NovoDocLargura) < 0.1 &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.AlturaMm - NovoDocAltura) < 0.1)
                {
                    novoModelo = NovoDocPredefinicaoSelecionada.Fabrica();
                    novoModelo.Nome = NovoDocNome;
                }
                else
                {
                    novoModelo = ModeloEtiquetaDesign.CriarEtiquetaNormalGenerica(NovoDocLargura, NovoDocAltura, NovoDocNome);
                }
            }

            Modelo = novoModelo;
            PresetSelecionado = Modelo.Nome;
            InscreverEventosModelo();
            ElementoSelecionado = null;
            AtualizarPreview();

            NovoDocumentoAberto = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao criar novo documento: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    // =========================================================================
    // IMPRESSÃO DE TESTE DIRETA
    // =========================================================================
    [RelayCommand]
    public void ImprimirTeste()
    {
        try
        {
            var prod = ProdutoAtual ?? new Produto
            {
                Nome = "PRODUTO DE TESTE NEXUS",
                PrecoVendaCentavos = 11990,
                PrecoAtacadoCentavos = 7990,
                Referencia = "REF-2026",
                CodigoBarras = "7891234567890",
                CodigoInterno = "0001"
            };

            var itemTeste = new ItemFila(prod, 1, "G");

            var sucesso = ImpressaoService.ImprimirDesign(new[] { itemTeste }, Modelo, NomeLoja);
            if (sucesso)
            {
                MessageBox.Show("Impressão enviada com sucesso!", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao imprimir teste: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void SalvarModelo()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Salvar Modelo de Etiqueta (JSON)",
            Filter = "Modelo de Etiqueta (*.json)|*.json",
            FileName = $"{Modelo.Nome.ToLowerInvariant().Replace(" ", "_").Replace("/", "_").Replace("&", "e")}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                Modelo.Salvar(sfd.FileName);
                MessageBox.Show("Modelo de etiqueta salvo com sucesso!", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar modelo: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void CarregarModelo()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Abrir Modelo de Etiqueta (JSON)",
            Filter = "Modelo de Etiqueta (*.json)|*.json"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                Modelo = ModeloEtiquetaDesign.Carregar(ofd.FileName);
                InscreverEventosModelo();
                ElementoSelecionado = null;
                AtualizarPreview();
                MessageBox.Show("Modelo carregado com sucesso!", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar modelo: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
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
