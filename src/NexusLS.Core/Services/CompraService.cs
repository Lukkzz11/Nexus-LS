using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosFornecedor(string Nome, string? Documento = null, string? Telefone = null, string? Email = null, string? Observacao = null);

public class FornecedorService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public FornecedorService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<Fornecedor> CadastrarAsync(DadosFornecedor dados)
    {
        Validar(dados);
        var f = new Fornecedor { DataCadastro = _agora(), Ativo = true };
        Preencher(f, dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        await GarantirNomeLivreAsync(db, f.Nome, null);
        db.Fornecedores.Add(f);
        await db.SaveChangesAsync();
        return f;
    }

    public async Task EditarAsync(int id, DadosFornecedor dados)
    {
        Validar(dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await db.Fornecedores.FirstOrDefaultAsync(x => x.Id == id) ?? throw new RegraDeNegocioException("Fornecedor não encontrado.");
        await GarantirNomeLivreAsync(db, dados.Nome.Trim(), id);
        Preencher(f, dados);
        await db.SaveChangesAsync();
    }

    public Task DesativarAsync(int id) => DefinirAtivoAsync(id, false);
    public Task ReativarAsync(int id) => DefinirAtivoAsync(id, true);

    public async Task<List<Fornecedor>> BuscarAsync(string? texto = null, bool incluirInativos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.Fornecedores.AsNoTracking().Where(f => incluirInativos || f.Ativo).ToListAsync();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            lista = lista.Where(f =>
                f.Nome.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
                (f.Documento?.Contains(t, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (f.Telefone?.Contains(t, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();
        }
        return lista.OrderBy(f => f.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private async Task DefinirAtivoAsync(int id, bool ativo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await db.Fornecedores.FirstOrDefaultAsync(x => x.Id == id) ?? throw new RegraDeNegocioException("Fornecedor não encontrado.");
        f.Ativo = ativo;
        await db.SaveChangesAsync();
    }

    private static async Task GarantirNomeLivreAsync(PdvDbContext db, string nome, int? idIgnorado)
    {
        var minusculo = nome.Trim().ToLower();
        var existe = await db.Fornecedores.AnyAsync(f => f.Ativo && f.Id != idIgnorado && f.Nome.ToLower() == minusculo);
        if (existe) throw new RegraDeNegocioException("Já existe um fornecedor ativo com esse nome.");
    }

    private static void Validar(DadosFornecedor dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome)) throw new RegraDeNegocioException("Informe o nome do fornecedor.");
        if (dados.Nome.Trim().Length > 150) throw new RegraDeNegocioException("O nome pode ter no máximo 150 caracteres.");
    }

    private static void Preencher(Fornecedor f, DadosFornecedor d)
    {
        f.Nome = d.Nome.Trim();
        f.Documento = Limpar(d.Documento);
        f.Telefone = Limpar(d.Telefone);
        f.Email = Limpar(d.Email);
        f.Observacao = Limpar(d.Observacao);
    }

    private static string? Limpar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}

public record ItemCompraDados(int ProdutoId, int Quantidade, long CustoUnitarioCentavos, string? Tamanho = null);

/// <summary>Como a compra será paga: gera as contas a pagar (uma por parcela) na mesma operação.</summary>
public record PagamentoCompra(int Parcelas, DateTime PrimeiroVencimento, bool JaPago = false, FormaPagamento? Forma = null);

public record DadosCompra(
    IReadOnlyList<ItemCompraDados> Itens,
    int? FornecedorId = null,
    string? NumeroNota = null,
    string? Observacao = null,
    bool AtualizarCusto = true,
    PagamentoCompra? Pagamento = null);

/// <summary>
/// Registra compras de mercadoria. Tudo acontece numa única transação: a compra, a entrada de estoque de cada item
/// (com movimentação "Compra nº X"), o novo custo do produto (opcional) e as contas a pagar (opcional). Se qualquer
/// parte falhar, nada é gravado. Cancelar devolve o estoque e cancela as contas ainda não pagas.
/// </summary>
public class CompraService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public CompraService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<Compra> RegistrarAsync(DadosCompra dados, string? operador = null)
    {
        if (dados.Itens.Count == 0) throw new RegraDeNegocioException("Adicione pelo menos um produto à compra.");
        foreach (var i in dados.Itens)
        {
            if (i.Quantidade <= 0) throw new RegraDeNegocioException("A quantidade de cada item deve ser maior que zero.");
            if (i.CustoUnitarioCentavos < 0) throw new RegraDeNegocioException("O custo não pode ser negativo.");
        }

        await using var db = await _fabrica.CreateDbContextAsync();
        Fornecedor? fornecedor = null;
        if (dados.FornecedorId is int fid)
        {
            fornecedor = await db.Fornecedores.FirstOrDefaultAsync(f => f.Id == fid) ?? throw new RegraDeNegocioException("Fornecedor não encontrado.");
            if (!fornecedor.Ativo) throw new RegraDeNegocioException("Esse fornecedor está desativado.");
        }

        var agora = _agora();
        var compra = new Compra
        {
            FornecedorId = fornecedor?.Id,
            DataHora = agora,
            NumeroNota = Limpar(dados.NumeroNota),
            Observacao = Limpar(dados.Observacao),
            Operador = Limpar(operador)
        };

        await using var tx = await db.Database.BeginTransactionAsync();

        var produtos = new Dictionary<int, Produto>();
        foreach (var item in dados.Itens)
        {
            if (!produtos.TryGetValue(item.ProdutoId, out var produto))
            {
                produto = await db.Produtos.Include(p => p.Tamanhos).FirstOrDefaultAsync(p => p.Id == item.ProdutoId)
                    ?? throw new RegraDeNegocioException("Produto não encontrado.");
                produtos[item.ProdutoId] = produto;
            }
            var nomeTamanho = NormalizarTamanho(item.Tamanho);
            if (!produto.Tamanhos.Any(t => t.Tamanho == nomeTamanho))
                throw new RegraDeNegocioException($"O produto \"{produto.Nome}\" não tem o tamanho {nomeTamanho}.");

            compra.Itens.Add(new ItemCompra
            {
                ProdutoId = produto.Id,
                NomeProduto = produto.Nome,
                Tamanho = nomeTamanho,
                Quantidade = item.Quantidade,
                CustoUnitarioCentavos = item.CustoUnitarioCentavos
            });
        }
        compra.TotalCentavos = compra.Itens.Sum(i => i.TotalCentavos);

        db.Compras.Add(compra);
        await db.SaveChangesAsync(); // dá o número (Id) da compra

        foreach (var item in compra.Itens)
        {
            var produto = produtos[item.ProdutoId];
            var linha = produto.Tamanhos.Single(t => t.Tamanho == item.Tamanho);
            linha.Estoque += item.Quantidade;
            produto.EstoqueAtual += item.Quantidade;
            if (dados.AtualizarCusto) produto.PrecoCustoCentavos = item.CustoUnitarioCentavos;
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id,
                DataHora = agora,
                Tipo = TipoMovimentacao.Entrada,
                Quantidade = item.Quantidade,
                Tamanho = item.Tamanho,
                Motivo = $"Compra nº {compra.Id}"
            });
        }

        if (dados.Pagamento is { } pg && compra.TotalCentavos > 0)
        {
            var descricao = $"Compra nº {compra.Id}" + (fornecedor is null ? "" : $" - {fornecedor.Nome}");
            var contas = FinanceiroService.Montar(new DadosLancamento(
                TipoLancamento.Despesa, descricao, compra.TotalCentavos, pg.PrimeiroVencimento,
                "Fornecedores", fornecedor?.Nome, compra.NumeroNota is null ? null : $"Nota {compra.NumeroNota}",
                pg.Parcelas, pg.JaPago, pg.Forma), agora, operador, compra.Id);
            db.Lancamentos.AddRange(contas);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return compra;
    }

    public async Task CancelarAsync(int compraId, string motivo)
    {
        if ((motivo ?? "").Trim().Length < 3) throw new RegraDeNegocioException("Informe o motivo do cancelamento (pelo menos 3 letras).");

        await using var db = await _fabrica.CreateDbContextAsync();
        var compra = await db.Compras.Include(c => c.Itens).FirstOrDefaultAsync(c => c.Id == compraId)
            ?? throw new RegraDeNegocioException("Compra não encontrada.");
        if (compra.Cancelada) throw new RegraDeNegocioException("Essa compra já está cancelada.");

        var contas = await db.Lancamentos.Where(l => l.CompraId == compraId && !l.Cancelado).ToListAsync();
        if (contas.Any(c => c.PagoEm is not null))
            throw new RegraDeNegocioException("Há contas desta compra já pagas. Estorne os pagamentos no Financeiro antes de cancelar a compra.");

        var agora = _agora();
        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var item in compra.Itens)
        {
            var produto = await db.Produtos.Include(p => p.Tamanhos).FirstAsync(p => p.Id == item.ProdutoId);
            var linha = produto.Tamanhos.FirstOrDefault(t => t.Tamanho == item.Tamanho)
                ?? throw new RegraDeNegocioException($"O tamanho {item.Tamanho} de \"{produto.Nome}\" não existe mais; ajuste o estoque à mão.");
            if (linha.Estoque < item.Quantidade)
                throw new RegraDeNegocioException(
                    $"Não dá para cancelar: de \"{item.NomeProduto}\" ({item.Tamanho}) entraram {item.Quantidade}, mas só restam {linha.Estoque} (o resto já saiu). Ajuste o estoque à mão.");

            linha.Estoque -= item.Quantidade;
            produto.EstoqueAtual -= item.Quantidade;
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id,
                DataHora = agora,
                Tipo = TipoMovimentacao.Correcao,
                Quantidade = -item.Quantidade,
                Tamanho = item.Tamanho,
                Motivo = $"Compra nº {compra.Id} cancelada"
            });
        }

        foreach (var c in contas)
        {
            c.Cancelado = true;
            c.MotivoCancelamento = $"Compra nº {compra.Id} cancelada";
            c.CanceladoEm = agora;
        }

        compra.Cancelada = true;
        compra.MotivoCancelamento = motivo.Trim();
        compra.CanceladaEm = agora;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Compras do período (da mais recente para a mais antiga), inclusive as canceladas.</summary>
    public async Task<IReadOnlyList<Compra>> ListarAsync(DateTime de, DateTime ate, int? fornecedorId = null)
    {
        var inicio = de.Date;
        var fim = ate.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var consulta = db.Compras.AsNoTracking().Include(c => c.Itens).Include(c => c.Fornecedor)
            .Where(c => c.DataHora >= inicio && c.DataHora < fim);
        if (fornecedorId is int fid) consulta = consulta.Where(c => c.FornecedorId == fid);
        var lista = await consulta.ToListAsync();
        return lista.OrderByDescending(c => c.DataHora).ThenByDescending(c => c.Id).ToList();
    }

    private static string NormalizarTamanho(string? tamanho) =>
        string.IsNullOrWhiteSpace(tamanho) || string.Equals(tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase)
            ? Tamanhos.SemTamanho
            : tamanho.Trim().ToUpperInvariant();

    private static string? Limpar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
