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
    private string _nomeLoja = "NEXUS LS";

    [ObservableProperty]
    private Produto? _produtoAtual;

    [ObservableProperty]
    private string _presetSelecionado = "Padrão Gôndola Varejo (50 × 30 mm)";

    [ObservableProperty]
    private bool _galeriaModelosAberta = false;

    [ObservableProperty]
    private string _filtroCategoriaGaleria = "Todos";

    [ObservableProperty]
    private PredefinicaoEtiqueta? _predefinicaoSelecionadaNaGaleria;

    public ObservableCollection<PredefinicaoEtiqueta> CatalogoModelos { get; } =
        new(PredefinicaoEtiqueta.ObterCatalogoCompleto());

    public ObservableCollection<string> PresetsDisponiveis { get; } = new();

    public ObservableCollection<string> CategoriasGaleria { get; } = new()
    {
        "Todos",
        "Varejo & Gôndola",
        "Vestuário & Moda",
        "Promoções & Ofertas",
        "Joias & Óculos",
        "Logística & Caixas",
        "Folhas A4 (Laser / Jato de Tinta)"
    };

    public IEnumerable<PredefinicaoEtiqueta> ModelosFiltradosNaGaleria =>
        FiltroCategoriaGaleria == "Todos"
            ? CatalogoModelos
            : CatalogoModelos.Where(m => m.Categoria == FiltroCategoriaGaleria);

    public IReadOnlyList<TipoSimboloCodigo> TiposCodigoDisponiveis { get; } =
        Enum.GetValues<TipoSimboloCodigo>();

    public IReadOnlyList<VariavelEtiqueta> VariaveisDisponiveis { get; } =
        Enum.GetValues<VariavelEtiqueta>();

    public IReadOnlyList<TextAlignment> AlinhamentosDisponiveis { get; } =
        Enum.GetValues<TextAlignment>();

    public DesignerViewModel()
    {
        foreach (var m in CatalogoModelos)
        {
            PresetsDisponiveis.Add(m.Nome);
        }

        _modelo = ModeloEtiquetaDesign.CriarPadrao50x30();
        InscreverEventosModelo();
        AtualizarPreview();
    }

    private void InscreverEventosModelo()
    {
        Modelo.PropertyChanged += (_, _) => AtualizarPreview();
        Modelo.Elementos.CollectionChanged += (_, _) =>
        {
            InscreverElementos();
            AtualizarPreview();
        };
        InscreverElementos();
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
            AtualizarPreview();
        }
    }

    public void DefinirProduto(Produto? p, string? loja = null)
    {
        ProdutoAtual = p;
        if (!string.IsNullOrWhiteSpace(loja))
        {
            NomeLoja = loja;
        }
        AtualizarPreview();
    }

    public void AtualizarPreview()
    {
        try
        {
            PreviewImagem = EditorDesenhoService.RenderizarBitmap(Modelo, ProdutoAtual, null, NomeLoja, escalaDpi: 2.5);
            ZplCodigo = EditorDesenhoService.GerarZpl(Modelo, ProdutoAtual, NomeLoja);
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

    [RelayCommand]
    public void DesmarcarSelecao()
    {
        SelecionarElemento(null);
    }

    [RelayCommand]
    public void AdicionarTexto()
    {
        var el = new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            Texto = "Novo Texto",
            X = 2.0,
            Y = Math.Min(Modelo.AlturaMm - 6, 2.0),
            Largura = Math.Min(Modelo.LarguraMm - 4, 30.0),
            Altura = 5.0,
            TamanhoFonte = 9.0
        };
        Modelo.Elementos.Add(el);
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
                var bytes = File.ReadAllBytes(ofd.FileName);
                var base64 = Convert.ToBase64String(bytes);

                var el = new ElementoDesign
                {
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
        var el = new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            X = 2.0,
            Y = Math.Max(2.0, Modelo.AlturaMm - 10.0),
            Largura = Math.Min(Modelo.LarguraMm - 4, 40.0),
            Altura = 8.0,
            MostrarTextoLegivel = true
        };
        Modelo.Elementos.Add(el);
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void AdicionarQrCode()
    {
        var el = new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            TipoCodigo = TipoSimboloCodigo.QrCode,
            Variavel = VariavelEtiqueta.CodigoBarras,
            X = Math.Max(2.0, Modelo.LarguraMm - 14.0),
            Y = Math.Max(2.0, Modelo.AlturaMm - 14.0),
            Largura = 12.0,
            Altura = 12.0,
            MostrarTextoLegivel = false
        };
        Modelo.Elementos.Add(el);
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void AdicionarRetangulo()
    {
        var el = new ElementoDesign
        {
            Tipo = TipoElemento.Retangulo,
            X = 2.0,
            Y = 2.0,
            Largura = Modelo.LarguraMm - 4.0,
            Altura = Modelo.AlturaMm - 4.0,
            EspessuraBorda = 1.0,
            Preenchido = false
        };
        Modelo.Elementos.Add(el);
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void AdicionarLinha()
    {
        var el = new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 10.0,
            Largura = Modelo.LarguraMm - 4.0,
            Altura = 1.0,
            EspessuraBorda = 0.8
        };
        Modelo.Elementos.Add(el);
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void ExcluirSelecionado()
    {
        if (ElementoSelecionado == null) return;
        Modelo.Elementos.Remove(ElementoSelecionado);
        ElementoSelecionado = null;
        AtualizarPreview();
    }

    [RelayCommand]
    public void DuplicarSelecionado()
    {
        if (ElementoSelecionado == null) return;
        var clone = ElementoSelecionado.Clonar();
        Modelo.Elementos.Add(clone);
        SelecionarElemento(clone);
    }

    // =========================================================================
    // COMANDOS DE ALINHAMENTO
    // =========================================================================
    [RelayCommand]
    public void CentralizarHorizontalmente()
    {
        if (ElementoSelecionado == null) return;
        ElementoSelecionado.X = Math.Max(0, Math.Round((Modelo.LarguraMm - ElementoSelecionado.Largura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void CentralizarVerticalmente()
    {
        if (ElementoSelecionado == null) return;
        ElementoSelecionado.Y = Math.Max(0, Math.Round((Modelo.AlturaMm - ElementoSelecionado.Altura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharEsquerda()
    {
        if (ElementoSelecionado == null) return;
        ElementoSelecionado.X = 2.0;
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharTopo()
    {
        if (ElementoSelecionado == null) return;
        ElementoSelecionado.Y = 2.0;
        AtualizarPreview();
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
        GaleriaModelosAberta = true;
    }

    [RelayCommand]
    public void FecharGaleriaModelos()
    {
        GaleriaModelosAberta = false;
    }

    [RelayCommand]
    public void FiltrarCategoriaGaleria(string categoria)
    {
        FiltroCategoriaGaleria = categoria;
        OnPropertyChanged(nameof(ModelosFiltradosNaGaleria));
    }

    [RelayCommand]
    public void AplicarPredefinicaoGaleria(PredefinicaoEtiqueta predefinicao)
    {
        if (predefinicao == null) return;
        PresetSelecionado = predefinicao.Nome;
        Modelo = predefinicao.Fabrica();
        InscreverEventosModelo();
        ElementoSelecionado = null;
        AtualizarPreview();
        GaleriaModelosAberta = false;
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
}
