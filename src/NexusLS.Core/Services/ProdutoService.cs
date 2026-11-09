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
        await GarantirNaoDuplicadoAsync(db, dados.Nome, referencia, null);

        var produto = new Produto
        {
            Nome = dados.Nome.Trim(),
            CodigoInterno = await ProximoCodigoInternoAsync(db),
            Categoria = Limpar(dados.Categoria),
            Cores = NormalizarCores(dados.Cores),
            VenderSemBaixa = dados.VenderSemBaixa,
            Sobra = dados.Sobra,
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
        await GarantirNaoDuplicadoAsync(db, dados.Nome, referencia, id);

        produto.Nome = dados.Nome.Trim();
        produto.Categoria = Limpar(dados.Categoria);
        produto.Cores = NormalizarCores(dados.Cores);
        produto.VenderSemBaixa = dados.VenderSemBaixa;
        produto.Sobra = dados.Sobra;
        produto.CodigoInterno ??= await ProximoCodigoInternoAsync(db); // produtos antigos ganham o código ao serem editados
        produto.CodigoBarras = codigo;
        produto.Referencia = referencia;
        produto.PrecoVendaCentavos = dados.PrecoVendaCentavos;
        produto.PrecoAtacadoCentavos = dados.PrecoAtacadoCentavos;
        produto.PrecoCustoCentavos = dados.PrecoCustoCentavos;
        produto.EstoqueMinimo = dados.EstoqueMinimo;
        await db.SaveChangesAsync();
    }

    /// <summary>Muda só o preço de venda (varejo) do produto; nada mais é tocado. Usado pela calculadora de preços.</summary>
    public async Task AplicarPrecoVendaAsync(int id, long precoVendaCentavos)
    {
        if (precoVendaCentavos <= 0)
            throw new RegraDeNegocioException("O preço de venda precisa ser maior que zero.");
        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FindAsync(id)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        produto.PrecoVendaCentavos = precoVendaCentavos;
        await db.SaveChangesAsync();
    }

    /// <summary>Marca ou desmarca a opção 'Vender sem baixa' de todos os produtos ativos de uma só vez.</summary>
    public async Task<int> MarcarTodosVenderSemBaixaAsync(bool venderSemBaixa = true)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var total = await db.Produtos.Where(p => p.Ativo)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.VenderSemBaixa, venderSemBaixa));
        return total;
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
                (p.CodigoInterno != null && EF.Functions.Like(p.CodigoInterno, padrao)) ||
                (p.Categoria != null && EF.Functions.Like(p.Categoria, padrao)) ||
                (p.CodigoBarras != null && EF.Functions.Like(p.CodigoBarras, padrao)) ||
                (p.Referencia != null && p.Referencia.Replace(" ", "") == texto.Replace(" ", "")));

            // A referência vem sempre primeiro: igual ao digitado, depois começando por ele, depois o resto (nome etc.).
            var exato = texto;
            var semEspaco = texto.Replace(" ", "");
            var comeca = $"{texto}%";
            return await consulta
                .OrderBy(p => p.Referencia != null && (p.Referencia == exato || p.Referencia.Replace(" ", "") == semEspaco) ? 0
                            : p.Referencia != null && EF.Functions.Like(p.Referencia, comeca) ? 1
                            : 2)
                .ThenBy(p => p.Nome)
                .ToListAsync();
        }

        return await consulta.OrderBy(p => p.Nome).ToListAsync();
    }

    /// <summary>Produto ativo cuja referência é exatamente o texto (ignora maiúsculas e espaços).</summary>
    public async Task<Produto?> ObterPorReferenciaAsync(string referencia)
    {
        var texto = (referencia ?? "").Trim();
        if (texto.Length == 0) return null;
        var semEspaco = texto.Replace(" ", "");
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Produtos.AsNoTracking()
            .Where(p => p.Ativo && p.Referencia != null
                        && (p.Referencia == texto || p.Referencia.Replace(" ", "") == semEspaco))
            .OrderBy(p => p.Nome)
            .FirstOrDefaultAsync();
    }

    /// <summary>Todos os produtos com a grade de tamanhos carregada, para exportar o estoque.</summary>
    public async Task<List<Produto>> ListarComTamanhosAsync(bool incluirInativos = true)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        IQueryable<Produto> consulta = db.Produtos.AsNoTracking().Include(p => p.Tamanhos);
        if (!incluirInativos) consulta = consulta.Where(p => p.Ativo);
        return await consulta.OrderBy(p => p.Nome).ToListAsync();
    }

    /// <summary>Quantas sobras da loja (produtos ativos marcados como sobra) existem.</summary>
    public async Task<int> ContarSobrasAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Produtos.AsNoTracking().CountAsync(p => p.Sobra && p.Ativo);
    }

    public async Task<Produto?> ObterAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Produtos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>Usado pelo leitor de código de barras no PDV: só produto ativo, código exato.</summary>
    public async Task<Produto?> ObterPorCodigoBarrasAsync(string codigoBarras)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var codigo = codigoBarras.Trim();
        return await db.Produtos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Ativo && (p.CodigoBarras == codigo || p.CodigoInterno == codigo));
    }

    /// <summary>
    /// A escolha do Enter na busca do PDV: se o operador desceu com ↑↓ até um item, vale esse (<paramref name="destacado"/>);
    /// senão a referência exata tem prioridade, depois o código de barras exato (leitor); senão vale o primeiro resultado da busca. Nulo se não achar nada.
    /// </summary>
    public async Task<Produto?> EscolherParaVendaAsync(string texto, Produto? destacado = null)
    {
        if (destacado is not null) return destacado;

        texto = texto.Trim();
        if (texto.Length == 0) return null;

        // Referência primeiro; só depois o código de barras exato (leitor); por fim o primeiro resultado da busca.
        return await ObterPorReferenciaAsync(texto)
            ?? await ObterPorCodigoBarrasAsync(texto)
            ?? (await BuscarAsync(texto)).FirstOrDefault();
    }

    /// <summary>O próximo código de barras EAN-13 interno livre (prefixo 2). Só gera: quem grava é o cadastro do produto.</summary>
    public async Task<string> GerarCodigoBarrasInternoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var existentes = await db.Produtos.AsNoTracking()
            .Where(p => p.CodigoBarras != null && p.CodigoBarras.StartsWith("2") && p.CodigoBarras.Length == 13)
            .Select(p => p.CodigoBarras!).ToListAsync();

        var maior = existentes.Select(CodigoBarrasInterno.SequenciaDe).Where(s => s is not null).Select(s => s!.Value)
            .DefaultIfEmpty(0).Max();
        return CodigoBarrasInterno.Montar(maior + 1);
    }

    /// <summary>Categorias já usadas nos produtos, em ordem alfabética (para sugerir na digitação).</summary>
    public async Task<List<string>> ListarCategoriasAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Produtos.AsNoTracking().Where(p => p.Categoria != null)
            .Select(p => p.Categoria!).Distinct().OrderBy(c => c).ToListAsync();
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
    /// <summary>"preto,  branco ,PRETO" vira "preto, branco": tira espaços, repetidos e itens vazios. Nulo se não sobrar nada.</summary>
    public static string? NormalizarCores(string? texto)
    {
        var cores = (texto ?? "").Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        return cores.Count == 0 ? null : string.Join(", ", cores);
    }

    /// <summary>
    /// Cadastra de uma vez a grade pronta (PP..G10, 36..70 ou 1..10). Tamanhos que o produto já tem ficam como estão, com o
    /// estoque deles. Devolve só os que foram criados (com estoque zero; o estoque entra por ajuste).
    /// </summary>
    public async Task<List<ProdutoTamanho>> AdicionarGradeAsync(int produtoId, IEnumerable<string> tamanhos)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        if (!await db.Produtos.AnyAsync(p => p.Id == produtoId))
            throw new RegraDeNegocioException("Produto não encontrado.");

        var existentes = (await db.TamanhosProduto.Where(t => t.ProdutoId == produtoId).Select(t => t.Tamanho).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var criados = new List<ProdutoTamanho>();
        foreach (var t in tamanhos.Select(NormalizarTamanho).Distinct())
        {
            if (existentes.Contains(t)) continue;
            var novo = new ProdutoTamanho { ProdutoId = produtoId, Tamanho = t, Estoque = 0 };
            db.TamanhosProduto.Add(novo);
            criados.Add(novo);
        }
        if (criados.Count > 0) await db.SaveChangesAsync();
        return criados;
    }

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

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>INT-000001, INT-000002... sempre o maior já usado + 1 (nunca reaproveita número).</summary>
    private static async Task<string> ProximoCodigoInternoAsync(PdvDbContext db)
    {
        var codigos = await db.Produtos.AsNoTracking().Where(p => p.CodigoInterno != null)
            .Select(p => p.CodigoInterno!).ToListAsync();
        var maior = codigos
            .Select(c => c.StartsWith("INT-") && int.TryParse(c.AsSpan(4), out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return $"INT-{maior + 1:D6}";
    }

    /// <summary>Dois produtos ATIVOS não podem ter o mesmo nome E a mesma referência (nome igual com referência diferente pode).</summary>
    private static async Task GarantirNaoDuplicadoAsync(PdvDbContext db, string nome, string? referencia, int? idIgnorado)
    {
        var nomeMinusculo = nome.Trim().ToLower();
        var candidatos = await db.Produtos.AsNoTracking()
            .Where(p => p.Ativo && p.Id != idIgnorado && p.Nome.ToLower() == nomeMinusculo)
            .Select(p => p.Referencia).ToListAsync();

        if (candidatos.Any(r => string.Equals(r, referencia, StringComparison.OrdinalIgnoreCase)))
            throw new RegraDeNegocioException(
                referencia is null
                    ? "Já existe um produto com esse nome. Se for outro modelo, informe uma referência diferente."
                    : "Já existe um produto com esse nome e essa referência.");
    }

    private static async Task GarantirCodigoLivreAsync(PdvDbContext db, string? codigo, int? idIgnorado)
    {
        if (codigo is null) return;
        var existe = await db.Produtos.AnyAsync(p => p.CodigoBarras == codigo && p.Id != idIgnorado);
        if (existe)
            throw new RegraDeNegocioException("Já existe um produto com esse código de barras.");
    }
}
