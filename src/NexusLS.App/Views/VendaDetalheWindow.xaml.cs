using System.Windows;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>A venda por inteiro (peças, pagamento, operador, trocas já feitas). De lá o operador inicia a troca.</summary>
public partial class VendaDetalheWindow : Window
{
    public VendaDetalheWindow(DetalheVenda detalhe)
    {
        InitializeComponent();
        DataContext = new DetalheVendaViewModel(detalhe);
        Title = $"Venda #{detalhe.VendaId}";
    }

    /// <summary>Abre a janela e devolve verdadeiro se o operador pediu para trocar.</summary>
    public static bool Mostrar(Window? dono, DetalheVenda detalhe)
    {
        var janela = new VendaDetalheWindow(detalhe) { Owner = dono };
        return janela.ShowDialog() == true;
    }

    private void Trocar_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
