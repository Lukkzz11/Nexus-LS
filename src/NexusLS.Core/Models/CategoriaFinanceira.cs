namespace NexusLS.Core.Models;

/// <summary>
/// Categoria dos lançamentos do Financeiro (aluguel, energia, serviços...), configurável pelo dono da loja. Nunca é apagada:
/// desativa-se (o lançamento antigo continua com o nome que tinha). O nome é único dentro de cada tipo (a pagar / a receber).
/// </summary>
public class CategoriaFinanceira : EntidadeSincronizavel
{
    public int Id { get; set; }
    public TipoLancamento Tipo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public bool Ativa { get; set; } = true;
    public DateTime CriadaEm { get; set; }
}
