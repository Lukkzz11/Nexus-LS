using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;

namespace NexusLS.BarTender.Views.Designer;

public class ElementoAdorner : Adorner
{
    private readonly Thumb _topoEsquerda;
    private readonly Thumb _topoDireita;
    private readonly Thumb _baixoEsquerda;
    private readonly Thumb _baixoDireita;
    private readonly VisualCollection _visuais;
    private readonly ElementoDesign _elemento;

    public ElementoAdorner(UIElement elementoAdornado, ElementoDesign elemento) : base(elementoAdornado)
    {
        _elemento = elemento;
        _visuais = new VisualCollection(this);

        _topoEsquerda = CriarThumb(Cursors.SizeNWSE);
        _topoDireita = CriarThumb(Cursors.SizeNESW);
        _baixoEsquerda = CriarThumb(Cursors.SizeNESW);
        _baixoDireita = CriarThumb(Cursors.SizeNWSE);

        _topoEsquerda.DragDelta += TopoEsquerda_DragDelta;
        _topoDireita.DragDelta += TopoDireita_DragDelta;
        _baixoEsquerda.DragDelta += BaixoEsquerda_DragDelta;
        _baixoDireita.DragDelta += BaixoDireita_DragDelta;

        _visuais.Add(_topoEsquerda);
        _visuais.Add(_topoDireita);
        _visuais.Add(_baixoEsquerda);
        _visuais.Add(_baixoDireita);
    }

    private static Thumb CriarThumb(Cursor cursor)
    {
        return new Thumb
        {
            Cursor = cursor,
            Width = 9,
            Height = 9,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), // Nexus Blue #2563EB
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5)
        };
    }

    private void TopoEsquerda_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dxMm = EditorDesenhoService.Mm(e.HorizontalChange);
        var dyMm = EditorDesenhoService.Mm(e.VerticalChange);

        var novoX = _elemento.X + dxMm;
        var novoY = _elemento.Y + dyMm;
        var novaLarg = _elemento.Largura - dxMm;
        var novaAlt = _elemento.Altura - dyMm;

        if (novaLarg >= 4 && novoX >= 0)
        {
            _elemento.X = Math.Round(novoX, 1);
            _elemento.Largura = Math.Round(novaLarg, 1);
        }

        if (novaAlt >= 2 && novoY >= 0)
        {
            _elemento.Y = Math.Round(novoY, 1);
            _elemento.Altura = Math.Round(novaAlt, 1);
        }
    }

    private void TopoDireita_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dxMm = EditorDesenhoService.Mm(e.HorizontalChange);
        var dyMm = EditorDesenhoService.Mm(e.VerticalChange);

        var novoY = _elemento.Y + dyMm;
        var novaLarg = _elemento.Largura + dxMm;
        var novaAlt = _elemento.Altura - dyMm;

        if (novaLarg >= 4)
        {
            _elemento.Largura = Math.Round(novaLarg, 1);
        }

        if (novaAlt >= 2 && novoY >= 0)
        {
            _elemento.Y = Math.Round(novoY, 1);
            _elemento.Altura = Math.Round(novaAlt, 1);
        }
    }

    private void BaixoEsquerda_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dxMm = EditorDesenhoService.Mm(e.HorizontalChange);
        var dyMm = EditorDesenhoService.Mm(e.VerticalChange);

        var novoX = _elemento.X + dxMm;
        var novaLarg = _elemento.Largura - dxMm;
        var novaAlt = _elemento.Altura + dyMm;

        if (novaLarg >= 4 && novoX >= 0)
        {
            _elemento.X = Math.Round(novoX, 1);
            _elemento.Largura = Math.Round(novaLarg, 1);
        }

        if (novaAlt >= 2)
        {
            _elemento.Altura = Math.Round(novaAlt, 1);
        }
    }

    private void BaixoDireita_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var dxMm = EditorDesenhoService.Mm(e.HorizontalChange);
        var dyMm = EditorDesenhoService.Mm(e.VerticalChange);

        var novaLarg = _elemento.Largura + dxMm;
        var novaAlt = _elemento.Altura + dyMm;

        if (novaLarg >= 4)
        {
            _elemento.Largura = Math.Round(novaLarg, 1);
        }

        if (novaAlt >= 2)
        {
            _elemento.Altura = Math.Round(novaAlt, 1);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var rect = new Rect(new Point(0, 0), RenderSize);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.5)
        {
            DashStyle = DashStyles.Dash
        };
        dc.DrawRectangle(null, pen, rect);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var metade = 4.5;
        _topoEsquerda.Arrange(new Rect(-metade, -metade, 9, 9));
        _topoDireita.Arrange(new Rect(finalSize.Width - metade, -metade, 9, 9));
        _baixoEsquerda.Arrange(new Rect(-metade, finalSize.Height - metade, 9, 9));
        _baixoDireita.Arrange(new Rect(finalSize.Width - metade, finalSize.Height - metade, 9, 9));
        return finalSize;
    }

    protected override int VisualChildrenCount => _visuais.Count;
    protected override Visual GetVisualChild(int index) => _visuais[index];
}
