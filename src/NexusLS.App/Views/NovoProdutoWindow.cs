using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>
/// Tela de cadastro de produto novo (no estilo do ERP): identificação e preços, códigos e tamanhos com a quantidade de cada um.
/// Abre a partir do Estoque da loja; ao salvar, o produto é criado com a grade e o estoque inicial de cada tamanho.
/// </summary>
public sealed class NovoProdutoWindow : Window
{
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly int _margemPadrao;

    private readonly TextBox _nome = new(), _custo = new(), _varejo = new(), _atacado = new(), _margem = new(), _minimo = new();
    private readonly TextBox _barras = new(), _referencia = new(), _base = new(), _cores = new();
    private readonly ComboBox _categoria = new() { IsEditable = true };
    private readonly CheckBox _semBaixa = new() { Content = "Vender sem baixa de estoque", Margin = new Thickness(0, 0, 18, 0) };
    private readonly CheckBox _sobra = new() { Content = "É sobra (fica fora do catálogo online)" };
    private readonly TextBlock _aviso = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _total = new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _gradeUi = new();
    private readonly TextBox _unico = new() { Width = 90, Padding = new Thickness(6) };
    private readonly Dictionary<string, TextBox> _quantidades = new();
    private readonly List<RadioButton> _abas = new();
    private HashSet<string> _copiar = new();
    private string _aba = "Letras";

    /// <summary>O produto criado (nulo se o operador fechou sem salvar).</summary>
    public Produto? Criado { get; private set; }

    public NovoProdutoWindow(ProdutoService produtos, EstoqueService estoque, int margemPadrao, int minimoPadrao, bool sobra)
    {
        _produtos = produtos; _estoque = estoque; _margemPadrao = margemPadrao;
        Title = sobra ? "Nova sobra" : "Novo produto";
        Width = 1000; Height = 780; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.TryFindResource("CorFundo") is Brush fundo) Background = fundo;
        if (Application.Current.TryFindResource("CorTexto") is Brush texto) Foreground = texto;

        _sobra.IsChecked = sobra;
        _minimo.Text = minimoPadrao.ToString(CultureInfo.InvariantCulture);
        _margem.Text = margemPadrao > 0 ? margemPadrao.ToString(CultureInfo.InvariantCulture) : "";
        _margem.Width = 70;
        _categoria.ItemsSource = ModelosDeReferencia.Todos.Select(m => m.Categoria).ToList();
        _ = CompletarCategoriasAsync();
        foreach (var t in new[] { _nome, _custo, _varejo, _atacado, _margem, _minimo, _barras, _referencia, _base, _cores })
            t.Padding = new Thickness(6);
        _categoria.Padding = new Thickness(6);

        var raiz = new Grid { Margin = new Thickness(18) };
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var voltar = Botao("← Voltar", (_, _) => Close(), false);
        DockPanel.SetDock(voltar, Dock.Right);
        topo.Children.Add(voltar);
        topo.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        raiz.Children.Add(topo);

        var pilha = new StackPanel();
        pilha.Children.Add(Cartao("Identificação e preços", MontarIdentificacao()));
        pilha.Children.Add(Cartao("Códigos", MontarCodigos()));
        pilha.Children.Add(Cartao("Tamanhos, cores e quantidades", MontarTamanhos()));
        var rolagem = new ScrollViewer { Content = pilha, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(rolagem, 1);
        raiz.Children.Add(rolagem);

        var rodape = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal };
        botoes.Children.Add(Botao("Salvar produto", async (_, _) => await SalvarAsync(), true));
        botoes.Children.Add(Botao("Cancelar", (_, _) => Close(), false));
        rodape.Children.Add(botoes);
        rodape.Children.Add(_aviso);
        Grid.SetRow(rodape, 2);
        raiz.Children.Add(rodape);
        Content = raiz;

