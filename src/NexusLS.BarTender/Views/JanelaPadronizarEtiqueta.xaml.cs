using System.Windows;
using System.Windows.Input;
using NexusLS.BarTender.ViewModels;

namespace NexusLS.BarTender.Views;

/// <summary>
/// Modal "Padronizar Etiqueta": o operador escolhe um modelo salvo/feito por ele ou predefinição,
/// confere a prévia com Tamanho, Referência e Código de Barras (Ref + Tamanho) e confirma.
/// </summary>
public partial class JanelaPadronizarEtiqueta : Window
{
    public PadronizarEtiquetaViewModel ViewModel { get; }

    public bool Confirmado { get; private set; }

    public JanelaPadronizarEtiqueta(PadronizarEtiquetaViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Confirmado = false;
                Close();
            }
        };
    }

    public static bool Mostrar(Window? owner, PadronizarEtiquetaViewModel viewModel)
    {
        var janela = new JanelaPadronizarEtiqueta(viewModel);

        var dono = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                   ?? Application.Current?.MainWindow;

        if (dono is not null && !ReferenceEquals(dono, janela) && dono.IsVisible)
            janela.Owner = dono;

        janela.ShowDialog();
        return janela.Confirmado;
    }

    private void Fechar_Click(object sender, RoutedEventArgs e)
    {
        Confirmado = false;
        Close();
    }

    private void ConfirmarPadronizacao_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.OpcaoSelecionada is null)
            return;

        Confirmado = true;
        DialogResult = true;
    }
}
