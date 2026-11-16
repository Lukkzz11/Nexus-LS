namespace NexusLS.BarTender.Services;

public interface IDialogoService
{
    /// <summary>Exibe um diálogo modal de confirmação (Sim/Não ou Confirmar/Cancelar).</summary>
    bool Confirmar(string mensagem, string titulo = "Confirmação", string textoConfirmar = "Confirmar", string textoCancelar = "Cancelar");

    /// <summary>Exibe um diálogo modal informativo.</summary>
    void Informar(string mensagem, string titulo = "Informação");

    /// <summary>Exibe um diálogo modal de erro.</summary>
    void Erro(string mensagem, string titulo = "Erro");

    /// <summary>Exibe um toast discreto que desaparece automaticamente em ~3 segundos sem travar o usuário.</summary>
    void Sucesso(string mensagem, string titulo = "Sucesso");
}
