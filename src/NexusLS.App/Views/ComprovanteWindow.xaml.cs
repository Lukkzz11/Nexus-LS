using System.Windows;

namespace NexusLS.App.Views;

/// <summary>O editor do comprovante, numa janela própria. Usa o mesmo ConfiguracoesViewModel da tela de Configurações.</summary>
public partial class ComprovanteWindow : Window
{
    public ComprovanteWindow() => InitializeComponent();

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
