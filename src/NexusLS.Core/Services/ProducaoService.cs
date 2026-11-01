using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Dados de uma matéria-prima no cadastro (o saldo muda por entrada/saída, não pela edição).</summary>
public record DadosMateriaPrima(string Nome, string? Tipo, string? Unidade, decimal EstoqueMinimo, long CustoUnitarioCentavos,
    string? Fornecedor = null, string? Localizacao = null);

/// <summary>Dados de uma ordem de produção nova. A grade é "tamanho → quantidade".</summary>
public record DadosOrdemProducao(string Modelo, IReadOnlyDictionary<string, int> Grade, string? Referencia = null, string? Categoria = null,
    DateTime? PrevisaoTermino = null, string? TecidoUtilizado = null, string? Responsavel = null, long CustoEstimadoPecaCentavos = 0,
    string? Observacoes = null);

public record DadosPerda(MotivoPerda Motivo, decimal Quantidade, string? MaterialNome = null, int? MateriaPrimaId = null, int? OrdemProducaoId = null,
    string? Unidade = null, long? CustoEstimadoCentavos = null, string? Observacao = null, bool BaixarDoEstoque = true);

/// <summary>Para onde vai a produção: um produto que já existe na loja, ou um novo (nome e preços).</summary>
public record DadosTransferenciaFabrica(IReadOnlyDictionary<string, int> Grade, int? ProdutoId = null, string? Nome = null, string? Referencia = null,
    string? Categoria = null, long PrecoVendaCentavos = 0, long PrecoAtacadoCentavos = 0);

public record ResultadoTransferenciaFabrica(int ProdutoId, string ProdutoNome, int TotalPecas, bool ProdutoNovo);

