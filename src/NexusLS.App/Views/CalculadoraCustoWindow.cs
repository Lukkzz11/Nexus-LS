using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>
/// Calculadora de custo (parecida com a do Windows) para calcular o preço de uma compra, somar juros e outros valores
/// (frete etc.) e devolver o total como custo por unidade. Modal; teclado numérico e digitação funcionam.
/// </summary>
public sealed class CalculadoraCustoWindow : Window
{
    private readonly CalculadoraCusto _calc = new();
    private readonly TextBlock _visor = new() { FontSize = 34, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _juros = new() { Width = 70, Padding = new Thickness(6) };
    private readonly TextBox _descricao = new() { Padding = new Thickness(6) };
    private readonly TextBox _valor = new() { Width = 100, Padding = new Thickness(6) };
    private readonly ListBox _lista = new() { MinHeight = 90, MaxHeight = 160 };
    private readonly TextBlock _total = new() { FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _aviso = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>Custo por unidade escolhido, em centavos (nulo se o operador fechou sem usar).</summary>
    public long? CustoCentavos { get; private set; }

    public CalculadoraCustoWindow()
    {
        Title = "Calculadora de custo";
        Width = 720; Height = 600; MinWidth = 640; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.TryFindResource("CorFundo") is Brush fundo) Background = fundo;
        if (Application.Current.TryFindResource("CorTexto") is Brush texto) Foreground = texto;

        var raiz = new Grid { Margin = new Thickness(16) };
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ----- coluna da calculadora -----
        var esq = new Grid();
        esq.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
        esq.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var visorBorda = new Border { Child = _visor, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        visorBorda.BorderBrush = Application.Current.TryFindResource("CorBorda") as Brush ?? Brushes.Gray;
        if (Application.Current.TryFindResource("CorPainel") is Brush painel) visorBorda.Background = painel;
        esq.Children.Add(visorBorda);

        var teclado = new UniformGrid { Columns = 4, Rows = 5 };
        Grid.SetRow(teclado, 1);
        void Tecla(string t, Action acao, bool destaque = false)
        {
            var b = new Button { Content = t, FontSize = 18, Margin = new Thickness(3), Focusable = false };
            if (Application.Current.TryFindResource(destaque ? "BotaoPrimario" : "BotaoSecundario") is Style s) b.Style = s;
            b.Click += (_, _) => { acao(); Atualizar(); };
            teclado.Children.Add(b);
        }
        Tecla("C", _calc.Limpar); Tecla("⌫", _calc.Apagar); Tecla("÷", () => _calc.Operador('/')); Tecla("×", () => _calc.Operador('*'));
        foreach (var linha in new[] { new[] { 7, 8, 9 }, new[] { 4, 5, 6 }, new[] { 1, 2, 3 } })
        {
            foreach (var d in linha) { var dd = d; Tecla(d.ToString(), () => _calc.Digito(dd)); }
            Tecla(linha[0] == 7 ? "−" : linha[0] == 4 ? "+" : "=", linha[0] == 7 ? () => _calc.Operador('-') : linha[0] == 4 ? () => _calc.Operador('+') : _calc.Igual, linha[0] == 1);
        }
        Tecla("0", () => _calc.Digito(0)); Tecla(",", _calc.Virgula);
        esq.Children.Add(teclado);
        raiz.Children.Add(esq);

        // ----- coluna de juros, acréscimos e total -----
        var dir = new StackPanel();
        Grid.SetColumn(dir, 2);
        dir.Children.Add(new TextBlock { Text = "Preço da compra", FontWeight = FontWeights.Bold });
        dir.Children.Add(new TextBlock { Text = "Calcule no teclado ao lado; o valor do visor é o preço base.", Opacity = .7, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12) });

        var linhaJuros = new StackPanel { Orientation = Orientation.Horizontal };
        linhaJuros.Children.Add(new TextBlock { Text = "Juros (%)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        linhaJuros.Children.Add(_juros);
        linhaJuros.Children.Add(Botao("Adicionar juros", (_, _) => AdicionarJuros(), false));
        dir.Children.Add(linhaJuros);
        dir.Children.Add(new TextBlock { Text = "Os juros incidem sobre o valor atual (preço + acréscimos já lançados).", Opacity = .7, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

        dir.Children.Add(new TextBlock { Text = "Outros valores (frete, taxas...)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var linhaValor = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var add = Botao("Adicionar", (_, _) => AdicionarValor(), false);
        DockPanel.SetDock(add, Dock.Right);
        DockPanel.SetDock(_valor, Dock.Right);
        _valor.Margin = new Thickness(6, 0, 6, 0);
        linhaValor.Children.Add(add); linhaValor.Children.Add(_valor); linhaValor.Children.Add(_descricao);
        dir.Children.Add(linhaValor);
        dir.Children.Add(new TextBlock { Text = "Descrição e valor (R$)", Opacity = .7, FontSize = 11.5, Margin = new Thickness(0, -4, 0, 8) });

        dir.Children.Add(_lista);
        dir.Children.Add(Botao("Remover item selecionado", (_, _) => { if (_lista.SelectedIndex >= 0) { _calc.RemoverAcrescimo(_lista.SelectedIndex); Atualizar(); } }, false));
        dir.Children.Add(_total);
        dir.Children.Add(_aviso);

        var rodape = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        rodape.Children.Add(Botao("Usar como custo por unidade", (_, _) => Usar(), true));
        rodape.Children.Add(Botao("Cancelar", (_, _) => Close(), false));
        dir.Children.Add(rodape);
        raiz.Children.Add(dir);

        Content = raiz;
        PreviewKeyDown += AoTeclar;
        Loaded += (_, _) => { Focus(); Atualizar(); };
    }

    private static Button Botao(string texto, RoutedEventHandler clique, bool primario)
    {
        var b = new Button { Content = texto, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 4, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        if (Application.Current.TryFindResource(primario ? "BotaoPrimario" : "BotaoSecundario") is Style estilo) b.Style = estilo;
        b.Click += clique;
        return b;
    }

    /// <summary>Teclado direto na calculadora, a menos que o foco esteja em um dos campos de texto.</summary>
    private void AoTeclar(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var k = e.Key;
        if (k >= Key.D0 && k <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None) _calc.Digito(k - Key.D0);
        else if (k >= Key.NumPad0 && k <= Key.NumPad9) _calc.Digito(k - Key.NumPad0);
        else if (k is Key.OemComma or Key.OemPeriod or Key.Decimal) _calc.Virgula();
        else if (k == Key.Add || (k == Key.OemPlus && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) _calc.Operador('+');
        else if (k is Key.Subtract or Key.OemMinus) _calc.Operador('-');
        else if (k == Key.Multiply || (k == Key.D8 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) _calc.Operador('*');
        else if (k is Key.Divide or Key.OemQuestion) _calc.Operador('/');
        else if (k is Key.Enter or Key.OemPlus) _calc.Igual();
        else if (k == Key.Back) _calc.Apagar();
        else if (k is Key.Escape) { Close(); return; }
        else if (k == Key.Delete) _calc.Limpar();
        else return;
        e.Handled = true;
        Atualizar();
    }

    private void Atualizar()
    {
        _visor.Text = _calc.Visor;
        _lista.ItemsSource = _calc.Acrescimos.Select(a => $"{a.Descricao}  —  {Dinheiro.Formatar(a.Centavos)}").ToList();
        _total.Text = $"Total: {Dinheiro.Formatar(_calc.TotalCentavos)}";
    }

    private void Avisar(string msg, bool erro)
    {
        _aviso.Text = msg;
        _aviso.Foreground = erro ? Brushes.IndianRed : (Application.Current.TryFindResource("CorTexto") as Brush ?? Brushes.Gray);
    }

    private void AdicionarJuros()
    {
        try
        {
            if (!decimal.TryParse(_juros.Text.Replace("%", "").Trim(), NumberStyles.AllowDecimalPoint, new CultureInfo("pt-BR"), out var p))
                throw new RegraDeNegocioException("Informe os juros em %, por exemplo 5 ou 2,5.");
            _calc.AdicionarJuros(p);
            _juros.Clear(); Avisar("", false); Atualizar();
        }
        catch (RegraDeNegocioException ex) { Avisar(ex.Message, true); }
    }

    private void AdicionarValor()
    {
        try
        {
            if (!Dinheiro.TentarConverter(_valor.Text, out var c))
                throw new RegraDeNegocioException("Informe o valor em reais, por exemplo 25,00.");
            _calc.AdicionarValor(_descricao.Text, c);
            _descricao.Clear(); _valor.Clear(); Avisar("", false); Atualizar();
        }
        catch (RegraDeNegocioException ex) { Avisar(ex.Message, true); }
    }

    private void Usar()
    {
        if (_calc.TotalCentavos <= 0) { Avisar("O total está zerado. Calcule o preço da compra primeiro.", true); return; }
        CustoCentavos = _calc.TotalCentavos;
        DialogResult = true;
    }
}
