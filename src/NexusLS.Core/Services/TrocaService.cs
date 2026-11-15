using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Uma linha da venda vista para a troca: quanto foi vendido, quanto já voltou e quanto ainda pode voltar.</summary>
/// <param name="ValorLinhaCentavos">Quanto o cliente pagou pela linha inteira (preço da linha com o desconto da venda repartido).</param>
public record ItemParaTroca(int ItemVendaId, int ProdutoId, string NomeProduto, string Tamanho, int Vendidos, int JaDevolvidos,
    long ValorLinhaCentavos, long ValorJaDevolvidoCentavos = 0)
{
    public int Disponiveis => Vendidos - JaDevolvidos;

    /// <summary>Quanto vale devolver <paramref name="quantidade"/> peças agora (o mesmo cálculo da gravação).</summary>
    public long ValorPara(int quantidade) => quantidade <= 0 || quantidade > Disponiveis
        ? 0
        : TrocaService.ValorDaDevolucao(ValorLinhaCentavos, Vendidos, JaDevolvidos, quantidade, ValorJaDevolvidoCentavos);

    /// <summary>Valor de uma peça (para mostrar; o valor exato de cada devolução sai de <see cref="TrocaService.ValorDaDevolucao"/>).</summary>
    public long ValorUnitarioCentavos => Vendidos == 0 ? 0 : (long)Math.Round(ValorLinhaCentavos / (decimal)Vendidos, MidpointRounding.AwayFromZero);
}

public record VendaParaTroca(int VendaId, DateTime DataHora, long TotalCentavos, FormaPagamento Forma, string? Cliente,
    IReadOnlyList<ItemParaTroca> Itens);

/// <summary>Quantas peças de cada item da venda voltam.</summary>
public record ItemADevolver(int ItemVendaId, int Quantidade);

public record ResultadoDevolucao(Devolucao Devolucao, ValeTroca? Vale);

/// <summary>Situação de um vale: valor, quanto já foi usado e o saldo que ainda paga.</summary>
public record SaldoVale(int ValeId, string Codigo, long ValorCentavos, long UsadoCentavos, bool Cancelado, DateTime CriadoEm,
    string? Cliente, int? VendaOrigemId)
{
    public long SaldoCentavos => Cancelado ? 0 : ValorCentavos - UsadoCentavos;
}

public record ResumoDevolucao(int Id, DateTime DataHora, int VendaId, string? Cliente, long ValorCentavos, int Pecas, string Motivo,
    string? Operador, string? CodigoVale, bool Cancelada, string? MotivoCancelamento);

/// <summary>
/// Troca de mercadoria: a peça devolvida volta ao estoque (do tamanho de onde saiu) e o valor pago por ela vira um
/// vale-troca, que o PDV aceita como pagamento (inteiro ou em parte). Tudo numa transação, com a trilha de atividades.
///
/// Regras:
///  - A venda original nunca muda. Não se devolve mais peças do que foram vendidas (somando as devoluções anteriores).
///  - O valor é o que o cliente pagou: o preço da linha com o desconto da venda repartido; o acréscimo do cartão não volta.
///  - Venda cancelada não troca; venda a prazo só troca depois de quitada (senão o vale seria crédito de algo não pago).
///  - Venda com devolução não se cancela (o vale já foi entregue); a devolução só se cancela se o vale não foi usado.
/// </summary>
public class TrocaService
{
    private const string Alfabeto = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // sem 0/O e 1/I, que se confundem no papel
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public TrocaService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>A venda com o que ainda pode voltar de cada item. Erro se a venda não existe ou não pode trocar.</summary>
    public async Task<VendaParaTroca> VendaParaTrocaAsync(int vendaId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var venda = await db.Vendas.AsNoTracking().Include(v => v.Itens).Include(v => v.Cliente).FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException($"Venda #{vendaId} não encontrada.");
        await ExigirQuePodeTrocarAsync(db, venda);
        var devolvidos = await DevolvidosPorItemAsync(db, venda.Id);
        var valoresJa = await ValoresDevolvidosPorItemAsync(db, venda.Id);
        var partes = PartesDasLinhas(venda);
        var itens = venda.Itens.OrderBy(i => i.Id)
            .Select(i => new ItemParaTroca(i.Id, i.ProdutoId, i.NomeProduto, i.Tamanho, i.Quantidade,
                devolvidos.GetValueOrDefault(i.Id), partes[i.Id], valoresJa.GetValueOrDefault(i.Id)))
            .ToList();
        return new VendaParaTroca(venda.Id, venda.DataHora, venda.TotalCentavos, venda.FormaPagamento, venda.Cliente?.Nome, itens);
    }