        MostrarAba("Letras");
        Loaded += (_, _) => _nome.Focus();
    }

    // ----- montagem da tela -----

    private Button Botao(string texto, RoutedEventHandler clique, bool primario)
    {
        var b = new Button { Content = texto, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current.TryFindResource(primario ? "BotaoPrimario" : "BotaoSecundario") is Style estilo) b.Style = estilo;
        b.Click += clique;
        return b;
    }

    private static UIElement Rotulado(string rotulo, Control campo, string? ajuda = null)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
        p.Children.Add(new TextBlock { Text = rotulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        p.Children.Add(campo);
        if (ajuda is not null) p.Children.Add(new TextBlock { Text = ajuda, Opacity = .7, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        return p;
    }

    private static Border Cartao(string titulo, UIElement conteudo)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = titulo, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        p.Children.Add(conteudo);
        var b = new Border { Child = p, Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        b.BorderBrush = Application.Current.TryFindResource("CorBorda") as Brush ?? Brushes.Gray;
        if (Application.Current.TryFindResource("CorPainel") is Brush painel) b.Background = painel;
        return b;
    }

    private static Grid Colunas(params (UIElement Item, double Largura)[] itens)
    {
        var g = new Grid();
        for (var i = 0; i < itens.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(itens[i].Largura, GridUnitType.Star) });
            Grid.SetColumn(itens[i].Item, i);
            g.Children.Add(itens[i].Item);
        }
        return g;
    }

    private UIElement MontarIdentificacao()
    {
        var p = new StackPanel();
        p.Children.Add(Colunas((Rotulado("Nome do produto *", _nome), 1.4), (Rotulado("Categoria", _categoria, "Define o modelo da referência (bermuda 14.., calça 15..)."), 1)));
        p.Children.Add(Colunas(
            (Rotulado("Custo por unidade (R$)", _custo, "Opcional. Alimenta o lucro nos relatórios."), 1),
            (Rotulado("Preço de varejo (R$) *", _varejo), 1),
            (Rotulado("Preço de atacado (R$)", _atacado, "Opcional. Usado nas vendas de atacado."), 1)));
        var calc = new StackPanel { Orientation = Orientation.Horizontal };
        calc.Children.Add(Botao("Calcular preço de venda", (_, _) => CalcularPreco(), false));
        calc.Children.Add(new TextBlock { Text = "Margem (%)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        calc.Children.Add(_margem);
        p.Children.Add(calc);
        return p;
    }

    private UIElement MontarCodigos()
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = "Use o código de barras da etiqueta do fabricante quando existir (digite ou passe o leitor). Sem ele, gere um código pelo modelo.",
            Opacity = .8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var gerar = Botao("Gerar código de barras", async (_, _) => await GerarCodigoAsync(), false);
        var esq = new StackPanel(); esq.Children.Add(Rotulado("Código de barras", _barras)); esq.Children.Add(gerar);
        p.Children.Add(Colunas((esq, 1), (Rotulado("Referência / código interno", _referencia, "Se deixar vazio e não houver código de barras, o código interno é criado ao salvar."), 1)));

        var baseLinha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _base.Width = 140;
        baseLinha.Children.Add(new TextBlock { Text = "Copiar de uma referência existente:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        baseLinha.Children.Add(_base);
        var copiar = Botao("Copiar da base", async (_, _) => await CopiarDaBaseAsync(), false);
        copiar.Margin = new Thickness(8, 0, 0, 0);
        baseLinha.Children.Add(copiar);
        p.Children.Add(baseLinha);
        return p;
    }

    private UIElement MontarTamanhos()
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = "Tudo opcional. O estoque guarda a quantidade por tamanho; as cores servem para identificar o produto.", Opacity = .8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        p.Children.Add(Rotulado("Cores (opcional, separadas por vírgula)", _cores));

        var abas = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (rotulo, chave) in new[] { ("Letras", "Letras"), ("Números", "Números"), ("Tamanho único", "Único") })
        {
            var r = new RadioButton { Content = rotulo, GroupName = "abas", Margin = new Thickness(0, 0, 14, 0), IsChecked = chave == "Letras", Tag = chave };
            if (Application.Current.TryFindResource("BotaoOpcao") is Style estilo) r.Style = estilo;
            r.Checked += (_, _) => MostrarAba(chave);
            _abas.Add(r);
            abas.Children.Add(r);
        }
        var cab = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        DockPanel.SetDock(_total, Dock.Right);
        cab.Children.Add(_total);
        cab.Children.Add(abas);
        p.Children.Add(cab);
        p.Children.Add(_gradeUi);

        var fim = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        _minimo.Width = 70;
        fim.Children.Add(new TextBlock { Text = "Estoque mínimo", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        fim.Children.Add(_minimo);
        fim.Children.Add(new Border { Width = 24 });
        fim.Children.Add(_semBaixa);
        fim.Children.Add(_sobra);
        p.Children.Add(fim);
        return p;
    }

    private void MostrarAba(string aba)
    {
        _aba = aba;
        _gradeUi.Children.Clear();
        _quantidades.Clear();
        if (aba == "Único")
        {
            _gradeUi.Children.Add(Rotulado("Quantidade em estoque (sem tamanho)", _unico));
            _unico.TextChanged -= AoMudarQuantidade; _unico.TextChanged += AoMudarQuantidade;
        }
        else
        {
            var tamanhos = aba == "Letras" ? GradesDeTamanho.Letras.Tamanhos : GradesDeTamanho.CalcaBermuda.Tamanhos;
            foreach (var t in tamanhos)
            {
                var caixa = new TextBox { Width = 70, Padding = new Thickness(6), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = $"Quantidade do tamanho {t}" };
                if (_copiar.Contains(t)) caixa.Text = "0";
                caixa.TextChanged += AoMudarQuantidade;
                _quantidades[t] = caixa;
                var celula = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
                celula.Children.Add(new TextBlock { Text = t, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) });
                celula.Children.Add(caixa);
                _gradeUi.Children.Add(celula);
            }
        }
        AtualizarTotal();
    }

    private void AoMudarQuantidade(object? s, TextChangedEventArgs e) => AtualizarTotal();

    private void AtualizarTotal() => _total.Text = $"Total: {SomaQuantidades()} peças";

    private static int Quantidade(TextBox t) => int.TryParse(t.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private int SomaQuantidades() => _aba == "Único" ? Quantidade(_unico) : _quantidades.Values.Sum(Quantidade);

    private async Task CompletarCategoriasAsync()
    {
        try
        {
            var usadas = await _produtos.ListarCategoriasAsync();
            var texto = _categoria.Text;
            var todas = ModelosDeReferencia.Todos.Select(m => m.Categoria).ToList();
            foreach (var c in usadas) if (!todas.Any(x => ModelosDeReferencia.Chave(x) == ModelosDeReferencia.Chave(c))) todas.Add(c);
            _categoria.ItemsSource = todas;
            _categoria.Text = texto;
        }
        catch (Exception) { /* a lista fixa basta */ }
    }

    // ----- ações -----

    private void CalcularPreco()
    {
        try
        {
            if (!Dinheiro.TentarConverter(_custo.Text, out var custo) || custo <= 0)
                throw new RegraDeNegocioException("Informe o custo por unidade para calcular o preço de venda.");
            if (!decimal.TryParse(_margem.Text.Replace("%", "").Trim(), NumberStyles.AllowDecimalPoint, new CultureInfo("pt-BR"), out var margem))
                throw new RegraDeNegocioException("Informe a margem desejada em %, por exemplo 40.");
            _varejo.Text = Dinheiro.FormatarParaEdicao(CalculadoraPreco.PrecoPorMargem(custo, margem));
            Avisar("Preço de varejo calculado. Confira antes de salvar.", false);
        }
        catch (RegraDeNegocioException ex) { Avisar(ex.Message, true); }
    }

    private async Task GerarCodigoAsync()
    {
        try
        {
            var codigo = await _produtos.SugerirReferenciaAsync(_categoria.Text);
            _barras.Text = codigo;
            _referencia.Text = codigo;
            var modelo = ModelosDeReferencia.PrefixoDa(_categoria.Text);
            Avisar(modelo is null && codigo.Length == 13
                ? "Categoria sem modelo: foi gerado um código interno de 13 dígitos. Escolha a categoria para gerar pela referência do modelo."
                : $"Código {codigo} gerado pelo modelo da categoria. Cada tamanho vai usar esse código + o número do tamanho (1, 2, 3...). Salve para reservá-lo.", false);
        }
        catch (RegraDeNegocioException ex) { Avisar(ex.Message, true); }
    }

    private async Task CopiarDaBaseAsync()
    {
        try
        {
            var modelo = await _produtos.ModeloDaReferenciaAsync(_base.Text);
            var b = modelo.Base;
            _nome.Text = b.Nome;
            _categoria.Text = b.Categoria ?? "";
            _cores.Text = b.Cores ?? "";
            _varejo.Text = Dinheiro.FormatarParaEdicao(b.PrecoVendaCentavos);
            _atacado.Text = b.PrecoAtacadoCentavos > 0 ? Dinheiro.FormatarParaEdicao(b.PrecoAtacadoCentavos) : "";
            _custo.Text = Dinheiro.FormatarParaEdicao(b.PrecoCustoCentavos);
            _minimo.Text = b.EstoqueMinimo.ToString(CultureInfo.InvariantCulture);
            _semBaixa.IsChecked = b.VenderSemBaixa;
            _referencia.Text = modelo.NovaReferencia;
            _barras.Text = modelo.NovaReferencia;
            _copiar = modelo.Tamanhos.ToHashSet();
            var emNumeros = _copiar.Any(t => GradesDeTamanho.CalcaBermuda.Tamanhos.Contains(t) && !GradesDeTamanho.Letras.Tamanhos.Contains(t));
            var aba = emNumeros ? "Números" : "Letras";
            foreach (var r in _abas) r.IsChecked = (string)r.Tag == aba;
            MostrarAba(aba);
            Avisar($"Nova referência {modelo.NovaReferencia} criada a partir da {b.Referencia}. Confira o nome, preencha as quantidades e salve.", false);
        }
        catch (RegraDeNegocioException ex) { Avisar(ex.Message, true); }
    }

    private async Task SalvarAsync()
    {
        Produto? produto = null;
        try
        {
            if (string.IsNullOrWhiteSpace(_nome.Text)) throw new RegraDeNegocioException("Informe o nome do produto.");
            if (!Dinheiro.TentarConverter(_varejo.Text, out var venda)) throw new RegraDeNegocioException("Preço de varejo inválido. Exemplo: 89,90");
            long custo = 0, atacado = 0;
            if (!string.IsNullOrWhiteSpace(_custo.Text) && !Dinheiro.TentarConverter(_custo.Text, out custo)) throw new RegraDeNegocioException("Custo inválido. Exemplo: 45,00");
            if (!string.IsNullOrWhiteSpace(_atacado.Text) && !Dinheiro.TentarConverter(_atacado.Text, out atacado)) throw new RegraDeNegocioException("Preço de atacado inválido. Exemplo: 59,90");
            var minimo = string.IsNullOrWhiteSpace(_minimo.Text) ? 0
                : int.TryParse(_minimo.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m
                : throw new RegraDeNegocioException("Estoque mínimo inválido. Use um número inteiro.");

            var unico = _aba == "Único" ? Quantidade(_unico) : 0;
            var dados = new DadosProduto(_nome.Text, _barras.Text, venda, custo, minimo, _referencia.Text, atacado, _categoria.Text,
                _semBaixa.IsChecked == true, _sobra.IsChecked == true, _cores.Text);
            produto = await _produtos.CadastrarAsync(dados, unico);

            var tamanhos = _aba == "Único" ? new Dictionary<string, int>()
                : _quantidades.Where(q => Quantidade(q.Value) > 0 || _copiar.Contains(q.Key)).ToDictionary(q => q.Key, q => Quantidade(q.Value));
            foreach (var t in _copiar.Where(t => !tamanhos.ContainsKey(t))) tamanhos[t] = 0; // tamanhos copiados da base que ficaram em outra aba
            if (tamanhos.Count > 0)
            {
                await _produtos.AdicionarGradeAsync(produto.Id, tamanhos.Keys);
                foreach (var (tamanho, qtd) in tamanhos.Where(t => t.Value > 0))
                    await _estoque.AjustarAsync(produto.Id, TipoMovimentacao.Entrada, qtd, "Estoque inicial", tamanho);
            }
            Criado = produto;
            DialogResult = true;
        }
        catch (RegraDeNegocioException ex)
        {
            if (produto is not null) { Criado = produto; Avisar($"Produto criado, mas houve um problema nos tamanhos: {ex.Message}", true); }
            else Avisar(ex.Message, true);
        }
    }

    private void Avisar(string texto, bool erro)
    {
        _aviso.Text = texto;
        _aviso.Foreground = erro && Application.Current.TryFindResource("CorPerigo") is Brush b ? b : Foreground;
    }
}
