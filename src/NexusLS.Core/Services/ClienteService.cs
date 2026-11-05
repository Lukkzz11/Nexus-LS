using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosCliente(string Nome, string? Documento = null, string? Telefone = null, string? Observacao = null, long LimiteCreditoCentavos = 0);

/// <summary>
/// As compras de um cliente. <c>TotalAPrazoCentavos</c> é só o que foi vendido a prazo; o que ainda
/// está devendo (descontando os pagamentos) vem de <see cref="ClienteService.ContaAsync(int)"/>.
/// </summary>
/// <param name="Vendas">Da mais recente para a mais antiga, inclusive as canceladas.</param>
public record HistoricoCliente(
    IReadOnlyList<Venda> Vendas, int Compras, long TotalCompradoCentavos, long TotalAPrazoCentavos, DateTime? UltimaCompra);

public class ClienteService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public ClienteService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<Cliente> CadastrarAsync(DadosCliente dados)
    {
        Validar(dados);
        var cliente = new Cliente { DataCadastro = _agora(), Ativo = true };
        Preencher(cliente, dados);

        await using var db = await _fabrica.CreateDbContextAsync();
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente;
    }

    public async Task EditarAsync(int id, DadosCliente dados)
    {
        Validar(dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        var cliente = await db.Clientes.FindAsync(id)
            ?? throw new RegraDeNegocioException("Cliente não encontrado.");
        Preencher(cliente, dados);
        await db.SaveChangesAsync();
    }

    /// <summary>Desativar não apaga: o cliente some da busca, mas continua nas vendas antigas.</summary>
    public Task DesativarAsync(int id) => DefinirAtivoAsync(id, false);

    public Task ReativarAsync(int id) => DefinirAtivoAsync(id, true);

    /// <summary>Busca por parte do nome, documento ou telefone. Sem texto, lista todos.</summary>
    public async Task<List<Cliente>> BuscarAsync(string? texto, bool incluirInativos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        IQueryable<Cliente> consulta = db.Clientes.AsNoTracking();
        if (!incluirInativos) consulta = consulta.Where(c => c.Ativo);

        texto = texto?.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            var padrao = $"%{texto}%";
            consulta = consulta.Where(c =>
                EF.Functions.Like(c.Nome, padrao) ||
                (c.Documento != null && EF.Functions.Like(c.Documento, padrao)) ||
                (c.Telefone != null && EF.Functions.Like(c.Telefone, padrao)));
        }
        return await consulta.OrderBy(c => c.Nome).ToListAsync();
    }

    /// <summary>Vendas do cliente com os totais (as canceladas aparecem na lista mas não somam).</summary>
    public async Task<HistoricoCliente> HistoricoAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => v.ClienteId == clienteId)
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .ToListAsync();

        var validas = vendas.Where(v => !v.Cancelada).ToList();
        return new HistoricoCliente(
            vendas,
            validas.Count,
            validas.Sum(v => v.TotalCentavos),
            validas.Where(v => v.FormaPagamento == FormaPagamento.APrazo).Sum(v => v.TotalCentavos),
            validas.Count == 0 ? null : validas.Max(v => v.DataHora));
    }

    /// <summary>A dívida a prazo do cliente: títulos (vencimento em 30 dias) já abatidos pelos pagamentos, do mais antigo ao mais novo.</summary>
    public async Task<ContaCliente> ContaAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await ContaAsync(db, clienteId);
    }

    internal static async Task<ContaCliente> ContaAsync(PdvDbContext db, int clienteId)
    {
        var vendas = await db.Vendas.AsNoTracking()
            .Where(v => v.ClienteId == clienteId && v.FormaPagamento == FormaPagamento.APrazo).ToListAsync();
        var pagamentos = await db.PagamentosCliente.AsNoTracking().Where(p => p.ClienteId == clienteId).ToListAsync();
        var ids = vendas.Select(v => v.Id).ToList();
        var ajustes = await db.AjustesTitulosPrazo.AsNoTracking().Where(a => ids.Contains(a.VendaId)).ToListAsync();
        return ContaCorrente.Calcular(vendas, pagamentos, ajustes);
    }

    /// <summary>Pagamentos recebidos do cliente, do mais recente para o mais antigo.</summary>
    public async Task<IReadOnlyList<PagamentoCliente>> PagamentosAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.PagamentosCliente.AsNoTracking().Where(p => p.ClienteId == clienteId).ToListAsync();
        return lista.OrderByDescending(p => p.DataHora).ThenByDescending(p => p.Id).ToList();
    }

    /// <summary>Registra que o cliente pagou parte (ou toda) a dívida. Não pode passar do que ele deve.</summary>
    public async Task<PagamentoCliente> RegistrarPagamentoAsync(int clienteId, long valorCentavos, FormaPagamento forma, string? observacao = null)
    {
        if (valorCentavos <= 0)
            throw new RegraDeNegocioException("Informe um valor maior que zero.");
        if (forma == FormaPagamento.APrazo)
            throw new RegraDeNegocioException("Escolha como o cliente pagou: dinheiro, Pix, cartão ou cheque.");

        await using var db = await _fabrica.CreateDbContextAsync();
        if (!await db.Clientes.AnyAsync(c => c.Id == clienteId))
            throw new RegraDeNegocioException("Cliente não encontrado.");

        var conta = await ContaAsync(db, clienteId);
        if (valorCentavos > conta.SaldoDevedorCentavos)
            throw new RegraDeNegocioException(
                $"O cliente deve {Dinheiro.Formatar(conta.SaldoDevedorCentavos)}; o pagamento não pode ser maior.");

        var operador = await db.ConfiguracoesLoja.AsNoTracking().Where(c => c.Id == 1).Select(c => c.NomeOperador).FirstOrDefaultAsync();
        var pagamento = new PagamentoCliente
        {
            ClienteId = clienteId,
            DataHora = _agora(),
            ValorCentavos = valorCentavos,
            Forma = forma,
            Observacao = Limpar(observacao),
            Operador = !string.IsNullOrWhiteSpace(OperadorAtual.Nome) ? OperadorAtual.Nome : string.IsNullOrWhiteSpace(operador) ? "Caixa" : operador
        };
        db.PagamentosCliente.Add(pagamento);
        await db.SaveChangesAsync();

        // Pagou com cheque: o dinheiro só entra na conta quando o cheque for compensado.
        if (forma == FormaPagamento.Cheque)
        {
            var nome = await db.Clientes.AsNoTracking().Where(c => c.Id == clienteId).Select(c => c.Nome).FirstOrDefaultAsync();
            db.ChequesRecebidos.Add(ChequeService.NovoPendente(valorCentavos, pagamento.DataHora, nome, null, pagamento.Id));
            await db.SaveChangesAsync();
        }
        return pagamento;
    }

    private async Task DefinirAtivoAsync(int id, bool ativo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var cliente = await db.Clientes.FindAsync(id)
            ?? throw new RegraDeNegocioException("Cliente não encontrado.");
        cliente.Ativo = ativo;
        await db.SaveChangesAsync();
    }

    private static void Validar(DadosCliente dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new RegraDeNegocioException("Informe o nome do cliente.");
        if (dados.LimiteCreditoCentavos < 0)
            throw new RegraDeNegocioException("O limite de crédito não pode ser negativo.");
    }

    private static void Preencher(Cliente cliente, DadosCliente dados)
    {
        cliente.Nome = dados.Nome.Trim();
        cliente.Documento = Limpar(dados.Documento);
        cliente.Telefone = Limpar(dados.Telefone);
        cliente.Observacao = Limpar(dados.Observacao);
        cliente.LimiteCreditoCentavos = dados.LimiteCreditoCentavos;
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
