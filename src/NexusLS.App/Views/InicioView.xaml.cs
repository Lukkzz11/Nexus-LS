using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NexusLS.App.Views;

/// <summary>
/// Só arranjo visual (nenhuma regra): a tela Início ocupa a largura e a altura inteiras. A área principal estica até
/// o fim da janela (abaixo de uma altura mínima a página rola em vez de espremer os painéis), e os atalhos ganham tantas
/// colunas quantas couberem, em linhas equilibradas; em tela estreita viram ladrilhos compactos (ícone em cima do nome).
/// </summary>
public partial class InicioView : UserControl
{
    /// <summary>Altura mínima da tela: abaixo dela, a página rola.</summary>
    private const double AlturaMinima = 900;

    /// <summary>Largura mínima de um atalho largo (ícone + nome + dica sem cortar) e de um compacto.</summary>
    private const double LarguraAtalhoLargo = 200;
    private const double LarguraAtalhoCompacto = 112;

    /// <summary>Largura mínima de um cartão do Consultor.</summary>
    private const double LarguraCartaoConsultor = 300;

    private UniformGrid? _gradeAtalhos;
    private UniformGrid? _gradeConsultor;

    public InicioView()
    {
        InitializeComponent();
        Rolagem.SizeChanged += (_, _) => Arranjar();
        PainelConsultor.SizeChanged += (_, _) => Arranjar();
        Atalhos.ItemContainerGenerator.ItemsChanged += (_, _) => AjustarAtalhos();
        Cartoes.ItemContainerGenerator.ItemsChanged += (_, _) => AjustarConsultor();
    }

    private void GradeAtalhos_Loaded(object sender, RoutedEventArgs e)
    {
        _gradeAtalhos = (UniformGrid)sender;
        AjustarAtalhos();
    }

    private void GradeConsultor_Loaded(object sender, RoutedEventArgs e)
    {
        _gradeConsultor = (UniformGrid)sender;
        AjustarConsultor();
    }

    private void Arranjar()
    {
        if (Rolagem.ActualHeight <= 0) return;
        // Os cartões do Consultor ocupam altura própria; a área principal continua com a altura de antes.
        var extra = PainelConsultor.Visibility == Visibility.Visible ? PainelConsultor.ActualHeight : 0;
        Conteudo.Height = Math.Max(Rolagem.ActualHeight, AlturaMinima + extra);
        AjustarConsultor();
        AjustarAtalhos();
    }

    /// <summary>Tantas colunas de cartões quantas couberem (no máximo uma por cartão).</summary>
    private void AjustarConsultor()
    {
        var largura = PainelConsultor.ActualWidth;
        if (_gradeConsultor is null || largura <= 0) return;
        var total = Math.Max(1, Cartoes.Items.Count);
        var cabem = Math.Max(1, (int)(largura / LarguraCartaoConsultor));
        var linhas = (int)Math.Ceiling(total / (double)Math.Min(cabem, total));
        var colunas = (int)Math.Ceiling(total / (double)linhas);
        if (_gradeConsultor.Columns != colunas) _gradeConsultor.Columns = colunas;
    }

    /// <summary>Uma linha só sempre que couber; se não couber, linhas com o mesmo número de atalhos.</summary>
    private void AjustarAtalhos()
    {
        var largura = Rolagem.ActualWidth;
        if (_gradeAtalhos is null || largura <= 0) return;
        var total = Math.Max(1, Atalhos.Items.Count);
        var largo = largura / total >= LarguraAtalhoLargo;
        var modelo = (DataTemplate)Resources[largo ? "AtalhoLargo" : "AtalhoCompacto"];
        if (!ReferenceEquals(Atalhos.ItemTemplate, modelo)) Atalhos.ItemTemplate = modelo;

        var cabem = Math.Max(1, (int)(largura / (largo ? LarguraAtalhoLargo : LarguraAtalhoCompacto)));
        var linhas = (int)Math.Ceiling(total / (double)Math.Min(cabem, total));
        _gradeAtalhos.Columns = (int)Math.Ceiling(total / (double)linhas);
    }
}
