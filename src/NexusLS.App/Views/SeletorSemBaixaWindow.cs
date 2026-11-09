using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>
/// Caixa para escolher, clicando um a um, quais produtos vendem sem baixa de estoque. Marque os produtos (a busca ajuda a achar)
/// e use "Vender sem baixa" ou "Contar estoque" para aplicar nos marcados.
/// </summary>
public sealed class SeletorSemBaixaWindow : Window
{
    private sealed class Item : INotifyPropertyChanged
    {
        public Item(Produto p) => Produto = p;
        public Produto Produto { get; }
        private bool _marcado;
        public bool Marcado { get => _marcado; set { _marcado = value; Mudou(nameof(Marcado)); } }
        public string Texto => $"{(string.IsNullOrWhiteSpace(Produto.Referencia) ? "—" : Produto.Referencia)}  ·  {Produto.Nome}"
                               + (Produto.Categoria is { Length: > 0 } ? $"  ({Produto.Categoria})" : "")
                               + (Produto.VenderSemBaixa ? "   [sem baixa]" : "");
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Mudou(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private readonly ProdutoService _produtos;
    private readonly ObservableCollection<Item> _itens = new();
    private readonly HashSet<int> _marcadosIds = new();
    private readonly TextBox _busca = new() { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6), ToolTip = "Buscar por nome, referência ou categoria" };
    private readonly TextBlock _aviso = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Opacity = .8 };
    private readonly TextBlock _contagem = new() { Margin = new Thickness(0, 0, 0, 6), Opacity = .8 };
    private int _versao;

    public SeletorSemBaixaWindow(ProdutoService produtos)
    {
        _produtos = produtos;
        Title = "Escolher produtos — vender sem baixa de estoque";
        Width = 640; Height = 620; MinWidth = 480; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.TryFindResource("CorFundo") is Brush fundo) Background = fundo;
        if (Application.Current.TryFindResource("CorTexto") is Brush texto) Foreground = texto;

        var lista = new ListBox { ItemsSource = _itens, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var modelo = new FrameworkElementFactory(typeof(CheckBox));
        modelo.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(Item.Marcado)) { Mode = BindingMode.TwoWay });
        modelo.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Item.Texto)));
        modelo.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 5, 4, 5));
        modelo.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler(AoMarcar));
        modelo.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler(AoMarcar));
        lista.ItemTemplate = new DataTemplate { VisualTree = modelo };

        Button Botao(string texto, RoutedEventHandler clique, string dica)
        {
            var b = new Button { Content = texto, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), ToolTip = dica };
            if (Application.Current.TryFindResource("BotaoSecundario") is Style estilo) b.Style = estilo;
            b.Click += clique;
            return b;
        }

        var selecao = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        selecao.Children.Add(Botao("Marcar os da busca", (_, _) => MarcarTodos(true), "Marca todos os produtos que aparecem agora na lista."));
        selecao.Children.Add(Botao("Limpar marcados", (_, _) => LimparTudo(), "Desmarca tudo."));

        var acoes = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        acoes.Children.Add(Botao("Vender sem baixa", async (_, _) => await AplicarAsync(true), "Os marcados passam a vender sem conferir nem baixar estoque."));
        acoes.Children.Add(Botao("Contar estoque", async (_, _) => await AplicarAsync(false), "Os marcados voltam a conferir e baixar estoque."));
        acoes.Children.Add(Botao("Fechar", (_, _) => Close(), "Fecha esta caixa."));

        var raiz = new DockPanel { Margin = new Thickness(16) };
        var topo = new StackPanel();
        topo.Children.Add(new TextBlock { Text = "Marque os produtos e depois escolha o que fazer com eles.", Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
        topo.Children.Add(_busca);
        topo.Children.Add(selecao);
        topo.Children.Add(_contagem);
        DockPanel.SetDock(topo, Dock.Top);
        var base_ = new StackPanel();
        base_.Children.Add(acoes);
        base_.Children.Add(_aviso);
        DockPanel.SetDock(base_, Dock.Bottom);
        raiz.Children.Add(topo);
        raiz.Children.Add(base_);
        raiz.Children.Add(lista);
        Content = raiz;

        _busca.TextChanged += async (_, _) => await CarregarAsync();
        Loaded += async (_, _) => { await CarregarAsync(); _busca.Focus(); };
    }

    private void AoMarcar(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Item item)
        {
            if (item.Marcado) _marcadosIds.Add(item.Produto.Id); else _marcadosIds.Remove(item.Produto.Id);
            AtualizarContagem();
        }
    }

    private async Task CarregarAsync()
    {
        var versao = ++_versao;
        var lista = await _produtos.BuscarAsync(_busca.Text);
        if (versao != _versao) return;
        _itens.Clear();
        foreach (var p in lista) _itens.Add(new Item(p) { Marcado = _marcadosIds.Contains(p.Id) });
        AtualizarContagem();
    }

    private void AtualizarContagem() => _contagem.Text = $"{_marcadosIds.Count} marcado(s) · {_itens.Count} na lista";

    private void MarcarTodos(bool valor)
    {
        foreach (var i in _itens) i.Marcado = valor;
        foreach (var i in _itens) { if (valor) _marcadosIds.Add(i.Produto.Id); else _marcadosIds.Remove(i.Produto.Id); }
        AtualizarContagem();
    }

    private void LimparTudo()
    {
        _marcadosIds.Clear();
        foreach (var i in _itens) i.Marcado = false;
        AtualizarContagem();
    }

    private async Task AplicarAsync(bool venderSemBaixa)
    {
        if (_marcadosIds.Count == 0) { _aviso.Text = "Marque pelo menos um produto."; return; }
        var texto = venderSemBaixa ? "vender sem baixa de estoque" : "contar estoque normalmente";
        if (MessageBox.Show(this, $"Fazer {_marcadosIds.Count} produto(s) {texto}?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            var alterados = await _produtos.DefinirVenderSemBaixaAsync(_marcadosIds.ToList(), venderSemBaixa);
            _aviso.Text = $"Pronto: {alterados} produto(s) alterado(s). Você pode marcar outros.";
            _marcadosIds.Clear();
            await CarregarAsync();
        }
        catch (RegraDeNegocioException ex) { _aviso.Text = ex.Message; }
    }
}
