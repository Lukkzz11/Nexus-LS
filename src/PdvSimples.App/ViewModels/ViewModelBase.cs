using CommunityToolkit.Mvvm.ComponentModel;
using PdvSimples.Core.Services;

namespace PdvSimples.App.ViewModels;

/// <summary>
/// Código que toda tela repete: a mensagem de sucesso/erro e a proteção contra exceções.
/// Herdar desta classe evita copiar e colar esse trecho em cada ViewModel.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private string _mensagem = "";
    [ObservableProperty] private bool _mensagemEhErro;

    /// <summary>Roda uma ação e transforma qualquer erro em mensagem na tela, em vez de fechar o programa.</summary>
    protected async Task ExecutarAsync(Func<Task> acao)
    {
        try
        {
            await acao();
        }
        catch (RegraDeNegocioException ex)
        {
            MostrarErro(ex.Message);
        }
        catch (Exception ex)
        {
            MostrarErro("Erro inesperado: " + ex.Message);
        }
    }

    protected void MostrarSucesso(string texto)
    {
        MensagemEhErro = false;
        Mensagem = texto;
    }

    protected void MostrarErro(string texto)
    {
        MensagemEhErro = true;
        Mensagem = texto;
    }
}
