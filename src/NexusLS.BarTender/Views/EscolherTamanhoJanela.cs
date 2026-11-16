using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NexusLS.BarTender.Views;

/// <summary>Pergunta qual tamanho do produto abrir no Estúdio. Um botão por tamanho.</summary>
public sealed class EscolherTamanhoJanela : Window
{
    public string? Escolhido { get; private set; }

    public EscolherTamanhoJanela(string produto, IReadOnlyList<string> tamanhos)
    {
        Title = "Escolha o tamanho";
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 340;
        MaxWidth = 560;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.ToolWindow;
        SetResourceReference(BackgroundProperty, "CorSuperficie");

        var raiz = new StackPanel { Margin = new Thickness(20) };
        raiz.Children.Add(new TextBlock
        {
            Text = "Qual tamanho você quer editar?",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
        });
        raiz.Children.Add(new TextBlock
        {
            Text = produto,
            Opacity = 0.7,
            Margin = new Thickness(0, 2, 0, 14),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 500,
        });

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
            if (TryFindResource("BotaoPrimario") is Style estilo) botao.Style = estilo;
            botao.Click += (_, _) => { Escolhido = t; DialogResult = true; };
            painel.Children.Add(botao);
        }
        raiz.Children.Add(painel);

        var cancelar = new Button { Content = "Cancelar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(14, 5, 14, 5), IsCancel = true };
        if (TryFindResource("BotaoSecundario") is Style sec) cancelar.Style = sec;
        raiz.Children.Add(cancelar);
        Content = raiz;

        var dono = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != this);
        if (dono is { IsLoaded: true }) { Owner = dono; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
