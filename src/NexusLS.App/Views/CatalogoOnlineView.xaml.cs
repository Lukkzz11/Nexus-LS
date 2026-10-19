using System.ComponentModel;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class CatalogoOnlineView : UserControl
{
    public CatalogoOnlineView()
    {
        InitializeComponent();

        // O PasswordBox não aceita binding (de propósito, por segurança): passa a senha para o ViewModel por aqui,
        // e limpa a caixa quando o ViewModel zera a senha (depois de salvar).
        CaixaSenha.PasswordChanged += (_, _) =>
        {
            if (DataContext is CatalogoOnlineViewModel vm && vm.SenhaNova != CaixaSenha.Password) vm.SenhaNova = CaixaSenha.Password;
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged antigo) antigo.PropertyChanged -= AoMudarPropriedade;
            if (e.NewValue is INotifyPropertyChanged novo) novo.PropertyChanged += AoMudarPropriedade;
        };
    }

    private void AoMudarPropriedade(object? remetente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogoOnlineViewModel.SenhaNova) && remetente is CatalogoOnlineViewModel vm
            && CaixaSenha.Password != vm.SenhaNova)
            CaixaSenha.Password = vm.SenhaNova;
    }
}
