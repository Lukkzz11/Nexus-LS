using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.BarTender.ViewModels;

namespace NexusLS.BarTender.Views.Designer;

public partial class DesignerView : UserControl
{
    private Point _pontoInicialClique;
    private bool _estaArrastando;
    private bool _estaRedimensionando;
    private ElementoDesign? _elementoSendoArrastado;
    private double _origemXMm;
    private double _origemYMm;
    private AdornerLayer? _camadaAdorner;
    private ElementoAdorner? _adornerAtual;

    // Controle de Navegação Pan (Mãozinha estilo CAD / Corel)
    private bool _estaEmPan;
    private Point _pontoInicialPan;
    private double _offsetInicialPanH;
    private double _offsetInicialPanV;

    public DesignerView()
    {
        InitializeComponent();
        DataContextChanged += DesignerView_DataContextChanged;
        Loaded += DesignerView_Loaded;
        Unloaded += DesignerView_Unloaded;
    }

    private void DesignerView_Loaded(object sender, RoutedEventArgs e)
    {
        _camadaAdorner = AdornerLayer.GetAdornerLayer(EditorCanvas);
        var win = Window.GetWindow(this);
        if (win != null)
        {
            win.PreviewKeyDown -= Window_PreviewKeyDown;
            win.PreviewKeyDown += Window_PreviewKeyDown;
        }
        ReconstruirCanvas();
    }

