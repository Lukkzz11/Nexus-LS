using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NexusLS.App.Views;

/// <summary>
/// Só arranjo visual (nenhuma regra): a tela Início ocupa a largura e a altura inteiras. Conforme a largura,
/// os quatro painéis ficam lado a lado (tela larga) ou em duas linhas (tela estreita), e os atalhos ganham
/// tantas colunas quantas couberem, em linhas equilibradas.
/// </summary>
public partial class InicioView : UserControl
{
    /// <summary>Acima desta largura os quatro painéis cabem numa linha só.</summary>
    private const double LarguraLarga = 1200;

    /// <summary>Altura mínima da tela: abaixo dela, a página rola em vez de espremer os painéis.</summary>
    private const double AlturaMinimaLarga = 680;
    private const double AlturaMinimaEstreita = 1000;

    /// <summary>Largura mínima de um atalho (ícone + nome + dica sem cortar).</summary>
    private const double LarguraMinimaAtalho = 210;

    private UniformGrid? _gradeAtalhos;
    private bool? _larga;

    public InicioView()
    {
        InitializeComponent();
        Rolagem.SizeChanged += (_, _) => Arranjar();
        Atalhos.ItemContainerGenerator.ItemsChanged += (_, _) => AjustarAtalhos();
    }

    private void GradeAtalhos_Loaded(object sender, RoutedEventArgs e)
    {
        _gradeAtalhos = (UniformGrid)sender;
        AjustarAtalhos();
    }

    private void Arranjar()
    {
        var largura = Rolagem.ActualWidth;
        var altura = Rolagem.ActualHeight;
        if (largura <= 0 || altura <= 0) return;

        var larga = largura >= LarguraLarga;
        Conteudo.Height = Math.Max(altura, larga ? AlturaMinimaLarga : AlturaMinimaEstreita);
        if (larga != _larga)
        {
            _larga = larga;
            var colunas = AreaPrincipal.ColumnDefinitions;
            var linhas = AreaPrincipal.RowDefinitions;
            if (larga)
            {
                Colunas(colunas, 4, 3, 4, 3);
                linhas[0].Height = new GridLength(1, GridUnitType.Star);
                linhas[1].Height = new GridLength(0);
                Posicionar(PainelSemana, 0, 0);
                Posicionar(PainelMaisVendidos, 0, 1);
                Posicionar(PainelAtencao, 0, 2);
                Posicionar(ColunaLateral, 0, 3);
            }
            else
            {
                Colunas(colunas, 1, 1, 0, 0);
                linhas[0].Height = new GridLength(1, GridUnitType.Star);
                linhas[1].Height = new GridLength(1, GridUnitType.Star);
                Posicionar(PainelSemana, 0, 0);
                Posicionar(PainelAtencao, 0, 1);
                Posicionar(PainelMaisVendidos, 1, 0);
                Posicionar(ColunaLateral, 1, 1);
            }
        }
        AjustarAtalhos();
    }

    private static void Colunas(ColumnDefinitionCollection colunas, params double[] pesos)
    {
        for (var i = 0; i < colunas.Count; i++) colunas[i].Width = new GridLength(pesos[i], GridUnitType.Star);
    }

    private static void Posicionar(UIElement painel, int linha, int coluna)
    {
        Grid.SetRow(painel, linha);
        Grid.SetColumn(painel, coluna);
    }

    /// <summary>Quantas colunas couberem; se precisar de mais de uma linha, as linhas ficam com o mesmo número de atalhos.</summary>
    private void AjustarAtalhos()
    {
        if (_gradeAtalhos is null || Rolagem.ActualWidth <= 0) return;
        var total = Math.Max(1, Atalhos.Items.Count);
        var cabem = Math.Max(1, (int)(Rolagem.ActualWidth / LarguraMinimaAtalho));
        var linhas = (int)Math.Ceiling(total / (double)Math.Min(cabem, total));
        _gradeAtalhos.Columns = (int)Math.Ceiling(total / (double)linhas);
    }
}
