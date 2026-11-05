using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Um ajuste de título com os textos prontos para o histórico do cliente (de → para, motivo, quem, quando).</summary>
public record AjusteDoTitulo(int VendaId, long ValorAnteriorCentavos, long ValorNovoCentavos,
    DateTime VencimentoAnterior, DateTime VencimentoNovo, string Motivo, string? Operador, DateTime Quando)
{
    public bool MudouValor => ValorAnteriorCentavos != ValorNovoCentavos;
    public bool MudouVencimento => VencimentoAnterior.Date != VencimentoNovo.Date;
}

/// <summary>
/// Edição de título a prazo (regra do ERP, sem mexer na venda). O valor devido e o vencimento ficam num ajuste; a venda
/// continua igual (itens, custo, receita e fechamento dependem dela) e a dívida do cliente segue recalculada por FIFO
/// (<see cref="ContaCorrente"/>), então nunca fica fora de sintonia. Tudo grava junto com a trilha em Atividades.
/// </summary>
public class TituloPrazoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public TituloPrazoService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>
    /// Muda o valor devido e/ou o vencimento do título da venda a prazo. Informe só o que muda (nulo = mantém).
    /// O valor novo precisa ser maior que zero e não menor que o que os pagamentos já abateram desse título (FIFO).
    /// </summary>
    public async Task<AjusteTituloPrazo> EditarAsync(int vendaId, long? novoValorCentavos, DateTime? novoVencimento, string motivo)
    {
        var texto = (motivo ?? "").Trim();
        if (texto.Length < 3) throw new RegraDeNegocioException("Informe o motivo da mudança (pelo menos 3 letras).");
        if (texto.Length > 200) throw new RegraDeNegocioException("O motivo pode ter no máximo 200 caracteres.");

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        var venda = await db.Vendas.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vendaId)
                    ?? throw new RegraDeNegocioException("Venda não encontrada.");
        if (venda.Cancelada) throw new RegraDeNegocioException("Venda cancelada não tem título a editar.");
        if (venda.FormaPagamento != FormaPagamento.APrazo || venda.ClienteId is not int clienteId)
            throw new RegraDeNegocioException("Só venda a prazo, de um cliente, tem título a editar.");

        var conta = await ClienteService.ContaAsync(db, clienteId);
        var titulo = conta.Titulos.FirstOrDefault(t => t.VendaId == vendaId)
                     ?? throw new RegraDeNegocioException("Título não encontrado.");

        var valor = novoValorCentavos ?? titulo.TotalCentavos;
        var vencimento = (novoVencimento ?? titulo.Vencimento).Date;
        if (valor <= 0) throw new RegraDeNegocioException("O valor do título deve ser maior que zero.");
        if (valor < titulo.PagoCentavos)
            throw new RegraDeNegocioException(
                $"Esse título já recebeu {Dinheiro.Formatar(titulo.PagoCentavos)}: o valor não pode ficar abaixo disso. " +
                "Estorne ou ajuste o pagamento do cliente antes.");
        if (vencimento < titulo.DataVenda.Date) throw new RegraDeNegocioException("O vencimento não pode ser antes da data da venda.");
        if (valor == titulo.TotalCentavos && vencimento == titulo.Vencimento.Date)
            throw new RegraDeNegocioException("Nada mudou: informe um valor ou um vencimento diferente.");

        var agora = _agora();
        var ajuste = new AjusteTituloPrazo
        {
            VendaId = vendaId,
            ValorAnteriorCentavos = titulo.TotalCentavos,
            VencimentoAnterior = titulo.Vencimento.Date,
            ValorNovoCentavos = valor,
            VencimentoNovo = vencimento,
            Motivo = texto,
            Operador = string.IsNullOrWhiteSpace(OperadorAtual.Nome) ? null : OperadorAtual.Nome.Trim(),
            CriadoEm = agora
        };
        db.AjustesTitulosPrazo.Add(ajuste);

        var partes = new List<string>();
        if (valor != titulo.TotalCentavos) partes.Add($"valor {Dinheiro.Formatar(titulo.TotalCentavos)} → {Dinheiro.Formatar(valor)}");
        if (vencimento != titulo.Vencimento.Date) partes.Add($"vencimento {titulo.Vencimento:dd/MM/yyyy} → {vencimento:dd/MM/yyyy}");
        AtividadeService.Registrar(db, TipoAtividade.TituloAPrazoEditado,
            $"Editou o título a prazo da venda #{vendaId} ({string.Join("; ", partes)}): {texto}", $"Venda #{vendaId}", null, agora);

        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return ajuste;
    }

    /// <summary>Todos os ajustes dos títulos de um cliente, do mais recente para o mais antigo (inclui os de venda já cancelada).</summary>
    public async Task<IReadOnlyList<AjusteDoTitulo>> HistoricoDoClienteAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var ids = await db.Vendas.AsNoTracking().Where(v => v.ClienteId == clienteId && v.FormaPagamento == FormaPagamento.APrazo)
            .Select(v => v.Id).ToListAsync();
        var ajustes = await db.AjustesTitulosPrazo.AsNoTracking().Where(a => ids.Contains(a.VendaId)).ToListAsync();
        return ajustes.OrderByDescending(a => a.CriadoEm).ThenByDescending(a => a.Id)
            .Select(a => new AjusteDoTitulo(a.VendaId, a.ValorAnteriorCentavos, a.ValorNovoCentavos, a.VencimentoAnterior,
                a.VencimentoNovo, a.Motivo, a.Operador, a.CriadoEm)).ToList();
    }
}