    private void DesignerView_Unloaded(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this);
        if (win != null)
        {
            win.PreviewKeyDown -= Window_PreviewKeyDown;
        }
    }

    private void DesignerView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DesignerViewModel vmAntigo)
        {
            vmAntigo.PropertyChanged -= Vm_PropertyChanged;
            vmAntigo.ElementoPropriedadeAlterada -= Vm_ElementoPropriedadeAlterada;
        }

        if (e.NewValue is DesignerViewModel vmNovo)
        {
            vmNovo.PropertyChanged += Vm_PropertyChanged;
            vmNovo.ElementoPropriedadeAlterada += Vm_ElementoPropriedadeAlterada;
            ReconstruirCanvas();
        }
    }

    private void Vm_ElementoPropriedadeAlterada(object? sender, EventArgs e)
    {
        if (!_estaArrastando && !_estaRedimensionando && !(VM?.EmOperacaoInterativa ?? false))
        {
            Dispatcher.InvokeAsync(ReconstruirCanvas);
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignerViewModel.Modelo) ||
            e.PropertyName == nameof(DesignerViewModel.FolhaLarguraMm) ||
            e.PropertyName == nameof(DesignerViewModel.FolhaAlturaMm) ||
            e.PropertyName == nameof(DesignerViewModel.ProdutoAtual) ||
            e.PropertyName == nameof(DesignerViewModel.MostrarDadosReais) ||
            e.PropertyName == nameof(DesignerViewModel.MostrarGrade))
        {
            Dispatcher.InvokeAsync(ReconstruirCanvas);
        }
        else if (e.PropertyName == nameof(DesignerViewModel.ElementoSelecionado))
        {
            AtualizarAdornerParaSelecionado();
        }
        else if (e.PropertyName == nameof(DesignerViewModel.ZoomNivel))
        {
            DesenharReguas();
        }
        else if (e.PropertyName == nameof(DesignerViewModel.FerramentaMaoAtiva))
        {
            if (CanvasScrollViewer != null)
            {
                CanvasScrollViewer.Cursor = (VM?.FerramentaMaoAtiva ?? false) ? Cursors.Hand : Cursors.Arrow;
            }
            if (EditorCanvas != null)
            {
                EditorCanvas.Cursor = (VM?.FerramentaMaoAtiva ?? false) ? Cursors.Hand : Cursors.Arrow;
            }
        }
    }

    private void AtualizarAdornerParaSelecionado()
    {
        RemoverAdorner();
        if (VM?.ElementoSelecionado == null || EditorCanvas == null) return;

        foreach (UIElement child in EditorCanvas.Children)
        {
            if (child is FrameworkElement fe && fe.Tag == VM.ElementoSelecionado)
            {
                AplicarAdorner(fe, VM.ElementoSelecionado);
                break;
            }
        }
    }

    private DesignerViewModel? VM => DataContext as DesignerViewModel;

    public void ReconstruirCanvas()
    {
        var vm = VM;
        if (vm?.Modelo?.Elementos == null || EditorCanvas == null) return;

        EditorCanvas.Children.Clear();
        RemoverAdorner();

        var larguraDip = EditorDesenhoService.Dip(vm.Modelo.LarguraMm);
        var alturaDip = EditorDesenhoService.Dip(vm.Modelo.AlturaMm);

        EditorCanvas.Width = larguraDip;
        EditorCanvas.Height = alturaDip;

        // Fundo com grade quadriculada suave (cada quadrado = 5mm)
        var tamanhoGrade = EditorDesenhoService.Dip(5.0);
        var grupoGeometria = new GeometryGroup();
        grupoGeometria.Children.Add(new LineGeometry(new Point(0, 0), new Point(tamanhoGrade, 0)));
        grupoGeometria.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, tamanhoGrade)));

        var brushGrade = new DrawingBrush
        {
            Viewport = new Rect(0, 0, tamanhoGrade, tamanhoGrade),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Drawing = new GeometryDrawing
            {
                Pen = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)), 0.6),
                Geometry = grupoGeometria
            }
        };
        EditorCanvas.Background = (VM?.MostrarGrade ?? true) ? brushGrade : Brushes.White;

        // Desenhar guias de margem segura (1.5mm das bordas)
        var margemSeguraDip = EditorDesenhoService.Dip(1.5);
        var retMargem = new Rectangle
        {
            Width = Math.Max(0, larguraDip - (margemSeguraDip * 2)),
            Height = Math.Max(0, alturaDip - (margemSeguraDip * 2)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x35, 0x25, 0x63, 0xEB)),
            StrokeThickness = 0.8,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(retMargem, margemSeguraDip);
        Canvas.SetTop(retMargem, margemSeguraDip);
        EditorCanvas.Children.Add(retMargem);

        foreach (var el in vm.Modelo.Elementos)
        {
            var elementoVisual = CriarVisualElemento(el);
            EditorCanvas.Children.Add(elementoVisual);

            if (el.IsSelected)
            {
                Dispatcher.InvokeAsync(() => AplicarAdorner(elementoVisual, el), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        DesenharReguas();
    }

    private void DesenharReguas()
    {
        if (VM == null || ReguaHorizontalCanvas == null || ReguaVerticalCanvas == null) return;

        ReguaHorizontalCanvas.Children.Clear();
        ReguaVerticalCanvas.Children.Clear();

        var zoom = VM.ZoomNivel;
        var largMm = VM.Modelo.LarguraMm;
        var altMm = VM.Modelo.AlturaMm;

        var largDip = EditorDesenhoService.Dip(largMm) * zoom;
        var altDip = EditorDesenhoService.Dip(altMm) * zoom;

        ReguaHorizontalCanvas.Width = largDip;
        ReguaVerticalCanvas.Height = altDip;

        var pincelTicks = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)); // slate-400
        var pincelTexto = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)); // slate-600

        // Régua Horizontal (X em mm)
        for (int mm = 0; mm <= (int)Math.Ceiling(largMm); mm++)
        {
            var xDip = EditorDesenhoService.Dip(mm) * zoom;
            double tickHeight = (mm % 10 == 0) ? 12 : (mm % 5 == 0) ? 7 : 4;

            var linha = new Line
            {
                X1 = xDip,
                Y1 = 24 - tickHeight,
                X2 = xDip,
                Y2 = 24,
                Stroke = pincelTicks,
                StrokeThickness = (mm % 10 == 0) ? 1.0 : 0.6
            };
            ReguaHorizontalCanvas.Children.Add(linha);

            if (mm % 10 == 0 && mm > 0 && mm < largMm)
            {
                var txt = new TextBlock
                {
                    Text = mm.ToString(),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = pincelTexto
                };
                Canvas.SetLeft(txt, xDip + 2);
                Canvas.SetTop(txt, 2);
                ReguaHorizontalCanvas.Children.Add(txt);
            }
        }

        // Régua Vertical (Y em mm)
        for (int mm = 0; mm <= (int)Math.Ceiling(altMm); mm++)
        {
            var yDip = EditorDesenhoService.Dip(mm) * zoom;
            double tickWidth = (mm % 10 == 0) ? 12 : (mm % 5 == 0) ? 7 : 4;

            var linha = new Line
            {
                X1 = 24 - tickWidth,
                Y1 = yDip,
                X2 = 24,
                Y2 = yDip,
                Stroke = pincelTicks,
                StrokeThickness = (mm % 10 == 0) ? 1.0 : 0.6
            };
            ReguaVerticalCanvas.Children.Add(linha);

            if (mm % 10 == 0 && mm > 0 && mm < altMm)
            {
                var txt = new TextBlock
                {
                    Text = mm.ToString(),
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = pincelTexto
                };
                Canvas.SetLeft(txt, 2);
                Canvas.SetTop(txt, yDip + 2);
                ReguaVerticalCanvas.Children.Add(txt);
            }
        }
    }

    private void CanvasScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Alinha os offsets das réguas conforme o scroll da bancada
        if (ReguaHorizontalCanvas != null && CanvasScrollViewer != null)
        {
            var transformH = new TranslateTransform(-CanvasScrollViewer.HorizontalOffset + 32, 0);
            ReguaHorizontalCanvas.RenderTransform = transformH;
        }

        if (ReguaVerticalCanvas != null && CanvasScrollViewer != null)
        {
            var transformV = new TranslateTransform(0, -CanvasScrollViewer.VerticalOffset + 32);
            ReguaVerticalCanvas.RenderTransform = transformV;
        }
    }

    private FrameworkElement CriarVisualElemento(ElementoDesign el)
    {
        var xDip = EditorDesenhoService.Dip(el.X);
        var yDip = EditorDesenhoService.Dip(el.Y);
        var largDip = Math.Max(EditorDesenhoService.Dip(el.Largura), 4);
        var altDip = Math.Max(EditorDesenhoService.Dip(el.Altura), 2);

        var container = new Border
        {
            Width = largDip,
            Height = altDip,
            Tag = el,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll
        };

        Canvas.SetLeft(container, xDip);
        Canvas.SetTop(container, yDip);

        if (el.Rotacao != 0)
        {
            container.RenderTransformOrigin = new Point(0.5, 0.5);
            container.RenderTransform = new RotateTransform(el.Rotacao);
        }

        UIElement conteudo;

        switch (el.Tipo)
        {
            case TipoElemento.Texto:
                var textoExibicao = (VM?.MostrarDadosReais ?? true)
                    ? EditorDesenhoService.ResolverTexto(el, VM?.ProdutoAtual, null, VM?.NomeLoja ?? "NEXUS LS")
                    : el.Texto;

                var txt = new TextBlock
                {
                    Text = textoExibicao,
                    FontFamily = new FontFamily(!string.IsNullOrWhiteSpace(el.Fonte) ? el.Fonte : "Segoe UI"),
                    FontSize = el.TamanhoFonte,
                    FontWeight = el.Negrito ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = el.Italico ? FontStyles.Italic : FontStyles.Normal,
                    TextAlignment = el.Alinhamento,
                    TextTrimming = TextTrimming.WordEllipsis,
                    Foreground = EditorDesenhoService.ConverterCor(el.CorTexto, Brushes.Black),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                if (el.Riscado)
                {
                    txt.TextDecorations = TextDecorations.Strikethrough;
                }

                conteudo = txt;
                break;

            case TipoElemento.Imagem:
                var imgCtrl = new Image
                {
                    Stretch = el.ManterProporcao ? Stretch.Uniform : Stretch.Fill,
                    IsHitTestVisible = false
                };

                if (!string.IsNullOrWhiteSpace(el.ImagemBase64))
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(el.ImagemBase64);
                        var ms = new System.IO.MemoryStream(bytes);
                        var bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.StreamSource = ms;
                        bi.EndInit();
                        imgCtrl.Source = bi;
                    }
                    catch { }
                }
                else
                {
                    var placeholderStack = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    placeholderStack.Children.Add(new Wpf.Ui.Controls.SymbolIcon
                    {
                        Symbol = Wpf.Ui.Controls.SymbolRegular.Image24,
                        FontSize = Math.Max(12, Math.Min(22, altDip * 0.4)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    placeholderStack.Children.Add(new TextBlock
                    {
                        Text = "Logotipo",
                        FontSize = 8,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    });

                    var bordaPlaceholder = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                        Child = placeholderStack
                    };
                    conteudo = bordaPlaceholder;
                    container.Child = conteudo;
                    ConfigurarEventosElemento(container, el);
                    return container;
                }

                conteudo = imgCtrl;
                break;

            case TipoElemento.CodigoBarras:
                var cod = EditorDesenhoService.ResolverCodigo(el, VM?.ProdutoAtual);
                var altPx = Math.Max(20, (int)altDip * 2);
                var largPx = Math.Max(40, (int)largDip * 2);
                var bmpBarcode = BarcodeGeneratorService.Gerar(cod, el.TipoCodigo, largPx, altPx, margem: 0);

                var gridBarcode = new Grid { IsHitTestVisible = false };
                gridBarcode.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var imgBarcode = new Image
                {
                    Source = bmpBarcode,
                    Stretch = Stretch.Fill
                };
                Grid.SetRow(imgBarcode, 0);
                gridBarcode.Children.Add(imgBarcode);

                if (el.MostrarTextoLegivel && el.TipoCodigo != TipoSimboloCodigo.QrCode)
                {
                    gridBarcode.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var txtLegivel = new TextBlock
                    {
                        Text = cod,
                        FontSize = Math.Clamp(altDip * 0.28, 6.5, 9.5),
                        FontFamily = new FontFamily("Segoe UI, Consolas"),
                        TextAlignment = TextAlignment.Center,
                        Foreground = Brushes.Black,
                        Margin = new Thickness(0, 1, 0, 0)
                    };
                    Grid.SetRow(txtLegivel, 1);
                    gridBarcode.Children.Add(txtLegivel);
                }
                conteudo = gridBarcode;
                break;

            case TipoElemento.Retangulo:
                var raioDip = EditorDesenhoService.Dip(el.RaioCanto);
                conteudo = new Rectangle
                {
                    RadiusX = raioDip,
                    RadiusY = raioDip,
                    Stroke = EditorDesenhoService.ConverterCor(el.CorTexto, Brushes.Black),
                    StrokeThickness = Math.Max(0.5, el.EspessuraBorda * 0.8),
                    Fill = el.Preenchido ? EditorDesenhoService.ConverterCor(el.CorFundo, Brushes.Black) : Brushes.Transparent,
                    IsHitTestVisible = false
                };
                break;

            case TipoElemento.Linha:
                conteudo = new Line
                {
                    X1 = 0,
                    Y1 = altDip / 2,
                    X2 = largDip,
                    Y2 = altDip / 2,
                    Stroke = EditorDesenhoService.ConverterCor(el.CorTexto, Brushes.Black),
                    StrokeThickness = Math.Max(0.5, el.EspessuraBorda * 0.8),
                    IsHitTestVisible = false
                };
                break;

            default:
                conteudo = new Border { Background = Brushes.LightGray };
                break;
        }

        container.Child = conteudo;
        ConfigurarEventosElemento(container, el);
        return container;
    }

    private void ConfigurarEventosElemento(FrameworkElement element, ElementoDesign el)
    {
        element.MouseLeftButtonDown += (s, e) =>
        {
            // Se a ferramenta Mão estiver ativa, qualquer clique navega a prancheta (Pan)
            if (VM != null && VM.FerramentaMaoAtiva)
            {
                _estaEmPan = true;
                _pontoInicialPan = e.GetPosition(CanvasScrollViewer);
                _offsetInicialPanH = CanvasScrollViewer.HorizontalOffset;
                _offsetInicialPanV = CanvasScrollViewer.VerticalOffset;
                CanvasScrollViewer.CaptureMouse();
                CanvasScrollViewer.Cursor = Cursors.Hand;
                e.Handled = true;
                return;
            }

            // Elementos bloqueados não podem ser selecionados ou movidos no canvas
            if (el.Bloqueado)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                if (el.Tipo == TipoElemento.Texto)
                {
                    IniciarEdicaoTextoInline(element as Border, el);
                    e.Handled = true;
                    return;
                }
                else if (el.Tipo == TipoElemento.Imagem)
                {
                    VM?.TrocarImagemElementoCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            // Seleção múltipla com SHIFT + clique
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                el.IsSelected = !el.IsSelected;
                if (el.IsSelected)
                {
                    VM?.SelecionarElemento(el);
                }
                else
                {
                    var restante = VM?.Modelo.Elementos.FirstOrDefault(i => i.IsSelected);
                    if (restante != null) VM?.SelecionarElemento(restante);
                    else VM?.DesmarcarSelecao();
                }
                e.Handled = true;
                return;
            }

            e.Handled = true;
            this.Focus();
            VM?.SelecionarElemento(el);

            _estaArrastando = true;
            _elementoSendoArrastado = el;
            if (VM != null) VM.EmOperacaoInterativa = true;
            _pontoInicialClique = e.GetPosition(EditorCanvas);
            _origemXMm = el.X;
            _origemYMm = el.Y;
            element.CaptureMouse();
        };

        element.MouseMove += (s, e) =>
        {
            if (_estaArrastando && _elementoSendoArrastado == el)
            {
                var pontoAtual = e.GetPosition(EditorCanvas);
                var dxMm = EditorDesenhoService.Mm(pontoAtual.X - _pontoInicialClique.X);
                var dyMm = EditorDesenhoService.Mm(pontoAtual.Y - _pontoInicialClique.Y);

                // Comportamento CorelDRAW: SHIFT durante o movimento restringe em linha reta (horizontal ou vertical)
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    if (Math.Abs(dxMm) >= Math.Abs(dyMm))
                    {
                        dyMm = 0;
                    }
                    else
                    {
                        dxMm = 0;
                    }
                }

                // Snap suave de 0.5 mm ou contínuo
                var step = (VM?.SnapGrade ?? true) ? 0.5 : 0.1;
                var novoX = Math.Round((_origemXMm + dxMm) / step) * step;
                var novoY = Math.Round((_origemYMm + dyMm) / step) * step;

                // Margem livre para permitir reposicionar elementos
                var margemLivre = 20.0;
                var limiteX = (VM?.Modelo.LarguraMm ?? 50.0) + margemLivre;
                var limiteY = (VM?.Modelo.AlturaMm ?? 30.0) + margemLivre;
                novoX = Math.Clamp(novoX, -margemLivre, limiteX);
                novoY = Math.Clamp(novoY, -margemLivre, limiteY);

                el.X = novoX;
                el.Y = novoY;

                Canvas.SetLeft(element, EditorDesenhoService.Dip(el.X));
                Canvas.SetTop(element, EditorDesenhoService.Dip(el.Y));

                _camadaAdorner?.Update();

                VM?.AtualizarCoordenadasMouse(novoX, novoY);
            }
        };

        element.MouseLeftButtonUp += (s, e) =>
        {
            if (_estaArrastando)
            {
                _estaArrastando = false;
                _elementoSendoArrastado = null;
                element.ReleaseMouseCapture();
                if (VM != null) VM.EmOperacaoInterativa = false;
                _camadaAdorner?.Update();
                VM?.RegistrarSnapshot();
                VM?.AtualizarPreview();
            }
        };
    }

    private void IniciarEdicaoTextoInline(Border? container, ElementoDesign el)
    {
        if (container == null || VM == null) return;

        var tb = new TextBox
        {
            Text = el.Texto,
            FontFamily = new FontFamily(!string.IsNullOrWhiteSpace(el.Fonte) ? el.Fonte : "Segoe UI"),
            FontSize = el.TamanhoFonte,
            FontWeight = el.Negrito ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = el.Italico ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = el.Alinhamento,
            Foreground = EditorDesenhoService.ConverterCor(el.CorTexto, Brushes.Black),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(2),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var conteudoOriginal = container.Child;
        container.Child = tb;

        void Finalizar(bool salvar)
        {
            if (salvar)
            {
                el.Texto = tb.Text;
            }
            container.Child = conteudoOriginal;
            if (conteudoOriginal is TextBlock txtOriginal)
            {
                txtOriginal.Text = (VM.MostrarDadosReais)
                    ? EditorDesenhoService.ResolverTexto(el, VM.ProdutoAtual, null, VM.NomeLoja)
                    : el.Texto;
            }
            VM.AtualizarPreview();
        }

        tb.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Finalizar(salvar: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Finalizar(salvar: false);
                e.Handled = true;
            }
        };

        tb.LostFocus += (s, e) => Finalizar(salvar: true);

        Dispatcher.InvokeAsync(() =>
        {
            tb.Focus();
            tb.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AplicarAdorner(FrameworkElement element, ElementoDesign el)
    {
        RemoverAdorner();
        _camadaAdorner = AdornerLayer.GetAdornerLayer(EditorCanvas);
        if (_camadaAdorner != null)
        {
            _adornerAtual = new ElementoAdorner(
                element,
                el,
                onRedimensionado: () =>
                {
                    _estaRedimensionando = false;
                    if (VM != null) VM.EmOperacaoInterativa = false;
                    VM?.RegistrarSnapshot();
                    VM?.AtualizarPreview();
                },
                onRedimensionandoIniciado: () =>
                {
                    _estaRedimensionando = true;
                    if (VM != null) VM.EmOperacaoInterativa = true;
                });
            _camadaAdorner.Add(_adornerAtual);
        }
    }

    private void RemoverAdorner()
    {
        if (_camadaAdorner != null && _adornerAtual != null)
        {
            _camadaAdorner.Remove(_adornerAtual);
            _adornerAtual = null;
        }
    }

    private bool _estaEmMarquee;
    private Point _pontoInicialMarquee;
    private Rectangle? _retanguloMarquee;

    // =========================================================================
    // NAVEGAÇÃO PAN (MÃOZINHA) & ZOOM CENTRADO NO CURSOR (ESTILO CAD / COREL)
    // =========================================================================
    private void CanvasScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Se a ferramenta Mão estiver ativa ou o botão do meio estiver pressionado, inicia Pan
        if (e.MiddleButton == MouseButtonState.Pressed || (VM != null && VM.FerramentaMaoAtiva && e.LeftButton == MouseButtonState.Pressed))
        {
            _estaEmPan = true;
            _pontoInicialPan = e.GetPosition(CanvasScrollViewer);
            _offsetInicialPanH = CanvasScrollViewer.HorizontalOffset;
            _offsetInicialPanV = CanvasScrollViewer.VerticalOffset;
            CanvasScrollViewer.CaptureMouse();
            CanvasScrollViewer.Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        // Clicar fora de qualquer elemento (na folha vazia ou no fundo cinza) desmarca o elemento
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var hit = e.OriginalSource as DependencyObject;
            bool clicouEmElemento = false;
            while (hit != null && hit != CanvasScrollViewer)
            {
                if (hit is Border b && b.Tag is ElementoDesign)
                {
                    clicouEmElemento = true;
                    break;
                }
                if (hit is Thumb || hit is Adorner)
                {
                    clicouEmElemento = true;
                    break;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }

            if (!clicouEmElemento)
            {
                this.Focus();
                VM?.DesmarcarSelecao();
                RemoverAdorner();
            }
        }
    }

    private void CanvasScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_estaEmPan)
        {
            var posAtual = e.GetPosition(CanvasScrollViewer);
            var deltaX = posAtual.X - _pontoInicialPan.X;
            var deltaY = posAtual.Y - _pontoInicialPan.Y;
            CanvasScrollViewer.ScrollToHorizontalOffset(_offsetInicialPanH - deltaX);
            CanvasScrollViewer.ScrollToVerticalOffset(_offsetInicialPanV - deltaY);
            e.Handled = true;
        }
    }

    private void CanvasScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_estaEmPan)
        {
            _estaEmPan = false;
            CanvasScrollViewer.ReleaseMouseCapture();
            CanvasScrollViewer.Cursor = (VM != null && VM.FerramentaMaoAtiva) ? Cursors.Hand : Cursors.Arrow;
            e.Handled = true;
        }
    }

    private void CanvasScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (VM == null || CanvasScrollViewer == null) return;

        var posViewerAntes = e.GetPosition(CanvasScrollViewer);
        double fator = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        var zoomNovo = Math.Clamp(Math.Round(VM.ZoomNivel * fator, 2), 0.5, 4.0);

        if (Math.Abs(zoomNovo - VM.ZoomNivel) > 0.005)
        {
            var zoomAntigo = VM.ZoomNivel;
            var escala = zoomNovo / zoomAntigo;

            var contentX = CanvasScrollViewer.HorizontalOffset + posViewerAntes.X;
            var contentY = CanvasScrollViewer.VerticalOffset + posViewerAntes.Y;

            VM.ZoomNivel = zoomNovo;

            Dispatcher.InvokeAsync(() =>
            {
                var novoOffsetH = (contentX * escala) - posViewerAntes.X;
                var novoOffsetV = (contentY * escala) - posViewerAntes.Y;
                CanvasScrollViewer.ScrollToHorizontalOffset(novoOffsetH);
                CanvasScrollViewer.ScrollToVerticalOffset(novoOffsetV);
                DesenharReguas();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        e.Handled = true;
    }

    private void ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        this.Focus();
        VM?.DesmarcarSelecao();
        RemoverAdorner();
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.Focus();
        if (VM != null && VM.FerramentaMaoAtiva) return;

        if (e.OriginalSource == EditorCanvas)
        {
            VM?.DesmarcarSelecao();
            RemoverAdorner();

            // Inicia seleção retangular por área (Marquee)
            _estaEmMarquee = true;
            _pontoInicialMarquee = e.GetPosition(EditorCanvas);

            if (_retanguloMarquee == null)
            {
                _retanguloMarquee = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    Fill = new SolidColorBrush(Color.FromArgb(0x20, 0x25, 0x63, 0xEB)),
                    IsHitTestVisible = false
                };
            }

            Canvas.SetLeft(_retanguloMarquee, _pontoInicialMarquee.X);
            Canvas.SetTop(_retanguloMarquee, _pontoInicialMarquee.Y);
            _retanguloMarquee.Width = 0;
            _retanguloMarquee.Height = 0;

            if (!EditorCanvas.Children.Contains(_retanguloMarquee))
            {
                EditorCanvas.Children.Add(_retanguloMarquee);
            }

            EditorCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (VM == null || EditorCanvas == null) return;
        var pos = e.GetPosition(EditorCanvas);
        var xMm = EditorDesenhoService.Mm(pos.X);
        var yMm = EditorDesenhoService.Mm(pos.Y);
        VM.AtualizarCoordenadasMouse(xMm, yMm);

        if (_estaEmMarquee && _retanguloMarquee != null)
        {
            var x = Math.Min(pos.X, _pontoInicialMarquee.X);
            var y = Math.Min(pos.Y, _pontoInicialMarquee.Y);
            var w = Math.Abs(pos.X - _pontoInicialMarquee.X);
            var h = Math.Abs(pos.Y - _pontoInicialMarquee.Y);

            Canvas.SetLeft(_retanguloMarquee, x);
            Canvas.SetTop(_retanguloMarquee, y);
            _retanguloMarquee.Width = w;
            _retanguloMarquee.Height = h;
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_estaEmMarquee)
        {
            _estaEmMarquee = false;
            EditorCanvas.ReleaseMouseCapture();

            if (_retanguloMarquee != null)
            {
                var xDip = Canvas.GetLeft(_retanguloMarquee);
                var yDip = Canvas.GetTop(_retanguloMarquee);
                var wDip = _retanguloMarquee.Width;
                var hDip = _retanguloMarquee.Height;

                EditorCanvas.Children.Remove(_retanguloMarquee);

                if (wDip > 4 && hDip > 4 && VM != null)
                {
                    var retSelMm = new Rect(
                        EditorDesenhoService.Mm(xDip),
                        EditorDesenhoService.Mm(yDip),
                        EditorDesenhoService.Mm(wDip),
                        EditorDesenhoService.Mm(hDip)
                    );

                    ElementoDesign? primeiro = null;
                    bool temSelecao = false;

                    foreach (var el in VM.Modelo.Elementos)
                    {
                        if (!el.Visivel || el.Bloqueado)
                        {
                            el.IsSelected = false;
                            continue;
                        }

                        var elRect = new Rect(el.X, el.Y, el.Largura, el.Altura);
                        if (retSelMm.IntersectsWith(elRect))
                        {
                            el.IsSelected = true;
                            primeiro ??= el;
                            temSelecao = true;
                        }
                        else
                        {
                            el.IsSelected = false;
                        }
                    }

                    if (temSelecao)
                    {
                        VM.ElementoSelecionado = primeiro;
                    }
                    else
                    {
                        VM.DesmarcarSelecao();
                    }
                    ReconstruirCanvas();
                }
            }
        }
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string preset)
        {
            VM?.AplicarPreset(preset);
            ReconstruirCanvas();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        UserControl_KeyDown(sender, e);
    }

    private void FolhaTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            var be = tb.GetBindingExpression(TextBox.TextProperty);
            be?.UpdateSource();
            Keyboard.ClearFocus();
            this.Focus();
            e.Handled = true;
        }
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (VM == null || e.Handled) return;

        // Se estiver digitando em uma caixa de texto inline, não intercepta atalhos da prancheta
        if (Keyboard.FocusedElement is TextBox tb)
        {
            if (e.Key == Key.Escape)
            {
                Keyboard.ClearFocus();
                this.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                var be = tb.GetBindingExpression(TextBox.TextProperty);
                be?.UpdateSource();
                Keyboard.ClearFocus();
                this.Focus();
                e.Handled = true;
            }
            return;
        }

        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        // CTRL + S: Salvar projeto
        if (isCtrl && !isShift && e.Key == Key.S)
        {
            VM.SalvarProjetoRapidoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + SHIFT + S: Salvar projeto como
        if (isCtrl && isShift && e.Key == Key.S)
        {
            VM.SalvarProjetoComoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + N: Novo Documento
        if (isCtrl && !isShift && e.Key == Key.N)
        {
            VM.AbrirNovoDocumentoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + Z: Desfazer
        if (isCtrl && !isShift && e.Key == Key.Z)
        {
            VM.DesfazerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + Y ou CTRL + SHIFT + Z: Refazer
        if ((isCtrl && e.Key == Key.Y) || (isCtrl && isShift && e.Key == Key.Z))
        {
            VM.RefazerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + C: Copiar
        if (isCtrl && !isShift && e.Key == Key.C)
        {
            VM.CopiarCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + V: Colar
        if (isCtrl && !isShift && e.Key == Key.V)
        {
            VM.ColarCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + X: Recortar
        if (isCtrl && !isShift && e.Key == Key.X)
        {
            VM.RecortarCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + A: Selecionar tudo
        if (isCtrl && !isShift && e.Key == Key.A)
        {
            VM.SelecionarTudoCommand.Execute(null);
            ReconstruirCanvas();
            e.Handled = true;
            return;
        }

        // CTRL + D: Duplicar objeto
        if (isCtrl && !isShift && e.Key == Key.D)
        {
            VM.DuplicarSelecionadoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + G: Agrupar objetos
        if (isCtrl && !isShift && e.Key == Key.G)
        {
            VM.AgruparCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // CTRL + SHIFT + G: Desagrupar objetos
        if (isCtrl && isShift && e.Key == Key.G)
        {
            VM.DesagruparCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // H: Ativar/Alternar Ferramenta Mão (Hand Tool)
        if (e.Key == Key.H && !isCtrl && !isAlt)
        {
            VM.AlternarFerramentaMaoCommand.Execute(null);
            if (CanvasScrollViewer != null)
            {
                CanvasScrollViewer.Cursor = VM.FerramentaMaoAtiva ? Cursors.Hand : Cursors.Arrow;
            }
            if (EditorCanvas != null)
            {
                EditorCanvas.Cursor = VM.FerramentaMaoAtiva ? Cursors.Hand : Cursors.Arrow;
            }
            e.Handled = true;
            return;
        }

        // ESC: Fechar Modais se abertos, ou desselecionar / sair da Ferramenta Mão
        if (e.Key == Key.Escape)
        {
            if (VM.ModalCodigoAberto)
            {
                VM.ModalCodigoAberto = false;
                e.Handled = true;
                return;
            }
            if (VM.NovoDocumentoAberto || VM.GaleriaModelosAberta)
            {
                VM.NovoDocumentoAberto = false;
                VM.GaleriaModelosAberta = false;
                e.Handled = true;
                return;
            }
            if (VM.FerramentaMaoAtiva)
            {
                VM.FerramentaMaoAtiva = false;
                if (CanvasScrollViewer != null) CanvasScrollViewer.Cursor = Cursors.Arrow;
                if (EditorCanvas != null) EditorCanvas.Cursor = Cursors.Arrow;
            }
            else
            {
                VM.DesmarcarSelecao();
                RemoverAdorner();
            }
            e.Handled = true;
            return;
        }

        // DELETE / BACKSPACE: Excluir objeto selecionado
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            VM.ExcluirSelecionadoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Atalhos de Z-Index / Camadas:
        // CTRL + SHIFT + ]: Trazer para a frente
        // CTRL + SHIFT + [: Enviar para o fundo
        // CTRL + ]: Avançar uma posição
        // CTRL + [: Enviar uma posição para trás
        bool isOpenBracket = e.Key == Key.OemOpenBrackets || e.Key == Key.Oem4 || e.Key == Key.Oem1 || e.Key == Key.PageDown;
        bool isCloseBracket = e.Key == Key.OemCloseBrackets || e.Key == Key.Oem6 || e.Key == Key.Oem2 || e.Key == Key.PageUp;

        if (isCtrl && isShift && isCloseBracket)
        {
            VM.TrazerParaFrenteCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (isCtrl && isShift && isOpenBracket)
        {
            VM.EnviarParaTrasCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (isCtrl && !isShift && isCloseBracket)
        {
            VM.AvancarCamadaCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (isCtrl && !isShift && isOpenBracket)
        {
            VM.RecuarCamadaCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // F4: Zoom para Ajustar Etiqueta inteira
        if (e.Key == Key.F4)
        {
            VM.ZoomAjustarCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // G: Alternar Grade (quando não pressionado com Ctrl)
        if (e.Key == Key.G && !isCtrl && !isAlt)
        {
            VM.AlternarGradeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (VM.ElementoSelecionado == null) return;

        var el = VM.ElementoSelecionado;

        // P: Centralizar na Página
        if (e.Key == Key.P && !isCtrl && !isAlt)
        {
            VM.CentralizarNaPaginaCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // C: Centralizar Horizontalmente
        if (e.Key == Key.C && !isCtrl && !isAlt)
        {
            VM.CentralizarHorizontalmenteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // E: Centralizar Verticalmente
        if (e.Key == Key.E && !isCtrl && !isAlt)
        {
            VM.CentralizarVerticalmenteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // T: Alinhar Topo
        if (e.Key == Key.T && !isCtrl && !isAlt)
        {
            VM.AlinharTopoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // B: Alinhar Base
        if (e.Key == Key.B && !isCtrl && !isAlt)
        {
            VM.AlinharBaseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // L: Alinhar Esquerda
        if (e.Key == Key.L && !isCtrl && !isAlt)
        {
            VM.AlinharEsquerdaCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // R: Alinhar Direita
        if (e.Key == Key.R && !isCtrl && !isAlt)
        {
            VM.AlinharDireitaCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Deslocamento por setas (Nudge): Setas = 0.5mm, Shift+Setas = 2.0mm, Ctrl+Setas = 0.1mm
        double step = 0.5;
        if (isShift) step = 2.0; // Super Nudge (Shift + Setas)
        else if (isCtrl) step = 0.1; // Micro Nudge (Ctrl + Setas)

        switch (e.Key)
        {
            case Key.Left:
                el.X = Math.Round((el.X - step) * 10) / 10.0;
                e.Handled = true;
                break;
            case Key.Right:
                el.X = Math.Round((el.X + step) * 10) / 10.0;
                e.Handled = true;
                break;
            case Key.Up:
                el.Y = Math.Round((el.Y - step) * 10) / 10.0;
                e.Handled = true;
                break;
            case Key.Down:
                el.Y = Math.Round((el.Y + step) * 10) / 10.0;
                e.Handled = true;
                break;
        }

        if (e.Handled)
        {
            Dispatcher.InvokeAsync(ReconstruirCanvas);
            VM.AtualizarPreview();
        }
    }
}