    /// <summary>
    /// Registra a devolução: as peças voltam ao estoque e o valor vira um vale-troca (com código único). Tudo ou nada.
    /// </summary>
    public async Task<ResultadoDevolucao> DevolverAsync(int vendaId, IReadOnlyList<ItemADevolver> itens, string motivo)
    {
        var texto = (motivo ?? "").Trim();
        if (texto.Length < 3) throw new RegraDeNegocioException("Informe o motivo da troca (pelo menos 3 letras).");
        if (texto.Length > 200) throw new RegraDeNegocioException("O motivo pode ter no máximo 200 caracteres.");
        var pedidos = itens.Where(i => i.Quantidade != 0).ToList();
        if (pedidos.Count == 0) throw new RegraDeNegocioException("Escolha pelo menos uma peça para devolver.");
        if (pedidos.Any(i => i.Quantidade < 0)) throw new RegraDeNegocioException("A quantidade devolvida não pode ser negativa.");
        if (pedidos.GroupBy(i => i.ItemVendaId).Any(g => g.Count() > 1))
            throw new RegraDeNegocioException("Cada item da venda aparece uma vez só na devolução.");

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        var venda = await db.Vendas.AsNoTracking().Include(v => v.Itens).Include(v => v.Cliente).FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException($"Venda #{vendaId} não encontrada.");
        await ExigirQuePodeTrocarAsync(db, venda);

        var devolvidos = await DevolvidosPorItemAsync(db, venda.Id);
        var valoresJa = await ValoresDevolvidosPorItemAsync(db, venda.Id);
        var partes = PartesDasLinhas(venda);
        var agora = _agora();
        var operador = await NomeDoOperadorAsync(db);

        var devolucao = new Devolucao
        {
            VendaId = venda.Id,
            ClienteId = venda.ClienteId,
            DataHora = agora,
            Operador = operador,
            Motivo = texto
        };

        var ids = venda.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var grades = await db.TamanhosProduto.Where(t => ids.Contains(t.ProdutoId)).ToListAsync();

        foreach (var pedido in pedidos)
        {
            var item = venda.Itens.FirstOrDefault(i => i.Id == pedido.ItemVendaId)
                ?? throw new RegraDeNegocioException("Item não pertence a esta venda.");
            var ja = devolvidos.GetValueOrDefault(item.Id);
            if (ja + pedido.Quantidade > item.Quantidade)
                throw new RegraDeNegocioException(
                    $"\"{item.NomeProduto}\" {item.Tamanho}: foram vendidas {item.Quantidade} e já voltaram {ja}. Dá para devolver no máximo {item.Quantidade - ja}.");

            var valor = ValorDaDevolucao(partes[item.Id], item.Quantidade, ja, pedido.Quantidade, valoresJa.GetValueOrDefault(item.Id));
            var volta = item.BaixouEstoque;
            devolucao.Itens.Add(new ItemDevolucao
            {
                ItemVendaId = item.Id,
                ProdutoId = item.ProdutoId,
                NomeProduto = item.NomeProduto,
                Tamanho = item.Tamanho,
                Quantidade = pedido.Quantidade,
                ValorCentavos = valor,
                CustoUnitarioCentavos = item.CustoUnitarioCentavos,
                DevolveuEstoque = volta
            });

            if (!volta || !produtos.TryGetValue(item.ProdutoId, out var produto)) continue;
            produto.EstoqueAtual += pedido.Quantidade;
            var linha = grades.FirstOrDefault(t => t.ProdutoId == item.ProdutoId && t.Tamanho == item.Tamanho);
            if (linha is null)
            {
                linha = new ProdutoTamanho { ProdutoId = item.ProdutoId, Tamanho = item.Tamanho, Estoque = 0 };
                db.TamanhosProduto.Add(linha);
                grades.Add(linha);
            }
            linha.Estoque += pedido.Quantidade;
        }

        devolucao.ValorCentavos = devolucao.Itens.Sum(i => i.ValorCentavos);
        db.Devolucoes.Add(devolucao);
        await db.SaveChangesAsync();

        foreach (var i in devolucao.Itens.Where(i => i.DevolveuEstoque))
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = i.ProdutoId,
                DataHora = agora,
                Tipo = TipoMovimentacao.Devolucao,
                Quantidade = i.Quantidade,
                Tamanho = i.Tamanho,
                Motivo = $"Troca #{devolucao.Id} da venda #{venda.Id}: {texto}",
                VendaId = venda.Id
            });

        ValeTroca? vale = null;
        if (devolucao.ValorCentavos > 0)
        {
            vale = new ValeTroca
            {
                Codigo = await NovoCodigoAsync(db),
                ValorCentavos = devolucao.ValorCentavos,
                DevolucaoId = devolucao.Id,
                ClienteId = venda.ClienteId,
                CriadoEm = agora,
                Operador = operador
            };
            db.ValesTroca.Add(vale);
        }

        var pecas = devolucao.Itens.Sum(i => i.Quantidade);
        AtividadeService.Registrar(db, TipoAtividade.DevolucaoRegistrada,
            $"Troca #{devolucao.Id}: {pecas} peça(s) da venda #{venda.Id} voltaram"
            + (vale is null ? "" : $"; vale-troca {vale.Codigo} de {Dinheiro.Formatar(vale.ValorCentavos)}") + $": {texto}",
            $"Venda #{venda.Id}", operador, agora);

        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return new ResultadoDevolucao(devolucao, vale);
    }

    /// <summary>
    /// Cancela a devolução (com motivo): as peças saem de novo do estoque e o vale é cancelado. Só se o vale não pagou
    /// nada ainda e se as peças ainda estão no estoque.
    /// </summary>
    public async Task CancelarDevolucaoAsync(int devolucaoId, string motivo)
    {
        var texto = (motivo ?? "").Trim();
        if (texto.Length < 3) throw new RegraDeNegocioException("Informe o motivo do cancelamento (pelo menos 3 letras).");
        if (texto.Length > 200) throw new RegraDeNegocioException("O motivo pode ter no máximo 200 caracteres.");

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();
        var devolucao = await db.Devolucoes.Include(d => d.Itens).FirstOrDefaultAsync(d => d.Id == devolucaoId)
            ?? throw new RegraDeNegocioException("Troca não encontrada.");
        if (devolucao.Cancelada) throw new RegraDeNegocioException("Esta troca já está cancelada.");

        var vale = await db.ValesTroca.FirstOrDefaultAsync(v => v.DevolucaoId == devolucao.Id);
        if (vale is not null)
        {
            var usado = await UsadoDoValeAsync(db, vale.Id);
            if (usado > 0)
                throw new RegraDeNegocioException(
                    $"O vale {vale.Codigo} já pagou {Dinheiro.Formatar(usado)} em vendas. Cancele essas vendas antes de cancelar a troca.");
        }

        var ids = devolucao.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var grades = await db.TamanhosProduto.Where(t => ids.Contains(t.ProdutoId)).ToListAsync();
        var agora = _agora();

        foreach (var i in devolucao.Itens.Where(i => i.DevolveuEstoque))
        {
            var linha = grades.FirstOrDefault(t => t.ProdutoId == i.ProdutoId && t.Tamanho == i.Tamanho);
            if (linha is null || linha.Estoque < i.Quantidade || !produtos.ContainsKey(i.ProdutoId))
                throw new RegraDeNegocioException(
                    $"\"{i.NomeProduto}\" {i.Tamanho} não tem mais {i.Quantidade} peça(s) no estoque (já foi vendida de novo?). A troca não pode ser cancelada.");
            linha.Estoque -= i.Quantidade;
            produtos[i.ProdutoId].EstoqueAtual -= i.Quantidade;
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = i.ProdutoId,
                DataHora = agora,
                Tipo = TipoMovimentacao.CancelamentoDevolucao,
                Quantidade = -i.Quantidade,
                Tamanho = i.Tamanho,
                Motivo = $"Cancelamento da troca #{devolucao.Id}: {texto}",
                VendaId = devolucao.VendaId
            });
        }

        var operador = await NomeDoOperadorAsync(db);
        devolucao.Cancelada = true;
        devolucao.DataCancelamento = agora;
        devolucao.MotivoCancelamento = texto;
        devolucao.CanceladoPor = operador;
        if (vale is not null) vale.Cancelado = true;

        AtividadeService.Registrar(db, TipoAtividade.DevolucaoCancelada,
            $"Cancelou a troca #{devolucao.Id} da venda #{devolucao.VendaId} ({Dinheiro.Formatar(devolucao.ValorCentavos)})"
            + (vale is null ? "" : $"; vale {vale.Codigo} cancelado") + $": {texto}",
            $"Venda #{devolucao.VendaId}", operador, agora);

        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    /// <summary>O vale pelo código (como impresso ou lido pelo leitor). Nulo se não existe.</summary>
    public async Task<SaldoVale?> ConsultarValeAsync(string codigo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var vale = await AcharValeAsync(db, codigo);
        return vale is null ? null : await SaldoAsync(db, vale);
    }

    /// <summary>Vales do mais novo para o mais antigo; <paramref name="soComSaldo"/> esconde os usados e os cancelados.</summary>
    public async Task<IReadOnlyList<SaldoVale>> ListarValesAsync(bool soComSaldo = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var vales = await db.ValesTroca.AsNoTracking().OrderByDescending(v => v.Id).ToListAsync();
        var usos = await db.Vendas.AsNoTracking().Where(v => v.ValeTrocaId != null && !v.Cancelada)
            .GroupBy(v => v.ValeTrocaId!.Value).Select(g => new { Id = g.Key, Usado = g.Sum(v => v.ValeTrocaCentavos) })
            .ToDictionaryAsync(x => x.Id, x => x.Usado);
        var nomes = await NomesDosClientesAsync(db, vales.Select(v => v.ClienteId));
        var origens = await db.Devolucoes.AsNoTracking().Select(d => new { d.Id, d.VendaId }).ToDictionaryAsync(d => d.Id, d => d.VendaId);
        var lista = vales.Select(v => new SaldoVale(v.Id, v.Codigo, v.ValorCentavos, usos.GetValueOrDefault(v.Id), v.Cancelado, v.CriadoEm,
            v.ClienteId is int c ? nomes.GetValueOrDefault(c) : null,
            v.DevolucaoId is int d && origens.TryGetValue(d, out var vo) ? vo : null)).ToList();
        return soComSaldo ? lista.Where(v => v.SaldoCentavos > 0).ToList() : lista;
    }

    /// <summary>Trocas de um período (data da troca), da mais recente para a mais antiga, incluindo as canceladas.</summary>
    public async Task<IReadOnlyList<ResumoDevolucao>> ListarDevolucoesAsync(DateTime de, DateTime ate)
    {
        var inicio = de.Date;
        var fim = ate.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.Devolucoes.AsNoTracking().Include(d => d.Itens)
            .Where(d => d.DataHora >= inicio && d.DataHora < fim).ToListAsync();
        var idsDev = lista.Select(d => (int?)d.Id).ToList();
        var codigos = await db.ValesTroca.AsNoTracking().Where(v => idsDev.Contains(v.DevolucaoId))
            .ToDictionaryAsync(v => v.DevolucaoId!.Value, v => v.Codigo);
        var nomes = await NomesDosClientesAsync(db, lista.Select(d => d.ClienteId));
        return lista.OrderByDescending(d => d.DataHora).ThenByDescending(d => d.Id)
            .Select(d => new ResumoDevolucao(d.Id, d.DataHora, d.VendaId, d.ClienteId is int c ? nomes.GetValueOrDefault(c) : null,
                d.ValorCentavos, d.Itens.Sum(i => i.Quantidade), d.Motivo, d.Operador, codigos.GetValueOrDefault(d.Id), d.Cancelada,
                d.MotivoCancelamento))
            .ToList();
    }

    // ================= usados também pelo VendaService (mesma transação da venda) =================

    /// <summary>Código como o operador digitou ou o leitor leu: sem espaços nem traços, em maiúsculas.</summary>
    public static string NormalizarCodigo(string? codigo) =>
        new string((codigo ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    internal static Task<ValeTroca?> AcharValeAsync(PdvDbContext db, string? codigo)
    {
        var limpo = NormalizarCodigo(codigo);
        return limpo.Length == 0 ? Task.FromResult<ValeTroca?>(null) : db.ValesTroca.FirstOrDefaultAsync(v => v.Codigo == limpo);
    }

    internal static async Task<long> UsadoDoValeAsync(PdvDbContext db, int valeId) =>
        await db.Vendas.Where(v => v.ValeTrocaId == valeId && !v.Cancelada).SumAsync(v => v.ValeTrocaCentavos);

    internal static async Task<SaldoVale> SaldoAsync(PdvDbContext db, ValeTroca vale)
    {
        var usado = await UsadoDoValeAsync(db, vale.Id);
        var cliente = vale.ClienteId is int c ? await db.Clientes.AsNoTracking().Where(x => x.Id == c).Select(x => x.Nome).FirstOrDefaultAsync() : null;
        var origem = vale.DevolucaoId is int d ? await db.Devolucoes.AsNoTracking().Where(x => x.Id == d).Select(x => (int?)x.VendaId).FirstOrDefaultAsync() : null;
        return new SaldoVale(vale.Id, vale.Codigo, vale.ValorCentavos, usado, vale.Cancelado, vale.CriadoEm, cliente, origem);
    }

    /// <summary>A venda tem devolução que vale (não cancelada)? Então ela não pode mais ser cancelada.</summary>
    internal static Task<bool> TemDevolucaoAsync(PdvDbContext db, int vendaId) =>
        db.Devolucoes.AnyAsync(d => d.VendaId == vendaId && !d.Cancelada);

    // ================= cálculo do valor =================

    /// <summary>
    /// Quanto o cliente pagou por cada linha: a base da venda (subtotal − desconto, sem o acréscimo do cartão) repartida
    /// pelas linhas na proporção do total de cada uma. Os centavos que sobram do arredondamento vão para as linhas de maior
    /// resto, então a soma das linhas é exatamente a base.
    /// </summary>
    public static Dictionary<int, long> PartesDasLinhas(Venda venda)
    {
        var baseVenda = venda.SubtotalCentavos - venda.DescontoCentavos;
        var itens = venda.Itens.OrderBy(i => i.Id).ToList();
        var subtotal = itens.Sum(i => i.TotalCentavos);
        if (subtotal <= 0 || baseVenda <= 0) return itens.ToDictionary(i => i.Id, _ => 0L);

        var exatos = itens.Select(i => (i.Id, Exato: (decimal)i.TotalCentavos * baseVenda / subtotal)).ToList();
        var partes = exatos.ToDictionary(x => x.Id, x => (long)Math.Floor(x.Exato));
        var sobra = baseVenda - partes.Values.Sum();
        foreach (var x in exatos.OrderByDescending(x => x.Exato - Math.Floor(x.Exato)).ThenBy(x => x.Id).Take((int)sobra))
            partes[x.Id]++;
        return partes;
    }

    /// <summary>
    /// Valor de <paramref name="quantidade"/> peças de uma linha, sabendo quantas já voltaram e quanto já foi pago por elas.
    /// Quem leva a última peça leva o que sobrou da linha, então devolver tudo, aos poucos, soma exatamente o valor da linha.
    /// </summary>
    public static long ValorDaDevolucao(long valorLinha, int vendidos, int jaDevolvidos, int quantidade, long valorJaDevolvido)
    {
        if (vendidos <= 0 || quantidade <= 0) return 0;
        if (jaDevolvidos + quantidade >= vendidos) return valorLinha - valorJaDevolvido;
        var ateAgora = (long)Math.Round(valorLinha * (decimal)(jaDevolvidos + quantidade) / vendidos, MidpointRounding.AwayFromZero);
        return ateAgora - valorJaDevolvido;
    }

    // ================= apoio =================

    private static async Task ExigirQuePodeTrocarAsync(PdvDbContext db, Venda venda)
    {
        if (venda.Cancelada) throw new RegraDeNegocioException($"A venda #{venda.Id} está cancelada: não há o que trocar.");
        if (venda.FormaPagamento == FormaPagamento.APrazo && venda.ClienteId is int cid)
        {
            var titulo = (await ClienteService.ContaAsync(db, cid)).Titulos.FirstOrDefault(t => t.VendaId == venda.Id);
            if (titulo is { SaldoCentavos: > 0 })
                throw new RegraDeNegocioException(
                    $"A venda #{venda.Id} é a prazo e o cliente ainda deve {Dinheiro.Formatar(titulo.SaldoCentavos)}. " +
                    "Troca com vale só depois de quitada (ou ajuste o título em Clientes).");
        }
        if (await db.ChequesRecebidos.AnyAsync(c => c.VendaId == venda.Id && c.Situacao == SituacaoCheque.Devolvido))
            throw new RegraDeNegocioException($"O cheque da venda #{venda.Id} voltou do banco: resolva o cheque antes da troca.");
    }

    private static async Task<Dictionary<int, int>> DevolvidosPorItemAsync(PdvDbContext db, int vendaId) =>
        await db.ItensDevolucao.AsNoTracking()
            .Where(i => db.Devolucoes.Any(d => d.Id == i.DevolucaoId && d.VendaId == vendaId && !d.Cancelada))
            .GroupBy(i => i.ItemVendaId).Select(g => new { g.Key, Qtd = g.Sum(i => i.Quantidade) })
            .ToDictionaryAsync(x => x.Key, x => x.Qtd);

    private static async Task<Dictionary<int, long>> ValoresDevolvidosPorItemAsync(PdvDbContext db, int vendaId) =>
        await db.ItensDevolucao.AsNoTracking()
            .Where(i => db.Devolucoes.Any(d => d.Id == i.DevolucaoId && d.VendaId == vendaId && !d.Cancelada))
            .GroupBy(i => i.ItemVendaId).Select(g => new { g.Key, Valor = g.Sum(i => i.ValorCentavos) })
            .ToDictionaryAsync(x => x.Key, x => x.Valor);

    private static async Task<Dictionary<int, string>> NomesDosClientesAsync(PdvDbContext db, IEnumerable<int?> ids)
    {
        var lista = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return await db.Clientes.AsNoTracking().Where(c => lista.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nome);
    }

    private static async Task<string> NovoCodigoAsync(PdvDbContext db)
    {
        for (var tentativa = 0; tentativa < 20; tentativa++)
        {
            var codigo = "VT" + new string(Enumerable.Range(0, 8).Select(_ => Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)]).ToArray());
            if (!await db.ValesTroca.AnyAsync(v => v.Codigo == codigo) && !db.ValesTroca.Local.Any(v => v.Codigo == codigo))
                return codigo;
        }
        throw new InvalidOperationException("Não foi possível gerar um código de vale único.");
    }

    private static async Task<string> NomeDoOperadorAsync(PdvDbContext db)
    {
        if (!string.IsNullOrWhiteSpace(OperadorAtual.Nome)) return OperadorAtual.Nome!;
        var nome = await db.ConfiguracoesLoja.AsNoTracking().Where(c => c.Id == 1).Select(c => c.NomeOperador).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(nome) ? "Caixa" : nome;
    }
}
