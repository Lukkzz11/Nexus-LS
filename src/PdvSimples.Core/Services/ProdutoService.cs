using Microsoft.EntityFrameworkCore;
using PdvSimples.Core.Data;
using PdvSimples.Core.Models;

namespace PdvSimples.Core.Services;

public class ProdutoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public ProdutoService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>
    /// Cadastra um produto. Se vier estoque inicial, registra uma movimentação de entrada.
    /// "async/await": o método espera o banco responder sem travar a janela; Task é a "promessa" do resultado.
    /// </summary>
    public async Task<Produto> CadastrarAsync(DadosProduto dados, int estoqueInicial = 0)
    {
        if (estoqueInicial < 0)
            throw new RegraDeNegocioException("O estoque inicial não pode ser negativo.");
        var codigo = Validar(dados);

        await using var db = await _fabrica.CreateDbContextAsync();
        await GarantirCodigoLivreAsync(db, codigo, null);

        var produto = new Produto
        {
            Nome = dados.Nome.Trim(),
            CodigoBarras = codigo,
            PrecoVendaCentavos = dados.PrecoVendaCentavos,
            PrecoCustoCentavos = dados.PrecoCustoCentavos,
            EstoqueMinimo = dados.EstoqueMinimo,
            EstoqueAtual = estoqueInicial,
            Ativo = true
        };
        db.Produtos.Add(produto);

        if (estoqueInicial > 0)
        {
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                Produto = produto,
                DataHora = DateTime.Now,
                Tipo = TipoMovimentacao.Entrada,
                Quantidade = estoqueInicial,
                Motivo = "Estoque inicial"
            });
        }

        await db.SaveChangesAsync();
        return produto;
    }

    public async Task EditarAsync(int id, DadosProduto dados)
    {
        var codigo = Validar(dados);

        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FindAsync(id)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        await GarantirCodigoLivreAsync(db, codigo, id);

        produto.Nome = dados.Nome.Trim();
        produto.CodigoBarras = codigo;
        produto.PrecoVendaCentavos = dados.PrecoVendaCentavos;
        produto.PrecoCustoCentavos = dados.PrecoCustoCentavos;
        produto.EstoqueMinimo = dados.EstoqueMinimo;
        await db.SaveChangesAsync();
    }

    /// <summary>Desativar não apaga: o produto some do PDV, mas o histórico de vendas continua intacto.</summary>
    public Task DesativarAsync(int id) => DefinirAtivoAsync(id, false);

    public Task ReativarAsync(int id) => DefinirAtivoAsync(id, true);

    /// <summary>Busca por parte do nome ou do código de barras. Sem texto, lista todos.</summary>
    public async Task<List<Produto>> BuscarAsync(string? texto, bool incluirInativos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        IQueryable<Produto> consulta = db.Produtos.AsNoTracking();

        if (!incluirInativos)
            consulta = consulta.Where(p => p.Ativo);

        texto = texto?.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            var padrao = $"%{texto}%";
            consulta = consulta.Where(p =>
                EF.Functions.Like(p.Nome, padrao) ||
                (p.CodigoBarras != null && EF.Functions.Like(p.CodigoBarras, padrao)));
        }

        return await consulta.OrderBy(p => p.Nome).ToListAsync();
    }

    /// <summary>Usado pelo leitor de código de barras no PDV: só produto ativo, código exato.</summary>
    public async Task<Produto?> ObterPorCodigoBarrasAsync(string codigoBarras)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var codigo = codigoBarras.Trim();
        return await db.Produtos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Ativo && p.CodigoBarras == codigo);
    }

    private async Task DefinirAtivoAsync(int id, bool ativo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FindAsync(id)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        produto.Ativo = ativo;
        await db.SaveChangesAsync();
    }

    /// <summary>Valida os campos e devolve o código de barras já "limpo" (vazio vira nulo).</summary>
    private static string? Validar(DadosProduto dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new RegraDeNegocioException("Informe o nome do produto.");
        if (dados.PrecoVendaCentavos < 0 || dados.PrecoCustoCentavos < 0)
            throw new RegraDeNegocioException("Os preços não podem ser negativos.");
        if (dados.EstoqueMinimo < 0)
            throw new RegraDeNegocioException("O estoque mínimo não pode ser negativo.");

        return string.IsNullOrWhiteSpace(dados.CodigoBarras) ? null : dados.CodigoBarras.Trim();
    }

    private static async Task GarantirCodigoLivreAsync(PdvDbContext db, string? codigo, int? idIgnorado)
    {
        if (codigo is null) return;
        var existe = await db.Produtos.AnyAsync(p => p.CodigoBarras == codigo && p.Id != idIgnorado);
        if (existe)
            throw new RegraDeNegocioException("Já existe um produto com esse código de barras.");
    }
}
