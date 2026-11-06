using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
    private ElementoDesign? _elementoSendoArrastado;
    private double _origemXMm;
    private double _origemYMm;
    private AdornerLayer? _camadaAdorner;
    private ElementoAdorner? _adornerAtual;

    public DesignerView()
    {
        InitializeComponent();
        DataContextChanged += DesignerView_DataContextChanged;
        Loaded += DesignerView_Loaded;
    }

    private void DesignerView_Loaded(object sender, RoutedEventArgs e)
    {
        _camadaAdorner = AdornerLayer.GetAdornerLayer(EditorCanvas);
        ReconstruirCanvas();
    }

    private void DesignerView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DesignerViewModel vmAntigo)
        {
            vmAntigo.PropertyChanged -= Vm_PropertyChanged;
        }

        if (e.NewValue is DesignerViewModel vmNovo)
        {
            vmNovo.PropertyChanged += Vm_PropertyChanged;
            ReconstruirCanvas();
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignerViewModel.Modelo) ||
            e.PropertyName == nameof(DesignerViewModel.ElementoSelecionado))
        {
            App.Log($"DesignerView Vm_PropertyChanged: {e.PropertyName}");
            Dispatcher.InvokeAsync(ReconstruirCanvas);
        }
    }

    private DesignerViewModel? VM => DataContext as DesignerViewModel;

    public void ReconstruirCanvas()
    {
        App.Log("DesignerView ReconstruirCanvas BEGIN");
        if (VM == null || EditorCanvas == null)
        {
            App.Log("DesignerView ReconstruirCanvas SKIPPED (VM or EditorCanvas is null)");
            return;
        }

        EditorCanvas.Children.Clear();
        RemoverAdorner();

        var larguraDip = EditorDesenhoService.Dip(VM.Modelo.LarguraMm);
        var alturaDip = EditorDesenhoService.Dip(VM.Modelo.AlturaMm);

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
        EditorCanvas.Background = brushGrade;

        foreach (var el in VM.Modelo.Elementos)
        {
            var elementoVisual = CriarVisualElemento(el);
            EditorCanvas.Children.Add(elementoVisual);

            if (el.IsSelected)
            {
                Dispatcher.InvokeAsync(() => AplicarAdorner(elementoVisual, el), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        App.Log($"DesignerView ReconstruirCanvas END ({VM.Modelo.Elementos.Count} elementos adicionados)");
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

        UIElement conteudo;

        switch (el.Tipo)
        {
            case TipoElemento.Texto:
                var txt = new TextBlock
                {
                    Text = EditorDesenhoService.ResolverTexto(el, VM?.ProdutoAtual, null, VM?.NomeLoja ?? "NEXUS LS"),
                    FontSize = el.TamanhoFonte,
                    FontWeight = el.Negrito ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = el.Italico ? FontStyles.Italic : FontStyles.Normal,
                    TextAlignment = el.Alinhamento,
                    TextTrimming = TextTrimming.WordEllipsis,
                    Foreground = Brushes.Black,
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

                // Carrega a imagem real se existir
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
                    var bordaPlaceholder = new Border
                    {
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(1),
                        Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
                        Child = new TextBlock
                        {
                            Text = "🖼️ [Logo]",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 9,
                            Foreground = Brushes.Gray
                        }
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

                var stack = new StackPanel { IsHitTestVisible = false };
                var imgBarcode = new Image
                {
                    Source = bmpBarcode,
                    Stretch = Stretch.Fill,
                    Height = el.MostrarTextoLegivel && el.TipoCodigo != TipoSimboloCodigo.QrCode ? Math.Max(6, altDip - 12) : altDip
                };
                stack.Children.Add(imgBarcode);

                if (el.MostrarTextoLegivel && el.TipoCodigo != TipoSimboloCodigo.QrCode)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = cod,
                        FontSize = 7.5,
                        TextAlignment = TextAlignment.Center,
                        Foreground = Brushes.Black
                    });
                }
                conteudo = stack;
                break;

            case TipoElemento.Retangulo:
                conteudo = new Rectangle
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = Math.Max(0.5, el.EspessuraBorda * 0.8),
                    Fill = el.Preenchido ? Brushes.Black : Brushes.Transparent,
                    IsHitTestVisible = false
                };
                break;

            case TipoElemento.Linha:
                conteudo = new System.Windows.Shapes.Line
                {
                    X1 = 0,
                    Y1 = altDip / 2,
                    X2 = largDip,
                    Y2 = altDip / 2,
                    Stroke = Brushes.Black,
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
            e.Handled = true;
            this.Focus();
            VM?.SelecionarElemento(el);

            _estaArrastando = true;
            _elementoSendoArrastado = el;
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

                // Snap suave de 0.5 mm
                var novoX = Math.Round((_origemXMm + dxMm) * 2) / 2.0;
                var novoY = Math.Round((_origemYMm + dyMm) * 2) / 2.0;

                novoX = Math.Clamp(novoX, 0, (VM?.Modelo.LarguraMm ?? 50) - el.Largura);
                novoY = Math.Clamp(novoY, 0, (VM?.Modelo.AlturaMm ?? 30) - el.Altura);

                el.X = novoX;
                el.Y = novoY;

                Canvas.SetLeft(element, EditorDesenhoService.Dip(el.X));
                Canvas.SetTop(element, EditorDesenhoService.Dip(el.Y));
            }
        };

        element.MouseLeftButtonUp += (s, e) =>
        {
            if (_estaArrastando)
            {
                _estaArrastando = false;
                _elementoSendoArrastado = null;
                element.ReleaseMouseCapture();
                VM?.AtualizarPreview();
            }
        };
    }

    private void AplicarAdorner(FrameworkElement element, ElementoDesign el)
    {
        RemoverAdorner();
        _camadaAdorner = AdornerLayer.GetAdornerLayer(EditorCanvas);
        if (_camadaAdorner != null)
        {
            _adornerAtual = new ElementoAdorner(element, el);
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

    private void ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == CanvasScrollViewer || e.OriginalSource == EditorCanvas)
        {
            this.Focus();
            VM?.DesmarcarSelecao();
            RemoverAdorner();
        }
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == EditorCanvas)
        {
            this.Focus();
            VM?.DesmarcarSelecao();
            RemoverAdorner();
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e) { }
    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string preset)
        {
            VM?.AplicarPreset(preset);
            ReconstruirCanvas();
        }
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (VM == null || VM.ElementoSelecionado == null) return;

        var el = VM.ElementoSelecionado;

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            VM.ExcluirSelecionadoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            VM.DuplicarSelecionadoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 1.0 : 0.5;

        switch (e.Key)
        {
            case Key.Left:
                el.X = Math.Max(0, Math.Round((el.X - step) * 2) / 2.0);
                e.Handled = true;
                break;
            case Key.Right:
                el.X = Math.Min(VM.Modelo.LarguraMm - el.Largura, Math.Round((el.X + step) * 2) / 2.0);
                e.Handled = true;
                break;
            case Key.Up:
                el.Y = Math.Max(0, Math.Round((el.Y - step) * 2) / 2.0);
                e.Handled = true;
                break;
            case Key.Down:
                el.Y = Math.Min(VM.Modelo.AlturaMm - el.Altura, Math.Round((el.Y + step) * 2) / 2.0);
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
