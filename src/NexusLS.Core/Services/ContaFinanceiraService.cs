using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosConta(string Nome, TipoContaFin Tipo, long SaldoInicialCentavos = 0, DateTime? DataSaldoInicial = null, bool Padrao = false);

public record DadosTransferencia(int ContaOrigemId, int ContaDestinoId, long ValorCentavos, DateTime Data, string? Descricao = null);

/// <summary>
/// Contas (gaveta, cofre, bancos) e transferências entre elas. O saldo nunca é gravado: o motor financeiro calcula.
/// Nada é apagado: conta se desativa, transferência se cancela com motivo.
/// </summary>
public class ContaFinanceiraService
{
    public const string NomeDaGaveta = "Gaveta do caixa";

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public ContaFinanceiraService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Garante que existe ao menos a gaveta do caixa (é nela que entra o dinheiro das vendas). Pode chamar à vontade.</summary>
    public async Task GarantirContaPadraoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        if (await db.ContasFinanceiras.AnyAsync(c => c.Tipo == TipoContaFin.Caixa)) return;
        db.ContasFinanceiras.Add(new ContaFinanceira { Nome = NomeDaGaveta, Tipo = TipoContaFin.Caixa, CriadaEm = _agora() });
        await db.SaveChangesAsync();
    }

    public async Task<List<ContaFinanceira>> ListarAsync(bool incluirInativas = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.ContasFinanceiras.AsNoTracking()
            .Where(c => incluirInativas || c.Ativa)
            .OrderBy(c => c.Tipo).ThenBy(c => c.Nome).ToListAsync();
    }

    public async Task<ContaFinanceira> CriarAsync(DadosConta dados)
    {
        var nome = ValidarNome(dados.Nome);
        await using var db = await _fabrica.CreateDbContextAsync();
        await GarantirNomeLivreAsync(db, nome, null);

        var conta = new ContaFinanceira { CriadaEm = _agora() };
        Preencher(conta, dados, nome);
        // A primeira conta de banco vira a padrão sozinha: é para onde vão o Pix e o cartão.
        if (conta.Tipo == TipoContaFin.Banco && !await db.ContasFinanceiras.AnyAsync(c => c.Tipo == TipoContaFin.Banco && c.Ativa))
            conta.Padrao = true;
        if (conta.Padrao) await TirarPadraoDasOutrasAsync(db, conta.Tipo, null);
        db.ContasFinanceiras.Add(conta);
        await db.SaveChangesAsync();
        return conta;
    }

    public async Task EditarAsync(int id, DadosConta dados)
    {
        var nome = ValidarNome(dados.Nome);
        await using var db = await _fabrica.CreateDbContextAsync();
        var conta = await ObterAsync(db, id);
        await GarantirNomeLivreAsync(db, nome, id);
        if (dados.Tipo != conta.Tipo && await TemMovimentoAsync(db, id))
            throw new RegraDeNegocioException("Esta conta já tem movimento: não dá para trocar entre caixa e banco.");

        var antes = $"{conta.Nome}, saldo inicial {Dinheiro.Formatar(conta.SaldoInicialCentavos)}";
        Preencher(conta, dados, nome);
        if (conta.Padrao) await TirarPadraoDasOutrasAsync(db, conta.Tipo, id);
        AtividadeService.Registrar(db, TipoAtividade.ContaAlterada,
            $"Alterou a conta \"{antes}\" para \"{conta.Nome}\", saldo inicial {Dinheiro.Formatar(conta.SaldoInicialCentavos)}",
            $"Conta {conta.Nome}", agora: _agora());
        await db.SaveChangesAsync();
    }

    public async Task DesativarAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var conta = await ObterAsync(db, id);
        if (conta.Tipo == TipoContaFin.Caixa && !await db.ContasFinanceiras.AnyAsync(c => c.Id != id && c.Ativa && c.Tipo == TipoContaFin.Caixa))
            throw new RegraDeNegocioException("Precisa existir ao menos uma conta de caixa (é nela que entra o dinheiro das vendas).");
        conta.Ativa = false;
        if (conta.Padrao)
        {
            conta.Padrao = false;
            var outra = await db.ContasFinanceiras.Where(c => c.Id != id && c.Ativa && c.Tipo == conta.Tipo).OrderBy(c => c.Id).FirstOrDefaultAsync();
            if (outra is not null) outra.Padrao = true;
        }
        AtividadeService.Registrar(db, TipoAtividade.ContaAlterada, $"Desativou a conta \"{conta.Nome}\"", $"Conta {conta.Nome}", agora: _agora());
        await db.SaveChangesAsync();
    }

    public async Task ReativarAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var conta = await ObterAsync(db, id);
        await GarantirNomeLivreAsync(db, conta.Nome, id);
        conta.Ativa = true;
        await db.SaveChangesAsync();
    }

    // ---------- transferências ----------

    public async Task<TransferenciaConta> TransferirAsync(DadosTransferencia dados, string? operador = null)
    {
        if (dados.ValorCentavos <= 0) throw new RegraDeNegocioException("O valor da transferência deve ser maior que zero.");
        if (dados.ContaOrigemId == dados.ContaDestinoId) throw new RegraDeNegocioException("Escolha duas contas diferentes.");
        if (dados.Data.Date > _agora().Date) throw new RegraDeNegocioException("A data da transferência não pode ser no futuro.");

        await using var db = await _fabrica.CreateDbContextAsync();
        var origem = await ObterAsync(db, dados.ContaOrigemId);
        var destino = await ObterAsync(db, dados.ContaDestinoId);
        if (!origem.Ativa || !destino.Ativa) throw new RegraDeNegocioException("Não dá para transferir de ou para uma conta desativada.");

        var t = new TransferenciaConta
        {
            ContaOrigemId = origem.Id,
            ContaDestinoId = destino.Id,
            ValorCentavos = dados.ValorCentavos,
            Data = dados.Data.Date,
            Descricao = string.IsNullOrWhiteSpace(dados.Descricao) ? null : dados.Descricao.Trim(),
            Operador = string.IsNullOrWhiteSpace(operador) ? OperadorAtual.Nome : operador.Trim(),
            CriadaEm = _agora()
        };
        db.Transferencias.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    public async Task CancelarTransferenciaAsync(int id, string motivo)
    {
        motivo = (motivo ?? "").Trim();
        if (motivo.Length < 3) throw new RegraDeNegocioException("Informe o motivo do cancelamento (pelo menos 3 letras).");
        await using var db = await _fabrica.CreateDbContextAsync();
        var t = await db.Transferencias.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new RegraDeNegocioException("Transferência não encontrada.");
        if (t.Cancelada) throw new RegraDeNegocioException("Essa transferência já está cancelada.");

        t.Cancelada = true;
        t.MotivoCancelamento = motivo;
        t.CanceladaEm = _agora();
        AtividadeService.Registrar(db, TipoAtividade.TransferenciaCancelada,
            $"Cancelou a transferência de {Dinheiro.Formatar(t.ValorCentavos)} de {t.Data:dd/MM/yyyy}: {motivo}", $"Transferência #{t.Id}", agora: t.CanceladaEm);
        await db.SaveChangesAsync();
    }

    public async Task<List<TransferenciaConta>> ListarTransferenciasAsync(DateTime de, DateTime ate)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Transferencias.AsNoTracking()
            .Where(t => t.Data >= de.Date && t.Data <= ate.Date)
            .OrderByDescending(t => t.Data).ThenByDescending(t => t.Id).ToListAsync();
    }

    // ---------- regras ----------

    private static string ValidarNome(string nome)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length < 2) throw new RegraDeNegocioException("Informe o nome da conta.");
        if (nome.Length > 80) throw new RegraDeNegocioException("O nome da conta pode ter no máximo 80 caracteres.");
        return nome;
    }

    private static void Preencher(ContaFinanceira conta, DadosConta dados, string nome)
    {
        conta.Nome = nome;
        conta.Tipo = dados.Tipo;
        conta.SaldoInicialCentavos = dados.SaldoInicialCentavos;
        conta.DataSaldoInicial = dados.DataSaldoInicial?.Date;
        conta.Padrao = dados.Padrao && dados.Tipo == TipoContaFin.Banco;
    }

    private static async Task GarantirNomeLivreAsync(PdvDbContext db, string nome, int? exceto)
    {
        var existe = await db.ContasFinanceiras.AnyAsync(c => c.Ativa && c.Id != exceto && c.Nome.ToLower() == nome.ToLower());
        if (existe) throw new RegraDeNegocioException($"Já existe uma conta chamada \"{nome}\".");
    }

    private static async Task TirarPadraoDasOutrasAsync(PdvDbContext db, TipoContaFin tipo, int? exceto)
    {
        var outras = await db.ContasFinanceiras.Where(c => c.Padrao && c.Tipo == tipo && c.Id != exceto).ToListAsync();
        foreach (var o in outras) o.Padrao = false;
    }

    private static async Task<bool> TemMovimentoAsync(PdvDbContext db, int contaId) =>
        await db.Transferencias.AnyAsync(t => !t.Cancelada && (t.ContaOrigemId == contaId || t.ContaDestinoId == contaId))
        || await db.LiquidacoesCartao.AnyAsync(l => !l.Cancelada && l.ContaId == contaId)
        || await db.Lancamentos.AnyAsync(l => l.ContaId == contaId)
        || await db.ChequesRecebidos.AnyAsync(c => c.ContaId == contaId);

    private static async Task<ContaFinanceira> ObterAsync(PdvDbContext db, int id) =>
        await db.ContasFinanceiras.FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new RegraDeNegocioException("Conta não encontrada.");
}
