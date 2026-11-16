using System.Windows;
using NexusLS.BarTender.Views;

namespace NexusLS.BarTender.Services;

public class DialogoService : IDialogoService
{
    private static IDialogoService? _instanciaPadrao;
    public static IDialogoService Instancia => _instanciaPadrao ??= new DialogoService();

    public static void DefinirInstancia(IDialogoService instancia) => _instanciaPadrao = instancia;

    public bool Confirmar(string mensagem, string titulo = "Confirmação", string textoConfirmar = "Confirmar", string textoCancelar = "Cancelar")
    {
        bool Executar()
        {
            try
            {
                var janela = new JanelaDialogoFluent(mensagem, titulo, TipoDialogoFluent.Confirmacao, textoConfirmar, textoCancelar);
                janela.ShowDialog();
                return janela.Resultado;
            }
            catch
            {
                return MessageBox.Show(mensagem, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            }
        }

        if (Application.Current == null)
            return false;

        if (Application.Current.Dispatcher.CheckAccess())
            return Executar();

        return Application.Current.Dispatcher.Invoke(Executar);
    }

    public void Informar(string mensagem, string titulo = "Informação")
    {
        void Executar()
        {
            try
            {
                var janela = new JanelaDialogoFluent(mensagem, titulo, TipoDialogoFluent.Informacao);
                janela.ShowDialog();
            }
            catch
            {
                MessageBox.Show(mensagem, titulo, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        if (Application.Current == null)
            return;

        if (Application.Current.Dispatcher.CheckAccess())
            Executar();
        else
            Application.Current.Dispatcher.Invoke(Executar);
    }

    public void Erro(string mensagem, string titulo = "Erro")
    {
        void Executar()
        {
            try
            {
                var janela = new JanelaDialogoFluent(mensagem, titulo, TipoDialogoFluent.Erro);
                janela.ShowDialog();
            }
            catch
            {
                MessageBox.Show(mensagem, titulo, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        if (Application.Current == null)
            return;

        if (Application.Current.Dispatcher.CheckAccess())
            Executar();
        else
            Application.Current.Dispatcher.Invoke(Executar);
    }

    public void Sucesso(string mensagem, string titulo = "Sucesso")
    {
        void Executar()
        {
            try
            {
                if (Application.Current?.MainWindow is MainWindow mw)
                {
                    mw.ExibirToast(mensagem, titulo);
                    return;
                }

                // Fallback para caso não haja MainWindow pronta
                var janela = new JanelaDialogoFluent(mensagem, titulo, TipoDialogoFluent.Sucesso);
                janela.ShowDialog();
            }
            catch
            {
                App.Log($"[SUCESSO] {titulo}: {mensagem}");
            }
        }

        if (Application.Current == null)
            return;

        if (Application.Current.Dispatcher.CheckAccess())
            Executar();
        else
            Application.Current.Dispatcher.InvokeAsync(Executar);
    }
}
