using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NexusLS.BarTender.Views;

/// <summary>
/// Pergunta qual tamanho do produto abrir no Estúdio. Um botão por tamanho. Mesmo visual do modal do produto
/// (JanelaProdutoCatalogo): cartão claro com cabeçalho, sombra e cantos arredondados.
/// </summary>
public sealed class EscolherTamanhoJanela : Window
{
    public string? Escolhido { get; private set; }

    private static object? Recurso(string chave) => Application.Current?.TryFindResource(chave);

    public EscolherTamanhoJanela(string produto, IReadOnlyList<string> tamanhos)
    {
        Title = "Escolha o tamanho";
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 420;
        MaxWidth = 640;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        if (Recurso("FonteTexto") is FontFamily fonte) FontFamily = fonte;

        var cartao = new Border
        {
            Margin = new Thickness(18),
            BorderThickness = new Thickness(1),
            CornerRadius = Recurso("RaioGrande") is CornerRadius raio ? raio : new CornerRadius(12),
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x0F, 0x17, 0x2A), BlurRadius = 32, ShadowDepth = 10, Opacity = 0.24 },
        };
        cartao.SetResourceReference(Border.BackgroundProperty, "CorPainel");
        cartao.SetResourceReference(Border.BorderBrushProperty, "CorBorda");

        var raiz = new StackPanel();

        // Cabeçalho: pergunta + nome do produto + fechar
        var cabecalho = new Border { Padding = new Thickness(22, 16, 16, 16), BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(10, 10, 0, 0) };
        cabecalho.SetResourceReference(Border.BackgroundProperty, "CorSuperficieSuave");
        cabecalho.SetResourceReference(Border.BorderBrushProperty, "CorBorda");
        var linha = new DockPanel();
        var fechar = new Button { Content = "✕", Width = 32, Height = 32, Padding = new Thickness(0), Cursor = Cursors.Hand, IsCancel = true, VerticalAlignment = VerticalAlignment.Top };
        if (Recurso("BotaoSecundario") is Style secF) fechar.Style = secF;
        DockPanel.SetDock(fechar, Dock.Right);
        linha.Children.Add(fechar);
        var titulos = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        var pergunta = new TextBlock { Text = "Qual tamanho você quer editar?", FontSize = 18, FontWeight = FontWeights.Bold };
        pergunta.SetResourceReference(TextBlock.ForegroundProperty, "CorTexto");
        var nome = new TextBlock { Text = produto, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 };
        nome.SetResourceReference(TextBlock.ForegroundProperty, "CorTextoSuave");
        titulos.Children.Add(pergunta);
        titulos.Children.Add(nome);
        linha.Children.Add(titulos);
        cabecalho.Child = linha;
        raiz.Children.Add(cabecalho);

        // Tamanhos
        var corpo = new StackPanel { Margin = new Thickness(22, 18, 22, 8) };
        var dica = new TextBlock { Text = "Escolha o tamanho que abre no Estúdio:", FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
        dica.SetResourceReference(TextBlock.ForegroundProperty, "CorTextoSuave");
        corpo.Children.Add(dica);
        var painel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var t in tamanhos)
        {
            var botao = new Button
            {
                Content = t,
                MinWidth = 64,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 8),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
            };
            if (Recurso("BotaoSecundario") is Style estilo) botao.Style = estilo;
            botao.Click += (_, _) => { Escolhido = t; DialogResult = true; };
            painel.Children.Add(botao);
        }
        corpo.Children.Add(painel);
        raiz.Children.Add(corpo);

        // Rodapé
        var rodape = new Border { Padding = new Thickness(22, 12, 22, 12), BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 10, 10) };
        rodape.SetResourceReference(Border.BackgroundProperty, "CorSuperficieSuave");
        rodape.SetResourceReference(Border.BorderBrushProperty, "CorBorda");
        var cancelar = new Button { Content = "Cancelar", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7), IsCancel = true };
        if (Recurso("BotaoSecundario") is Style sec) cancelar.Style = sec;
        rodape.Child = cancelar;
        raiz.Children.Add(rodape);

        cartao.Child = raiz;
        Content = cartao;

        var dono = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != this);
        if (dono is { IsLoaded: true }) { Owner = dono; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
