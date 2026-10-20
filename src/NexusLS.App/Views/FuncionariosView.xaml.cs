using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class FuncionariosView : UserControl
{
    public FuncionariosView()
    {
        InitializeComponent();

        // O PasswordBox não aceita binding (de propósito, por segurança): o PIN passa para o ViewModel por aqui,
        // e as caixas se esvaziam quando o ViewModel zera os PINs (depois de salvar).
        CaixaPin.PasswordChanged += (_, _) => { if (DataContext is FuncionariosViewModel vm && vm.PinNovo != CaixaPin.Password) vm.PinNovo = CaixaPin.Password; };
        CaixaPinConfirmacao.PasswordChanged += (_, _) => { if (DataContext is FuncionariosViewModel vm && vm.PinConfirmacao != CaixaPinConfirmacao.Password) vm.PinConfirmacao = CaixaPinConfirmacao.Password; };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged antigo) antigo.PropertyChanged -= AoMudar;
            if (e.NewValue is FuncionariosViewModel vm)
            {
                vm.PropertyChanged += AoMudar;
                vm.Avisar = (titulo, texto) => MessageBox.Show(Window.GetWindow(this), texto, titulo, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
    }

    private void AoMudar(object? remetente, PropertyChangedEventArgs e)
    {
        if (remetente is not FuncionariosViewModel vm) return;
        if (e.PropertyName == nameof(FuncionariosViewModel.PinNovo) && CaixaPin.Password != vm.PinNovo) CaixaPin.Password = vm.PinNovo;
        if (e.PropertyName == nameof(FuncionariosViewModel.PinConfirmacao) && CaixaPinConfirmacao.Password != vm.PinConfirmacao) CaixaPinConfirmacao.Password = vm.PinConfirmacao;
    }
}
