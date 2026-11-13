using System.ComponentModel;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class ConfiguracoesView : UserControl
{
    public ConfiguracoesView()
    {
        InitializeComponent();

        // O PasswordBox não aceita binding (de propósito): passa o segredo ao ViewModel por aqui e limpa a caixa quando ele zera.
        CaixaSegredoGoogle.PasswordChanged += (_, _) =>
        {
            if (DataContext is ConfiguracoesViewModel vm && vm.GoogleSegredoNovo != CaixaSegredoGoogle.Password)
                vm.GoogleSegredoNovo = CaixaSegredoGoogle.Password;
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged antigo) antigo.PropertyChanged -= AoMudarPropriedade;
            if (e.NewValue is INotifyPropertyChanged novo) novo.PropertyChanged += AoMudarPropriedade;
        };
    }

    // Máscaras enquanto digita (CNPJ/CPF, telefone e CEP), como no ERP.
    private static void Mascarar(object sender, Func<string?, string> formatar)
    {
        if (sender is not TextBox caixa) return;
        var formatado = formatar(caixa.Text);
        if (formatado == caixa.Text) return;
        caixa.Text = formatado;
        caixa.CaretIndex = formatado.Length;
    }

    private void Documento_TextChanged(object sender, TextChangedEventArgs e) => Mascarar(sender, NexusLS.Core.Services.ValidadorDocumento.FormatarDigitando);
    private void Telefone_TextChanged(object sender, TextChangedEventArgs e) => Mascarar(sender, NexusLS.Core.Services.ValidadorDocumento.FormatarTelefoneDigitando);
    private void Cep_TextChanged(object sender, TextChangedEventArgs e) => Mascarar(sender, NexusLS.Core.Services.ValidadorDocumento.FormatarCepDigitando);

    /// <summary>Abre o editor do comprovante numa janela própria (usa os mesmos dados da tela, então a prévia acompanha).</summary>
    private void EditarComprovante_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ConfiguracoesViewModel vm) return;
        new ComprovanteWindow { DataContext = vm, Owner = System.Windows.Window.GetWindow(this) }.ShowDialog();
    }

    private void AoMudarPropriedade(object? remetente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfiguracoesViewModel.GoogleSegredoNovo) && remetente is ConfiguracoesViewModel vm
            && CaixaSegredoGoogle.Password != vm.GoogleSegredoNovo)
            CaixaSegredoGoogle.Password = vm.GoogleSegredoNovo;
    }

    // PROVISÓRIO: remover junto com ComparacaoErpProvisoriaWindow.cs
    private void AbrirComparacaoErp(object sender, System.Windows.RoutedEventArgs e) =>
        new ComparacaoErpProvisoriaWindow { Owner = System.Windows.Window.GetWindow(this) }.ShowDialog();

    private void AbrirConfigPagamento(object sender, System.Windows.RoutedEventArgs e) =>
        new ConfiguracaoPagamentosWindow(App.Servicos.PagamentosConfig, App.Servicos.PixRepo)
        { Owner = System.Windows.Window.GetWindow(this) }.ShowDialog();
}
