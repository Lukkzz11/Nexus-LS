namespace PdvSimples.Core.Services;

/// <summary>
/// Erro "esperado" (ex.: estoque insuficiente). A mensagem é em português e pode ser mostrada
/// direto ao operador. Erros inesperados (bugs, disco cheio) usam outras exceções.
/// </summary>
public class RegraDeNegocioException : Exception
{
    public RegraDeNegocioException(string mensagem) : base(mensagem) { }
}
