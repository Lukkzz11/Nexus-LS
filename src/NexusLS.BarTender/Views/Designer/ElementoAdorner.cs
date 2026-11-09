using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;

namespace NexusLS.BarTender.Views.Designer;

/// <summary>
/// Adorner de seleção e transformação vetorial no padrão CorelDRAW / Illustrator / VectorCraft.
/// - Suporta SHIFT (manter proporções), CTRL (restrição 1:1 quadrada), ALT (redimensionar do centro)
/// - Redimensionamento REAL de fontes para TipoElemento.Texto (aumenta e diminui a fonte real mantendo editabilidade)
/// - Bounding Box com 8 alças vetoriais nítidas para 2D, e 2 nós de extremidade para linhas
/// </summary>
public class ElementoAdorner : Adorner
{
    private readonly VisualCollection _visuais;
    private readonly ElementoDesign _elemento;
    private readonly Action? _onRedimensionado;
    private readonly Action? _onRedimensionandoIniciado;
    private readonly bool _ehLinha;

    // Estado inicial no início da transformação
    private double _origX;
    private double _origY;
    private double _origLarg;
    private double _origAlt;
    private double _origFonte;
    private double _centroX;
    private double _centroY;
    private double _aspectRatio;

    // Alças para Linha (Apenas 2 extremidades)
    private readonly Thumb? _linhaPontoInicial;
    private readonly Thumb? _linhaPontoFinal;

    // Alças para Elementos 2D (8 pontos clássicos)
    private readonly Thumb? _topoEsquerda;
    private readonly Thumb? _topoMeio;
    private readonly Thumb? _topoDireita;
    private readonly Thumb? _meioEsquerda;
    private readonly Thumb? _meioDireita;
    private readonly Thumb? _baixoEsquerda;
    private readonly Thumb? _baixoMeio;
    private readonly Thumb? _baixoDireita;

    public ElementoAdorner(UIElement elementoAdornado, ElementoDesign elemento, Action? onRedimensionado = null, Action? onRedimensionandoIniciado = null) 
        : base(elementoAdornado)
    {
        _elemento = elemento;
        _onRedimensionado = onRedimensionado;
        _onRedimensionandoIniciado = onRedimensionandoIniciado;
        _visuais = new VisualCollection(this);
        _ehLinha = elemento.Tipo == TipoElemento.Linha;

        void OnDragStarted(object? s, DragStartedEventArgs e) => IniciarArrastoTransformacao();
        void OnDragCompleted(object? s, DragCompletedEventArgs e) => _onRedimensionado?.Invoke();

        if (_ehLinha)
        {
            // Linhas possuem apenas nó esquerdo e direito
            _linhaPontoInicial = CriarThumb(Cursors.SizeWE);
            _linhaPontoFinal = CriarThumb(Cursors.SizeWE);

            _linhaPontoInicial.DragStarted += OnDragStarted;
            _linhaPontoFinal.DragStarted += OnDragStarted;

            _linhaPontoInicial.DragDelta += LinhaPontoInicial_DragDelta;
            _linhaPontoFinal.DragDelta += LinhaPontoFinal_DragDelta;

            _linhaPontoInicial.DragCompleted += OnDragCompleted;
            _linhaPontoFinal.DragCompleted += OnDragCompleted;

            _visuais.Add(_linhaPontoInicial);
            _visuais.Add(_linhaPontoFinal);
        }
        else
        {
            _topoEsquerda = CriarThumb(Cursors.SizeNWSE);
            _topoMeio = CriarThumb(Cursors.SizeNS);
            _topoDireita = CriarThumb(Cursors.SizeNESW);
            _meioEsquerda = CriarThumb(Cursors.SizeWE);
            _meioDireita = CriarThumb(Cursors.SizeWE);
            _baixoEsquerda = CriarThumb(Cursors.SizeNESW);
            _baixoMeio = CriarThumb(Cursors.SizeNS);
            _baixoDireita = CriarThumb(Cursors.SizeNWSE);

            _topoEsquerda.DragStarted += OnDragStarted;
            _topoMeio.DragStarted += OnDragStarted;
            _topoDireita.DragStarted += OnDragStarted;
            _meioEsquerda.DragStarted += OnDragStarted;
            _meioDireita.DragStarted += OnDragStarted;
            _baixoEsquerda.DragStarted += OnDragStarted;
            _baixoMeio.DragStarted += OnDragStarted;
            _baixoDireita.DragStarted += OnDragStarted;

            _topoEsquerda.DragDelta += TopoEsquerda_DragDelta;
            _topoMeio.DragDelta += TopoMeio_DragDelta;
            _topoDireita.DragDelta += TopoDireita_DragDelta;
            _meioEsquerda.DragDelta += MeioEsquerda_DragDelta;
            _meioDireita.DragDelta += MeioDireita_DragDelta;
            _baixoEsquerda.DragDelta += BaixoEsquerda_DragDelta;
            _baixoMeio.DragDelta += BaixoMeio_DragDelta;
            _baixoDireita.DragDelta += BaixoDireita_DragDelta;

            _topoEsquerda.DragCompleted += OnDragCompleted;
            _topoMeio.DragCompleted += OnDragCompleted;
            _topoDireita.DragCompleted += OnDragCompleted;
            _meioEsquerda.DragCompleted += OnDragCompleted;
            _meioDireita.DragCompleted += OnDragCompleted;
            _baixoEsquerda.DragCompleted += OnDragCompleted;
            _baixoMeio.DragCompleted += OnDragCompleted;
            _baixoDireita.DragCompleted += OnDragCompleted;

            _visuais.Add(_topoEsquerda);
            _visuais.Add(_topoMeio);
            _visuais.Add(_topoDireita);
            _visuais.Add(_meioEsquerda);
            _visuais.Add(_meioDireita);
            _visuais.Add(_baixoEsquerda);
            _visuais.Add(_baixoMeio);
            _visuais.Add(_baixoDireita);
        }
    }