/// <summary>
/// Fábrica: matéria-prima, ordens de produção, perdas e a transferência da produção pronta para o estoque da loja.
/// Tudo localmente no SQLite; nada aqui mexe em vendas.
/// </summary>
public class ProducaoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly ProdutoService _produtos;
    private readonly Func<DateTime> _agora;

    public ProducaoService(IDbContextFactory<PdvDbContext> fabrica, ProdutoService produtos, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _produtos = produtos;
        _agora = agora ?? (() => DateTime.Now);
    }

    // ================= matéria-prima =================

    public async Task<List<MateriaPrima>> ListarMateriasAsync(bool incluirInativas = false, bool soAbaixoDoMinimo = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.MateriasPrimas.AsNoTracking().Where(m => incluirInativas || m.Ativo).ToListAsync();
        if (soAbaixoDoMinimo) lista = lista.Where(m => m.AbaixoDoMinimo).ToList(); // decimal: compara na memória (SQLite não compara decimais)
        return lista.OrderBy(m => m.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<MateriaPrima> CadastrarMateriaAsync(DadosMateriaPrima dados, decimal estoqueInicial = 0)
    {
        ValidarMateria(dados);
        if (estoqueInicial < 0) throw new RegraDeNegocioException("O estoque inicial não pode ser negativo.");
        await using var db = await _fabrica.CreateDbContextAsync();
        await GarantirNomeLivreAsync(db, dados.Nome, null);
        var materia = new MateriaPrima { EstoqueAtual = estoqueInicial, AtualizadoEm = _agora() };
        Aplicar(materia, dados);
        db.MateriasPrimas.Add(materia);
        await db.SaveChangesAsync();
        return materia;
    }

    public async Task<MateriaPrima> EditarMateriaAsync(int id, DadosMateriaPrima dados)
    {
        ValidarMateria(dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        var materia = await db.MateriasPrimas.FindAsync(id) ?? throw new RegraDeNegocioException("Matéria-prima não encontrada.");
        await GarantirNomeLivreAsync(db, dados.Nome, id);
        Aplicar(materia, dados);
        materia.AtualizadoEm = _agora();
        await db.SaveChangesAsync();
        return materia;
    }

    /// <summary>Soma (entrada, positivo) ou tira (saída, negativo) do saldo. O saldo nunca fica negativo.</summary>
    public async Task<MateriaPrima> AjustarEstoqueMateriaAsync(int id, decimal variacao)
    {
        if (variacao == 0) throw new RegraDeNegocioException("A quantidade não pode ser zero.");
        await using var db = await _fabrica.CreateDbContextAsync();
        var materia = await db.MateriasPrimas.FindAsync(id) ?? throw new RegraDeNegocioException("Matéria-prima não encontrada.");
        if (materia.EstoqueAtual + variacao < 0)
            throw new RegraDeNegocioException($"Estoque insuficiente: \"{materia.Nome}\" tem {materia.EstoqueAtual:0.###} {materia.Unidade}.");
        materia.EstoqueAtual += variacao;
        materia.AtualizadoEm = _agora();
        await db.SaveChangesAsync();
        return materia;
    }

    public async Task DefinirMateriaAtivaAsync(int id, bool ativa)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var materia = await db.MateriasPrimas.FindAsync(id) ?? throw new RegraDeNegocioException("Matéria-prima não encontrada.");
        materia.Ativo = ativa;
        materia.AtualizadoEm = _agora();
        await db.SaveChangesAsync();
    }

    private static void ValidarMateria(DadosMateriaPrima d)
    {
        if (string.IsNullOrWhiteSpace(d.Nome)) throw new RegraDeNegocioException("Informe o nome da matéria-prima.");
        if (d.EstoqueMinimo < 0) throw new RegraDeNegocioException("O estoque mínimo não pode ser negativo.");
        if (d.CustoUnitarioCentavos < 0) throw new RegraDeNegocioException("O custo não pode ser negativo.");
    }

    private static void Aplicar(MateriaPrima m, DadosMateriaPrima d)
    {
        m.Nome = d.Nome.Trim();
        m.Tipo = d.Tipo?.Trim() ?? "";
        m.Unidade = string.IsNullOrWhiteSpace(d.Unidade) ? "un" : d.Unidade.Trim();
        m.EstoqueMinimo = d.EstoqueMinimo;
        m.CustoUnitarioCentavos = d.CustoUnitarioCentavos;
        m.Fornecedor = string.IsNullOrWhiteSpace(d.Fornecedor) ? null : d.Fornecedor.Trim();
        m.Localizacao = string.IsNullOrWhiteSpace(d.Localizacao) ? null : d.Localizacao.Trim();
    }

    private static async Task GarantirNomeLivreAsync(PdvDbContext db, string nome, int? ignorarId)
    {
        var n = nome.Trim().ToLower();
        var existe = (await db.MateriasPrimas.AsNoTracking().Where(m => m.Id != ignorarId).Select(m => m.Nome).ToListAsync())
            .Any(x => x.Trim().ToLower() == n);
        if (existe) throw new RegraDeNegocioException($"Já existe a matéria-prima \"{nome.Trim()}\".");
    }

    // ================= ordens de produção =================

    public async Task<List<OrdemProducao>> ListarOrdensAsync(bool incluirEncerradas = true)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var ordens = await db.OrdensProducao.AsNoTracking().Include(o => o.Grade).ToListAsync();
        if (!incluirEncerradas)
            ordens = ordens.Where(o => o.Status is not (StatusOrdemProducao.Concluida or StatusOrdemProducao.Cancelada)).ToList();
        return ordens.OrderByDescending(o => o.Id).ToList();
    }

    public async Task<OrdemProducao> CriarOrdemAsync(DadosOrdemProducao dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Modelo)) throw new RegraDeNegocioException("Informe o modelo da ordem de produção.");
        if (dados.CustoEstimadoPecaCentavos < 0) throw new RegraDeNegocioException("O custo estimado não pode ser negativo.");
        var grade = NormalizarGrade(dados.Grade);
        if (grade.Count == 0) throw new RegraDeNegocioException("Informe pelo menos uma peça na grade da ordem.");

        await using var db = await _fabrica.CreateDbContextAsync();
        var agora = _agora();
        var prefixo = $"OP-{agora.Year}-";
        var existentes = await db.OrdensProducao.AsNoTracking().Where(o => o.Codigo.StartsWith(prefixo)).Select(o => o.Codigo).ToListAsync();
        var proximo = existentes.Select(c => int.TryParse(c[prefixo.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;

        var ordem = new OrdemProducao
        {
            Codigo = $"{prefixo}{proximo:D4}",
            Modelo = dados.Modelo.Trim(),
            Referencia = Limpar(dados.Referencia),
            Categoria = Limpar(dados.Categoria),
            PrevisaoTermino = dados.PrevisaoTermino?.Date,
            TecidoUtilizado = Limpar(dados.TecidoUtilizado),
            Responsavel = Limpar(dados.Responsavel) ?? "Fábrica",
            CustoEstimadoPecaCentavos = dados.CustoEstimadoPecaCentavos,
            Observacoes = Limpar(dados.Observacoes),
            CriadoEm = agora,
            AtualizadoEm = agora,
            Grade = grade.Select(g => new OrdemProducaoGrade { Tamanho = g.Key, Quantidade = g.Value }).ToList()
        };
        db.OrdensProducao.Add(ordem);
        await db.SaveChangesAsync();
        return ordem;
    }

    /// <summary>Muda a etapa (corte, costura...). "Concluída" só vem pela transferência; ordem encerrada não muda mais.</summary>
    public async Task<OrdemProducao> MudarEtapaAsync(int ordemId, StatusOrdemProducao novo)
    {
        if (novo == StatusOrdemProducao.Concluida)
            throw new RegraDeNegocioException("A ordem só é concluída ao transferir a produção para a loja.");
        await using var db = await _fabrica.CreateDbContextAsync();
        var ordem = await db.OrdensProducao.Include(o => o.Grade).FirstOrDefaultAsync(o => o.Id == ordemId)
            ?? throw new RegraDeNegocioException("Ordem de produção não encontrada.");
        if (ordem.Status == StatusOrdemProducao.Concluida)
            throw new RegraDeNegocioException("Esta ordem já foi transferida para a loja.");
        if (ordem.Status == StatusOrdemProducao.Cancelada)
            throw new RegraDeNegocioException("Esta ordem está cancelada.");
        ordem.Status = novo;
        ordem.AtualizadoEm = _agora();
        await db.SaveChangesAsync();
        return ordem;
    }

    /// <summary>Passa para a etapa seguinte (planejamento → corte → costura → estampa → acabamento).</summary>
    public async Task<OrdemProducao> AvancarEtapaAsync(int ordemId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var atual = (await db.OrdensProducao.AsNoTracking().FirstOrDefaultAsync(o => o.Id == ordemId))?.Status
            ?? throw new RegraDeNegocioException("Ordem de produção não encontrada.");
        if (atual == StatusOrdemProducao.Acabamento)
            throw new RegraDeNegocioException("A ordem já está no acabamento: transfira a produção para a loja para concluir.");
        if (atual is StatusOrdemProducao.Concluida or StatusOrdemProducao.Cancelada)
            return await MudarEtapaAsync(ordemId, atual); // devolve o erro certo (concluída ou cancelada)
        return await MudarEtapaAsync(ordemId, atual + 1);
    }

    public Task<OrdemProducao> CancelarOrdemAsync(int ordemId) => MudarEtapaAsync(ordemId, StatusOrdemProducao.Cancelada);

    // ================= perdas =================

    public async Task<List<PerdaProducao>> ListarPerdasAsync(int limite = 200)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.PerdasProducao.AsNoTracking().OrderByDescending(p => p.DataHora).ThenByDescending(p => p.Id).Take(limite).ToListAsync();
    }

    /// <summary>
    /// Registra uma perda. Com matéria-prima escolhida e "baixar do estoque", tira a quantidade do saldo dela (sem deixar negativo),
    /// tudo na mesma gravação; o custo estimado, se não informado, é quantidade × custo da matéria-prima.
    /// </summary>
    public async Task<PerdaProducao> RegistrarPerdaAsync(DadosPerda dados)
    {
        if (dados.Quantidade <= 0) throw new RegraDeNegocioException("A quantidade perdida deve ser maior que zero.");
        if (dados.CustoEstimadoCentavos < 0) throw new RegraDeNegocioException("O custo estimado não pode ser negativo.");

        await using var db = await _fabrica.CreateDbContextAsync();
        MateriaPrima? materia = null;
        if (dados.MateriaPrimaId is int mid)
            materia = await db.MateriasPrimas.FindAsync(mid) ?? throw new RegraDeNegocioException("Matéria-prima não encontrada.");
        var nome = Limpar(dados.MaterialNome) ?? materia?.Nome;
        if (nome is null) throw new RegraDeNegocioException("Informe o material perdido.");

        OrdemProducao? ordem = null;
        if (dados.OrdemProducaoId is int oid)
            ordem = await db.OrdensProducao.FindAsync(oid) ?? throw new RegraDeNegocioException("Ordem de produção não encontrada.");

        var baixar = materia is not null && dados.BaixarDoEstoque;
        if (baixar)
        {
            if (materia!.EstoqueAtual < dados.Quantidade)
                throw new RegraDeNegocioException($"Estoque insuficiente: \"{materia.Nome}\" tem {materia.EstoqueAtual:0.###} {materia.Unidade}.");
            materia.EstoqueAtual -= dados.Quantidade;
            materia.AtualizadoEm = _agora();
        }

        var custo = dados.CustoEstimadoCentavos
            ?? (materia is null ? 0 : (long)Math.Round(materia.CustoUnitarioCentavos * dados.Quantidade, MidpointRounding.AwayFromZero));
        var perda = new PerdaProducao
        {
            DataHora = _agora(),
            OrdemProducaoId = ordem?.Id,
            OrdemCodigo = ordem?.Codigo,
            Motivo = dados.Motivo,
            MateriaPrimaId = materia?.Id,
            MaterialNome = nome,
            Quantidade = dados.Quantidade,
            Unidade = Limpar(dados.Unidade) ?? materia?.Unidade ?? "un",
            CustoEstimadoCentavos = custo,
            BaixouEstoque = baixar,
            Observacao = Limpar(dados.Observacao),
            RegistradoPor = Limpar(OperadorAtual.Nome)
        };
        db.PerdasProducao.Add(perda);
        await db.SaveChangesAsync();
        return perda;
    }

    // ================= transferência para a loja =================

    /// <summary>
    /// Passa a produção pronta para o estoque da loja: soma a grade no produto de destino (escolhido, ou achado pela mesma referência/nome,
    /// ou criado), registra as entradas no histórico de estoque e conclui a ordem, tudo numa gravação só. Uma ordem já concluída ou
    /// cancelada não transfere de novo.
    /// </summary>
    public async Task<ResultadoTransferenciaFabrica> TransferirParaLojaAsync(int ordemId, DadosTransferenciaFabrica dados)
    {
        var grade = NormalizarGrade(dados.Grade);
        if (grade.Count == 0) throw new RegraDeNegocioException("Informe quantas peças de cada tamanho vão para a loja.");

        // 1) a ordem (confere antes de criar qualquer produto)
        OrdemProducao ordem;
        await using (var leitura = await _fabrica.CreateDbContextAsync())
        {
            ordem = await leitura.OrdensProducao.AsNoTracking().FirstOrDefaultAsync(o => o.Id == ordemId)
                ?? throw new RegraDeNegocioException("Ordem de produção não encontrada.");
        }
        if (ordem.Status == StatusOrdemProducao.Concluida) throw new RegraDeNegocioException("Esta ordem de produção já foi transferida para a loja.");
        if (ordem.Status == StatusOrdemProducao.Cancelada) throw new RegraDeNegocioException("Esta ordem está cancelada.");

        // 2) o produto de destino
        var (produtoId, novo) = await ResolverProdutoAsync(ordem, dados);

        // 3) estoque + histórico + ordem concluída, juntos
        await using var db = await _fabrica.CreateDbContextAsync();
        var ordemDb = await db.OrdensProducao.FindAsync(ordemId);
        if (ordemDb!.Status is StatusOrdemProducao.Concluida or StatusOrdemProducao.Cancelada)
            throw new RegraDeNegocioException("Esta ordem de produção já foi transferida para a loja.");
        var produto = await db.Produtos.Include(p => p.Tamanhos).FirstAsync(p => p.Id == produtoId);

        var agora = _agora();
        var total = 0;
        foreach (var (tam, qtd) in grade)
        {
            var linha = produto.Tamanhos.FirstOrDefault(t => string.Equals(t.Tamanho, tam, StringComparison.OrdinalIgnoreCase));
            if (linha is null)
            {
                linha = new ProdutoTamanho { Tamanho = tam, Estoque = 0 };
                produto.Tamanhos.Add(linha);
            }
            linha.Estoque += qtd;
            produto.EstoqueAtual += qtd;
            total += qtd;
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                Produto = produto, DataHora = agora, Tipo = TipoMovimentacao.Entrada, Quantidade = qtd,
                Tamanho = linha.Tamanho, Motivo = $"Transferência da fábrica ({ordem.Codigo})"
            });
        }
        produto.AtualizadoEm = DateTime.UtcNow;

        ordemDb.Status = StatusOrdemProducao.Concluida;
        ordemDb.TransferidaEm = agora;
        ordemDb.ProdutoDestinoId = produto.Id;
        ordemDb.AtualizadoEm = agora;
        await db.SaveChangesAsync();

        return new ResultadoTransferenciaFabrica(produto.Id, produto.Nome, total, novo);
    }

    private async Task<(int Id, bool Novo)> ResolverProdutoAsync(OrdemProducao ordem, DadosTransferenciaFabrica dados)
    {
        if (dados.ProdutoId is int id)
        {
            await using var db = await _fabrica.CreateDbContextAsync();
            var p = await db.Produtos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw new RegraDeNegocioException("Produto de destino não encontrado.");
            return (p.Id, false);
        }

        var nome = Limpar(dados.Nome) ?? ordem.Modelo;
        var referencia = Limpar(dados.Referencia) ?? ordem.Referencia;
        await using (var db = await _fabrica.CreateDbContextAsync())
        {
            // o mesmo produto: pela referência; sem referência, pelo mesmo nome
            var candidatos = await db.Produtos.AsNoTracking().Select(p => new { p.Id, p.Nome, p.Referencia }).ToListAsync();
            var achado = referencia is not null
                ? candidatos.FirstOrDefault(p => string.Equals(p.Referencia, referencia, StringComparison.OrdinalIgnoreCase))
                : candidatos.FirstOrDefault(p => string.Equals(p.Nome.Trim(), nome.Trim(), StringComparison.OrdinalIgnoreCase));
            if (achado is not null) return (achado.Id, false);
        }

        var criado = await _produtos.CadastrarAsync(new DadosProduto(nome, null, dados.PrecoVendaCentavos, ordem.CustoEstimadoPecaCentavos, 0,
            referencia, dados.PrecoAtacadoCentavos, Limpar(dados.Categoria) ?? ordem.Categoria));
        return (criado.Id, true);
    }

    // ================= utilidades =================

    /// <summary>Tamanhos em maiúsculas, quantidades positivas, repetidos somados; zero some. "Sem tamanho" fica como está.</summary>
    private static Dictionary<string, int> NormalizarGrade(IReadOnlyDictionary<string, int> grade)
    {
        var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tam, qtd) in grade)
        {
            if (qtd < 0) throw new RegraDeNegocioException("A quantidade de peças não pode ser negativa.");
            if (qtd == 0) continue;
            var nome = string.IsNullOrWhiteSpace(tam) || string.Equals(tam.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase)
                ? Tamanhos.SemTamanho : tam.Trim().ToUpperInvariant();
            resultado[nome] = resultado.GetValueOrDefault(nome) + qtd;
        }
        return resultado;
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
