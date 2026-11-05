using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record FiltroAtividades(DateTime? De = null, DateTime? Ate = null, TipoAtividade? Tipo = null, string? Texto = null, int Limite = 500);

public class AtividadeService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public AtividadeService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>
    /// Adiciona o registro no contexto da ação em andamento: ele só vale se a ação gravar (mesma transação).
    /// Quem chama continua responsável pelo SaveChanges.
    /// </summary>
    public static void Registrar(PdvDbContext db, TipoAtividade tipo, string descricao, string? referencia = null,
        string? operador = null, DateTime? agora = null)
    {
        db.Atividades.Add(new Atividade
        {
            DataHora = agora ?? DateTime.Now,
            Tipo = tipo,
            Operador = Limpar(operador) ?? Limpar(OperadorAtual.Nome),
            Descricao = descricao.Trim(),
            Referencia = Limpar(referencia)
        });
    }

    public static string NomeDoTipo(TipoAtividade tipo) => tipo switch
    {
        TipoAtividade.VendaCancelada => "Venda cancelada",
        TipoAtividade.CompraCancelada => "Compra cancelada",
        TipoAtividade.LancamentoCancelado => "Lançamento cancelado",
        TipoAtividade.PagamentoEstornado => "Pagamento estornado",
        TipoAtividade.PedidoCancelado => "Pedido cancelado",
        TipoAtividade.EstoqueAjustado => "Estoque ajustado",
        TipoAtividade.FuncionarioCadastrado => "Funcionário cadastrado",
        TipoAtividade.FuncionarioEditado => "Funcionário editado",
        TipoAtividade.FuncionarioDesativado => "Funcionário desativado",
        TipoAtividade.FuncionarioReativado => "Funcionário reativado",
        TipoAtividade.PinRedefinido => "PIN redefinido",
        TipoAtividade.DadosApagados => "Dados apagados",
        TipoAtividade.TransferenciaCancelada => "Transferência cancelada",
        TipoAtividade.LiquidacaoEstornada => "Liquidação de cartão estornada",
        TipoAtividade.ChequeDevolvido => "Cheque devolvido",
        TipoAtividade.ChequeCancelado => "Cheque cancelado",
        TipoAtividade.ContaAlterada => "Conta alterada",
        TipoAtividade.LancamentoCorrigido => "Lançamento corrigido",
        TipoAtividade.LancamentoRetirado => "Lançamento retirado",
        TipoAtividade.TituloAPrazoEditado => "Título a prazo editado",
        TipoAtividade.ChequeEmitido => "Cheque emitido",
        TipoAtividade.ChequeEmitidoCompensado => "Cheque emitido compensado",
        TipoAtividade.ChequeEmitidoCancelado => "Cheque emitido cancelado",
        _ => tipo.ToString()
    };

    /// <summary>Mais recentes primeiro. O período usa a data inteira (de 00:00 até o fim do dia "até").</summary>
    public async Task<List<Atividade>> ListarAsync(FiltroAtividades? filtro = null)
    {
        filtro ??= new FiltroAtividades();
        await using var db = await _fabrica.CreateDbContextAsync();
        IQueryable<Atividade> consulta = db.Atividades.AsNoTracking();

        if (filtro.De is DateTime de) consulta = consulta.Where(a => a.DataHora >= de.Date);
        if (filtro.Ate is DateTime ate) consulta = consulta.Where(a => a.DataHora < ate.Date.AddDays(1));
        if (filtro.Tipo is TipoAtividade tipo) consulta = consulta.Where(a => a.Tipo == tipo);

        var texto = filtro.Texto?.Trim();
        if (!string.IsNullOrEmpty(texto))
            consulta = consulta.Where(a => EF.Functions.Like(a.Descricao, $"%{texto}%")
                                        || (a.Operador != null && EF.Functions.Like(a.Operador, $"%{texto}%"))
                                        || (a.Referencia != null && EF.Functions.Like(a.Referencia, $"%{texto}%")));

        return await consulta.OrderByDescending(a => a.DataHora).ThenByDescending(a => a.Id)
            .Take(Math.Clamp(filtro.Limite, 1, 5000)).ToListAsync();
    }

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