    private void IniciarArrastoTransformacao()
    {
        _origX = _elemento.X;
        _origY = _elemento.Y;
        _origLarg = Math.Max(2.0, _elemento.Largura);
        _origAlt = Math.Max(1.0, _elemento.Altura);
        _origFonte = Math.Max(4.0, _elemento.TamanhoFonte);
        _centroX = _origX + (_origLarg / 2.0);
        _centroY = _origY + (_origAlt / 2.0);
        _aspectRatio = _origAlt > 0 ? (_origLarg / _origAlt) : 1.0;

        _onRedimensionandoIniciado?.Invoke();
    }

    /// <summary>
    /// Cria alça vetorial nítida no estilo CorelDRAW / VectorCraft (fundo branco, borda azul 1.5px)
    /// </summary>
    private static Thumb CriarThumb(Cursor cursor)
    {
        var thumb = new Thumb
        {
            Cursor = cursor,
            Width = 8,
            Height = 8,
            Focusable = false
        };

        var template = new ControlTemplate(typeof(Thumb));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.White);
        borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))); // Nexus Blue #2563EB
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.0));
        template.VisualTree = borderFactory;
        thumb.Template = template;

        return thumb;
    }

    private void SincronizarVisual()
    {
        if (AdornedElement is FrameworkElement fe)
        {
            var largDip = Math.Max(EditorDesenhoService.Dip(_elemento.Largura), 4);
            var altDip = Math.Max(EditorDesenhoService.Dip(_elemento.Altura), _ehLinha ? 2 : 2);

            fe.Width = largDip;
            fe.Height = altDip;
            Canvas.SetLeft(fe, EditorDesenhoService.Dip(_elemento.X));
            Canvas.SetTop(fe, EditorDesenhoService.Dip(_elemento.Y));

            if (fe is Border border && border.Child is FrameworkElement childFe)
            {
                if (childFe is TextBlock tb)
                {
                    // Atualização em tempo real da fonte real do texto
                    tb.FontSize = _elemento.TamanhoFonte;
                    tb.Width = largDip;
                    tb.Height = altDip;
                }
                else if (childFe is Line line)
                {
                    line.X2 = largDip;
                    line.Y1 = altDip / 2.0;
                    line.Y2 = altDip / 2.0;
                }
                else
                {
                    childFe.Width = largDip;
                    childFe.Height = altDip;
                }
            }

            fe.InvalidateMeasure();
            fe.InvalidateArrange();
            InvalidateArrange();
            InvalidateVisual();
            AdornerLayer.GetAdornerLayer(fe)?.Update();
        }
    }

    // =========================================================================
    // REDIMENSIONAMENTO DE LINHA (2 PONTOS)
    // =========================================================================
    private void LinhaPontoInicial_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dxMm = EditorDesenhoService.Mm(e.HorizontalChange);
        var novoX = _elemento.X + dxMm;
        var novaLarg = _elemento.Largura - dxMm;

        if (novaLarg >= 2.0)
        {
            _elemento.X = Math.Round(novoX, 1);
            _elemento.Largura = Math.Round(novaLarg, 1);
            SincronizarVisual();
        }
    }

    private void LinhaPontoFinal_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dxMm = EditorDesenhoService.Mm(e.HorizontalChange);
        var novaLarg = _elemento.Largura + dxMm;

        if (novaLarg >= 2.0)
        {
            _elemento.Largura = Math.Round(novaLarg, 1);
            SincronizarVisual();
        }
    }

    // =========================================================================
    // MOTOR DE TRANSFORMAÇÃO VETORIAL COREL (8 PONTOS, MODIFICADORES & FONTE REAL)
    // =========================================================================
    private void Transformar(
        double dxMm, double dyMm,
        bool ajustaEsquerda, bool ajustaDireita,
        bool ajustaTopo, bool ajustaBase)
    {
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        // Imagens que preservam proporção nativa acionam comportamento proporcional
        if (_elemento.Tipo == TipoElemento.Imagem && _elemento.ManterProporcao)
        {
            shift = true;
        }

        double novoX = _elemento.X;
        double novoY = _elemento.Y;
        double novaLarg = _elemento.Largura;
        double novaAlt = _elemento.Altura;

        if (ajustaEsquerda)
        {
            novaLarg = _elemento.Largura - dxMm;
            novoX = _elemento.X + dxMm;
        }
        else if (ajustaDireita)
        {
            novaLarg = _elemento.Largura + dxMm;
        }

        if (ajustaTopo)
        {
            novaAlt = _elemento.Altura - dyMm;
            novoY = _elemento.Y + dyMm;
        }
        else if (ajustaBase)
        {
            novaAlt = _elemento.Altura + dyMm;
        }

        // Regra 1: SHIFT mantém a proporção do objeto
        if (shift && (ajustaEsquerda || ajustaDireita) && (ajustaTopo || ajustaBase))
        {
            var proporcao = _aspectRatio > 0 ? _aspectRatio : 1.0;
            if (Math.Abs(dxMm) >= Math.Abs(dyMm))
            {
                var altAjustada = novaLarg / proporcao;
                if (ajustaTopo) novoY = _elemento.Y + (_elemento.Altura - altAjustada);
                novaAlt = altAjustada;
            }
            else
            {
                var largAjustada = novaAlt * proporcao;
                if (ajustaEsquerda) novoX = _elemento.X + (_elemento.Largura - largAjustada);
                novaLarg = largAjustada;
            }
        }
        else if (shift && (ajustaEsquerda || ajustaDireita) && !(ajustaTopo || ajustaBase))
        {
            var proporcao = _aspectRatio > 0 ? _aspectRatio : 1.0;
            var diffAlt = (novaLarg / proporcao) - _elemento.Altura;
            novaAlt = novaLarg / proporcao;
            novoY = _elemento.Y - (diffAlt / 2.0);
        }
        else if (shift && (ajustaTopo || ajustaBase) && !(ajustaEsquerda || ajustaDireita))
        {
            var proporcao = _aspectRatio > 0 ? _aspectRatio : 1.0;
            var diffLarg = (novaAlt * proporcao) - _elemento.Largura;
            novaLarg = novaAlt * proporcao;
            novoX = _elemento.X - (diffLarg / 2.0);
        }

        // Regra 2: CTRL força restrição quadrada 1:1
        if (ctrl)
        {
            var lado = Math.Max(novaLarg, novaAlt);
            if (ajustaEsquerda) novoX = _elemento.X + (_elemento.Largura - lado);
            if (ajustaTopo) novoY = _elemento.Y + (_elemento.Altura - lado);
            novaLarg = lado;
            novaAlt = lado;
        }

        // Regra 3: ALT redimensiona simetricamente a partir do centro
        if (alt)
        {
            novoX = _centroX - (novaLarg / 2.0);
            novoY = _centroY - (novaAlt / 2.0);
        }

        // Dimensões mínimas operacionais
        if (novaLarg < 2.0)
        {
            novaLarg = 2.0;
            novoX = _elemento.X;
        }
        if (novaAlt < 1.5)
        {
            novaAlt = 1.5;
            novoY = _elemento.Y;
        }

        _elemento.X = Math.Round(novoX, 1);
        _elemento.Y = Math.Round(novoY, 1);
        _elemento.Largura = Math.Round(novaLarg, 1);
        _elemento.Altura = Math.Round(novaAlt, 1);

        // =========================================================================
        // REDIMENSIONAMENTO REAL DA FONTE PARA TEXTO
        // =========================================================================
        if (_elemento.Tipo == TipoElemento.Texto)
        {
            double ratio;
            if ((ajustaTopo || ajustaBase) && (ajustaEsquerda || ajustaDireita))
            {
                ratio = _elemento.Altura / Math.Max(1.0, _origAlt);
            }
            else if (ajustaTopo || ajustaBase)
            {
                ratio = _elemento.Altura / Math.Max(1.0, _origAlt);
            }
            else
            {
                ratio = _elemento.Largura / Math.Max(1.0, _origLarg);
            }

            var novoTamanhoFonte = Math.Max(4.0, Math.Round(_origFonte * ratio, 1));
            _elemento.TamanhoFonte = novoTamanhoFonte;
        }

        SincronizarVisual();
    }

    private void TopoEsquerda_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dx = EditorDesenhoService.Mm(e.HorizontalChange);
        var dy = EditorDesenhoService.Mm(e.VerticalChange);
        Transformar(dx, dy, ajustaEsquerda: true, ajustaDireita: false, ajustaTopo: true, ajustaBase: false);
    }

    private void TopoMeio_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dy = EditorDesenhoService.Mm(e.VerticalChange);
        Transformar(0, dy, ajustaEsquerda: false, ajustaDireita: false, ajustaTopo: true, ajustaBase: false);
    }

    private void TopoDireita_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dx = EditorDesenhoService.Mm(e.HorizontalChange);
        var dy = EditorDesenhoService.Mm(e.VerticalChange);
        Transformar(dx, dy, ajustaEsquerda: false, ajustaDireita: true, ajustaTopo: true, ajustaBase: false);
    }

    private void MeioEsquerda_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dx = EditorDesenhoService.Mm(e.HorizontalChange);
        Transformar(dx, 0, ajustaEsquerda: true, ajustaDireita: false, ajustaTopo: false, ajustaBase: false);
    }

    private void MeioDireita_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dx = EditorDesenhoService.Mm(e.HorizontalChange);
        Transformar(dx, 0, ajustaEsquerda: false, ajustaDireita: true, ajustaTopo: false, ajustaBase: false);
    }

    private void BaixoEsquerda_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dx = EditorDesenhoService.Mm(e.HorizontalChange);
        var dy = EditorDesenhoService.Mm(e.VerticalChange);
        Transformar(dx, dy, ajustaEsquerda: true, ajustaDireita: false, ajustaTopo: false, ajustaBase: true);
    }

    private void BaixoMeio_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dy = EditorDesenhoService.Mm(e.VerticalChange);
        Transformar(0, dy, ajustaEsquerda: false, ajustaDireita: false, ajustaTopo: false, ajustaBase: true);
    }

    private void BaixoDireita_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dx = EditorDesenhoService.Mm(e.HorizontalChange);
        var dy = EditorDesenhoService.Mm(e.VerticalChange);
        Transformar(dx, dy, ajustaEsquerda: false, ajustaDireita: true, ajustaTopo: false, ajustaBase: true);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.2)
        {
            DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
        };

        if (_ehLinha)
        {
            var yMid = RenderSize.Height / 2.0;
            dc.DrawLine(pen, new Point(0, yMid), new Point(RenderSize.Width, yMid));
        }
        else
        {
            var rect = new Rect(new Point(0, 0), RenderSize);
            dc.DrawRectangle(null, pen, rect);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var metade = 4.0;
        var tam = 8.0;

        if (_ehLinha)
        {
            var yCenter = (finalSize.Height / 2.0) - metade;
            _linhaPontoInicial?.Arrange(new Rect(-metade, yCenter, tam, tam));
            _linhaPontoFinal?.Arrange(new Rect(finalSize.Width - metade, yCenter, tam, tam));
        }
        else
        {
            _topoEsquerda?.Arrange(new Rect(-metade, -metade, tam, tam));
            _topoMeio?.Arrange(new Rect((finalSize.Width / 2.0) - metade, -metade, tam, tam));
            _topoDireita?.Arrange(new Rect(finalSize.Width - metade, -metade, tam, tam));

            _meioEsquerda?.Arrange(new Rect(-metade, (finalSize.Height / 2.0) - metade, tam, tam));
            _meioDireita?.Arrange(new Rect(finalSize.Width - metade, (finalSize.Height / 2.0) - metade, tam, tam));

            _baixoEsquerda?.Arrange(new Rect(-metade, finalSize.Height - metade, tam, tam));
            _baixoMeio?.Arrange(new Rect((finalSize.Width / 2.0) - metade, finalSize.Height - metade, tam, tam));
            _baixoDireita?.Arrange(new Rect(finalSize.Width - metade, finalSize.Height - metade, tam, tam));
        }

        return finalSize;
    }

    protected override int VisualChildrenCount => _visuais.Count;
    protected override Visual GetVisualChild(int index) => _visuais[index];
}
