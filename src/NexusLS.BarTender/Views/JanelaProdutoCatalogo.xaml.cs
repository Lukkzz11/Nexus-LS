using System.Windows;
using System.Windows.Input;
using NexusLS.BarTender.ViewModels;

namespace NexusLS.BarTender.Views;

/// <summary>
/// Modal do catálogo: mostra os dados do produto, deixa digitar a quantidade de cada tamanho
/// e oferece as duas saídas (Estúdio ou preparação de impressão). A seleção digitada é mantida
/// na instância de <see cref="SelecaoImpressaoViewModel"/> recebida, então voltar ao modal
/// depois não perde o que já foi informado.
/// </summary>
public partial class JanelaProdutoCatalogo : Window
{
    public SelecaoImpressaoViewModel Selecao { get; }

    /// <summary>O que o operador escolheu. Nenhuma quando a janela foi fechada/cancelada.</summary>
    public AcaoProdutoCatalogo Acao { get; private set; } = AcaoProdutoCatalogo.Nenhuma;

    public JanelaProdutoCatalogo(SelecaoImpressaoViewModel selecao)
    {
        Selecao = selecao ?? throw new ArgumentNullException(nameof(selecao));

        InitializeComponent();
        DataContext = selecao;

        // Fecha com Esc sem precisar do botão Cancelar.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Acao = AcaoProdutoCatalogo.Nenhuma;
                Close();
            }
        };
    }

    /// <summary>Abre o modal sobre a janela ativa e devolve a ação escolhida.</summary>
    public static AcaoProdutoCatalogo Mostrar(Window? owner, SelecaoImpressaoViewModel selecao)
    {
        var janela = new JanelaProdutoCatalogo(selecao);

        var dono = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                   ?? Application.Current?.MainWindow;

        if (dono is not null && !ReferenceEquals(dono, janela) && dono.IsVisible)
            janela.Owner = dono;

        janela.ShowDialog();
        return janela.Acao;
    }

    private void Fechar_Click(object sender, RoutedEventArgs e)
    {
        Acao = AcaoProdutoCatalogo.Nenhuma;
        Close();
    }

    private void AbrirEstudio_Click(object sender, RoutedEventArgs e)
    {
        // Vai para o Estúdio mesmo sem quantidade: lá o operador edita o modelo.
        Acao = AcaoProdutoCatalogo.Estudio;
        DialogResult = true;
    }

    private void IrParaImpressao_Click(object sender, RoutedEventArgs e)
    {
        Selecao.AtualizarTotais();

        if (!Selecao.ValidarParaImpressao())
        {
            // A mensagem já aparece no rodapé do modal; não fecha para o operador corrigir.
            return;
        }

        Acao = AcaoProdutoCatalogo.Impressao;
        DialogResult = true;
    }
}
