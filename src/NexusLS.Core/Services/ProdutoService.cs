using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public class ProdutoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public ProdutoService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>
    /// Cadastra um produto. Todo produto nasce com a linha "Sem tamanho" na grade, que recebe o estoque
    /// inicial (se houver, com uma movimentação de entrada). Os demais tamanhos entram depois, com AdicionarTamanhoAsync.
    /// "async/await": o método espera o banco responder sem travar a janela; Task é a "promessa" do resultado.
    /// </summary>
    public async Task<Produto> CadastrarAsync(DadosProduto dados, int estoqueInicial = 0)
    {
        if (estoqueInicial < 0)
            throw new RegraDeNegocioException("O estoque inicial não pode ser negativo.");
        var (codigo, referencia) = Validar(dados);

        await using var db = await _fabrica.CreateDbContextAsync();
        await GarantirCodigoLivreAsync(db, codigo, null);

        var produto = new Produto
        {
            Nome = dados.Nome.Trim(),
            CodigoBarras = codigo,
            Referencia = referencia,
            PrecoVendaCentavos = dados.PrecoVendaCentavos,
            PrecoAtacadoCentavos = dados.PrecoAtacadoCentavos,
            PrecoCustoCentavos = dados.PrecoCustoCentavos,
            EstoqueMinimo = dados.EstoqueMinimo,
            EstoqueAtual = estoqueInicial,
            Ativo = true
        };
        produto.Tamanhos.Add(new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho, Estoque = estoqueInicial });
        db.Produtos.Add(produto);

        if (estoqueInicial > 0)
        {
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                Produto = produto,
                DataHora = DateTime.Now,
                Tipo = TipoMovimentacao.Entrada,
                Quantidade = estoqueInicial,
                Tamanho = Tamanhos.SemTamanho,
                Motivo = "Estoque inicial"
            });
        }

        await db.SaveChangesAsync();
        return produto;
    }

    public async Task EditarAsync(int id, DadosProduto dados)
    {
        var (codigo, referencia) = Validar(dados);

        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FindAsync(id)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        await GarantirCodigoLivreAsync(db, codigo, id);

        produto.Nome = dados.Nome.Trim();
        produto.CodigoBarras = codigo;
        produto.Referencia = referencia;
        produto.PrecoVendaCentavos = dados.PrecoVendaCentavos;
        produto.PrecoAtacadoCentavos = dados.PrecoAtacadoCentavos;
        produto.PrecoCustoCentavos = dados.PrecoCustoCentavos;
        produto.EstoqueMinimo = dados.EstoqueMinimo;
        await db.SaveChangesAsync();
    }

    /// <summary>Desativar não apaga: o produto some do PDV, mas o histórico de vendas continua intacto.</summary>
    public Task DesativarAsync(int id) => DefinirAtivoAsync(id, false);

    public Task ReativarAsync(int id) => DefinirAtivoAsync(id, true);

    /// <summary>Busca por parte do nome, da referência ou do código de barras. Sem texto, lista todos.</summary>
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
                (p.Referencia != null && EF.Functions.Like(p.Referencia, padrao)) ||
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

    // ================= Grade de tamanhos =================

    /// <summary>A grade do produto: "Sem tamanho" primeiro, depois os tamanhos na ordem em que foram cadastrados.</summary>
    public async Task<List<ProdutoTamanho>> ListarTamanhosAsync(int produtoId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.TamanhosProduto.AsNoTracking()
            .Where(t => t.ProdutoId == produtoId)
            .OrderBy(t => t.Id)
            .ToListAsync();
    }

    /// <summary>Acrescenta um tamanho (ex.: "M", "38") com estoque zero. O nome é guardado em maiúsculas.</summary>
    public async Task<ProdutoTamanho> AdicionarTamanhoAsync(int produtoId, string tamanho)
    {
        var nome = NormalizarTamanho(tamanho);

        await using var db = await _fabrica.CreateDbContextAsync();
        if (!await db.Produtos.AnyAsync(p => p.Id == produtoId))
            throw new RegraDeNegocioException("Produto não encontrado.");
        if (await db.TamanhosProduto.AnyAsync(t => t.ProdutoId == produtoId && t.Tamanho == nome))
            throw new RegraDeNegocioException($"O produto já tem o tamanho {nome}.");

        var novo = new ProdutoTamanho { ProdutoId = produtoId, Tamanho = nome, Estoque = 0 };
        db.TamanhosProduto.Add(novo);
        await db.SaveChangesAsync();
        return novo;
    }

    /// <summary>Remove um tamanho da grade. Só é possível com estoque zerado; "Sem tamanho" nunca sai.</summary>
    public async Task RemoverTamanhoAsync(int produtoId, string tamanho)
    {
        if (string.Equals(tamanho?.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            throw new RegraDeNegocioException("A opção \"Sem tamanho\" não pode ser removida.");
        var nome = NormalizarTamanho(tamanho!);

        await using var db = await _fabrica.CreateDbContextAsync();
        var linha = await db.TamanhosProduto.FirstOrDefaultAsync(t => t.ProdutoId == produtoId && t.Tamanho == nome)
            ?? throw new RegraDeNegocioException($"O produto não tem o tamanho {nome}.");
        if (linha.Estoque != 0)
            throw new RegraDeNegocioException(
                $"O tamanho {nome} ainda tem {linha.Estoque} unidade(s) em estoque. Zere o estoque antes de remover.");

        db.TamanhosProduto.Remove(linha);
        await db.SaveChangesAsync();
    }

    private static string NormalizarTamanho(string tamanho)
    {
        var nome = (tamanho ?? "").Trim().ToUpperInvariant();
        if (nome.Length == 0)
            throw new RegraDeNegocioException("Informe o tamanho.");
        if (nome.Length > 30)
            throw new RegraDeNegocioException("O tamanho pode ter no máximo 30 caracteres.");
        if (string.Equals(nome, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            throw new RegraDeNegocioException("\"Sem tamanho\" já existe em todo produto.");
        return nome;
    }

    // ================= Apoio =================

    private async Task DefinirAtivoAsync(int id, bool ativo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FindAsync(id)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        produto.Ativo = ativo;
        await db.SaveChangesAsync();
    }

    /// <summary>Valida os campos e devolve código de barras e referência já "limpos" (vazio vira nulo).</summary>
    private static (string? codigo, string? referencia) Validar(DadosProduto dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new RegraDeNegocioException("Informe o nome do produto.");
        if (dados.PrecoVendaCentavos < 0 || dados.PrecoCustoCentavos < 0 || dados.PrecoAtacadoCentavos < 0)
            throw new RegraDeNegocioException("Os preços não podem ser negativos.");
        if (dados.EstoqueMinimo < 0)
            throw new RegraDeNegocioException("O estoque mínimo não pode ser negativo.");

        var codigo = string.IsNullOrWhiteSpace(dados.CodigoBarras) ? null : dados.CodigoBarras.Trim();
        var referencia = string.IsNullOrWhiteSpace(dados.Referencia) ? null : dados.Referencia.Trim();
        return (codigo, referencia);
    }

    private static async Task GarantirCodigoLivreAsync(PdvDbContext db, string? codigo, int? idIgnorado)
    {
        if (codigo is null) return;
        var existe = await db.Produtos.AnyAsync(p => p.CodigoBarras == codigo && p.Id != idIgnorado);
        if (existe)
            throw new RegraDeNegocioException("Já existe um produto com esse código de barras.");
    }
}
