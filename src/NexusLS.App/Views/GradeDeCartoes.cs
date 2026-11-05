using System.Windows;
using System.Windows.Controls;

namespace NexusLS.App.Views;

/// <summary>
/// Painel só de layout: reparte os cartões em colunas que ocupam a largura toda. Quantas colunas cabem depende da
/// largura disponível (cada coluna tem pelo menos <see cref="LarguraMinima"/>), até <see cref="MaximoColunas"/> e nunca
/// mais colunas do que cartões visíveis. Assim a mesma tela fica em 1 coluna num monitor pequeno e em 2 ou 3 num monitor
/// largo, sem sobrar vazio à direita.
/// Com <see cref="IgualarAltura"/> os cartões ficam em linhas, todos da mesma altura na linha (bom para números lado a lado);
/// sem ele cada cartão entra na coluna mais curta (bom para formulários de alturas diferentes).
/// </summary>
public class GradeDeCartoes : Panel
{
    public static readonly DependencyProperty LarguraMinimaProperty = DependencyProperty.Register(nameof(LarguraMinima), typeof(double),
        typeof(GradeDeCartoes), new FrameworkPropertyMetadata(360.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaximoColunasProperty = DependencyProperty.Register(nameof(MaximoColunas), typeof(int),
        typeof(GradeDeCartoes), new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty EspacoProperty = DependencyProperty.Register(nameof(Espaco), typeof(double),
        typeof(GradeDeCartoes), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty IgualarAlturaProperty = DependencyProperty.Register(nameof(IgualarAltura), typeof(bool),
        typeof(GradeDeCartoes), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Largura mínima de cada coluna; abaixo disso o painel usa menos colunas.</summary>
    public double LarguraMinima { get => (double)GetValue(LarguraMinimaProperty); set => SetValue(LarguraMinimaProperty, value); }

    public int MaximoColunas { get => (int)GetValue(MaximoColunasProperty); set => SetValue(MaximoColunasProperty, value); }

    /// <summary>Espaço entre os cartões, na horizontal e na vertical.</summary>
    public double Espaco { get => (double)GetValue(EspacoProperty); set => SetValue(EspacoProperty, value); }

    public bool IgualarAltura { get => (bool)GetValue(IgualarAlturaProperty); set => SetValue(IgualarAlturaProperty, value); }

    /// <summary>Quantas colunas cabem em <paramref name="largura"/> (função pura, testada).</summary>
    public static int ColunasPara(double largura, double larguraMinima, double espaco, int maximo, int visiveis)
    {
        if (visiveis <= 0) return 1;
        var limite = Math.Max(1, Math.Min(maximo, visiveis));
        if (double.IsInfinity(largura) || double.IsNaN(largura)) return limite;
        var cabem = (int)Math.Floor((largura + espaco) / (Math.Max(1, larguraMinima) + espaco));
        return Math.Clamp(cabem, 1, limite);
    }

    private List<UIElement> Visiveis() =>
        InternalChildren.Cast<UIElement>().Where(c => c is not null && c.Visibility != Visibility.Collapsed).ToList();

    private (int colunas, double larguraColuna) Colunas(double largura, int visiveis)
    {
        var n = ColunasPara(largura, LarguraMinima, Espaco, MaximoColunas, visiveis);
        var w = double.IsInfinity(largura) ? LarguraMinima : Math.Max(0, (largura - Espaco * (n - 1)) / n);
        return (n, w);
    }

    protected override Size MeasureOverride(Size disponivel)
    {
        foreach (UIElement c in InternalChildren)
            if (c is not null && c.Visibility == Visibility.Collapsed) c.Measure(new Size(0, 0));
        var filhos = Visiveis();
        var (n, w) = Colunas(disponivel.Width, filhos.Count);
        foreach (var c in filhos) c.Measure(new Size(w, double.PositiveInfinity));

        var altura = Posicionar(filhos, n, w, (_, _) => { });
        var largura = double.IsInfinity(disponivel.Width) ? n * w + Espaco * (n - 1) : disponivel.Width;
        return new Size(largura, altura);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var filhos = Visiveis();
        var (n, w) = Colunas(final.Width, filhos.Count);
        // se a largura final for outra que a medida (o WPF ignora a chamada quando é a mesma), mede de novo na coluna certa
        foreach (var c in filhos) c.Measure(new Size(w, double.PositiveInfinity));
        Posicionar(filhos, n, w, (c, r) => c.Arrange(r));
        return final;
    }

    /// <summary>Calcula onde cada cartão fica (e a altura total), chamando <paramref name="colocar"/> para cada um.</summary>
    private double Posicionar(List<UIElement> filhos, int n, double w, Action<UIElement, Rect> colocar)
    {
        if (filhos.Count == 0) return 0;
        if (IgualarAltura)
        {
            double y = 0;
            for (var inicio = 0; inicio < filhos.Count; inicio += n)
            {
                var linha = filhos.Skip(inicio).Take(n).ToList();
                var h = linha.Max(c => c.DesiredSize.Height);
                for (var i = 0; i < linha.Count; i++) colocar(linha[i], new Rect(i * (w + Espaco), y, w, h));
                y += h + Espaco;
            }
            return y - Espaco;
        }

        var alturas = new double[n];
        foreach (var c in filhos)
        {
            var col = Array.IndexOf(alturas, alturas.Min());
            var y = alturas[col] == 0 ? 0 : alturas[col] + Espaco;
            colocar(c, new Rect(col * (w + Espaco), y, w, c.DesiredSize.Height));
            alturas[col] = y + c.DesiredSize.Height;
        }
        return alturas.Max();
    }
}
