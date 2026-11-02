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

    private void AoMudarPropriedade(object? remetente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfiguracoesViewModel.GoogleSegredoNovo) && remetente is ConfiguracoesViewModel vm
            && CaixaSegredoGoogle.Password != vm.GoogleSegredoNovo)
            CaixaSegredoGoogle.Password = vm.GoogleSegredoNovo;
    }
}
