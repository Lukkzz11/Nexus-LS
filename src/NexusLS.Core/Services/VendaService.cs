using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public class VendaService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    /// <param name="agora">Relógio. Nos testes passamos uma data fixa; no app, o padrão é DateTime.Now.</param>
    public VendaService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>
    /// Grava a venda e dá baixa no estoque (por produto e tamanho) NA MESMA TRANSAÇÃO. Uma transação é um
    /// "tudo ou nada": se qualquer passo falhar, o banco volta ao estado anterior e nada fica pela metade.
    /// </summary>
    public async Task<Venda> FinalizarAsync(PedidoVenda pedido)
    {
        // 1) Validações que não dependem do banco.
        ValidarPedido(pedido);

        // Linhas iguais (mesmo produto, tamanho e preço) viram uma só, somando as quantidades.
        // Linha com total editado nunca se junta a outra: o total vale para aquela linha.
        var grupos = pedido.Itens
            .Select((item, indice) => (item, indice))
            .GroupBy(x => (
                x.item.ProdutoId,
                Tamanho: NomeTamanho(x.item.Tamanho),
                x.item.PrecoUnitarioCentavos,
                x.item.TotalLinhaCentavos,
                Separador: x.item.TotalLinhaCentavos is null ? -1 : x.indice))
            .Select(g => (Chave: g.Key, Quantidade: g.Sum(x => x.item.Quantidade)))
            .ToList();

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        var ids = grupos.Select(g => g.Chave.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var linhas = await db.TamanhosProduto.Where(t => ids.Contains(t.ProdutoId)).ToListAsync();

        // 2) Produto e tamanho precisam existir; o produto precisa estar ativo.
        foreach (var (chave, _) in grupos)
        {
            if (!produtos.TryGetValue(chave.ProdutoId, out var produto))
                throw new RegraDeNegocioException("Produto do carrinho não encontrado.");
            if (!produto.Ativo)
                throw new RegraDeNegocioException($"O produto \"{produto.Nome}\" está desativado.");
            if (!linhas.Any(t => t.ProdutoId == chave.ProdutoId && t.Tamanho == chave.Tamanho))
                throw new RegraDeNegocioException($"O produto \"{produto.Nome}\" não tem o tamanho {chave.Tamanho}.");
        }

        // 3) Estoque: soma o que foi pedido de cada produto+tamanho (mesmo em linhas com preços diferentes).
        var faltas = new List<string>();
        foreach (var demanda in grupos.GroupBy(g => (g.Chave.ProdutoId, g.Chave.Tamanho)))
        {
            if (produtos[demanda.Key.ProdutoId].VenderSemBaixa) continue; // sem controle de estoque
            var pedidoTotal = demanda.Sum(g => g.Quantidade);
            var linha = linhas.First(t => t.ProdutoId == demanda.Key.ProdutoId && t.Tamanho == demanda.Key.Tamanho);
            if (linha.Estoque < pedidoTotal)
            {
                var onde = linha.EhSemTamanho ? "" : $" tamanho {linha.Tamanho}";
                faltas.Add($"\"{produtos[demanda.Key.ProdutoId].Nome}\"{onde} (pedido: {pedidoTotal}, em estoque: {linha.Estoque})");
            }
        }
        if (faltas.Count > 0)
            throw new RegraDeNegocioException("Estoque insuficiente para: " + string.Join("; ", faltas) + ".");

        // 4) Monta a venda.
        var operador = await NomeDoOperadorAsync(db);
        var venda = new Venda
        {
            Operador = operador,
            DataHora = _agora(),
            FormaPagamento = pedido.FormaPagamento,
            TabelaPreco = pedido.TabelaPreco,
            Parcelas = pedido.Parcelas
        };

        foreach (var (chave, quantidade) in grupos)
        {
            var produto = produtos[chave.ProdutoId];
            long total, unitario;

            if (chave.TotalLinhaCentavos is long totalEditado)
            {
                total = totalEditado;
                unitario = (long)Math.Round(total / (decimal)quantidade, MidpointRounding.AwayFromZero);
            }
            else
            {
                unitario = chave.PrecoUnitarioCentavos ?? produto.PrecoPara(pedido.TabelaPreco);
                total = unitario * quantidade;
            }

            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = produto.Id,
                NomeProduto = produto.Nome,           // cópia do nome e do preço no momento da venda
                Tamanho = chave.Tamanho,
                PrecoUnitarioCentavos = unitario,
                Quantidade = quantidade,
                TotalCentavos = total,
                BaixouEstoque = !produto.VenderSemBaixa,
                CustoUnitarioCentavos = produto.PrecoCustoCentavos
            });
        }

        venda.SubtotalCentavos = venda.Itens.Sum(i => i.TotalCentavos);
        venda.ClienteId = await ValidarClienteAsync(db, pedido);
        CalcularDescontoEPagamento(venda, pedido);
        if (venda.FormaPagamento == FormaPagamento.APrazo && venda.ClienteId is int clienteId)
            await ValidarLimiteAsync(db, clienteId, venda.TotalCentavos);

        // 5) 1º passo: grava a venda para o banco gerar o Id dela.
        db.Vendas.Add(venda);
        await db.SaveChangesAsync();

        // 6) 2º passo: baixa o estoque do tamanho e do produto, e registra as movimentações ligadas à venda.
        foreach (var item in venda.Itens.Where(i => i.BaixouEstoque))
        {
            produtos[item.ProdutoId].EstoqueAtual -= item.Quantidade;
            linhas.First(t => t.ProdutoId == item.ProdutoId && t.Tamanho == item.Tamanho).Estoque -= item.Quantidade;
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = item.ProdutoId,
                DataHora = venda.DataHora,
                Tipo = TipoMovimentacao.Venda,
                Quantidade = -item.Quantidade,
                Tamanho = item.Tamanho,
                Motivo = $"Venda #{venda.Id}",
                VendaId = venda.Id
            });
        }
        await db.SaveChangesAsync();

        await transacao.CommitAsync(); // só aqui as mudanças viram definitivas
        return venda;
    }

    /// <summary>
    /// Cancela a venda. O registro NUNCA é apagado: fica marcado como cancelado, com o motivo (obrigatório),
    /// quem cancelou e quando. Os itens voltam ao estoque, a não ser que se escolha não devolver
    /// (por exemplo, peça danificada ou já entregue).
    /// </summary>
    /// <param name="canceladoPor">Nome de quem cancela; se vazio, vale o operador configurado.</param>
    public async Task<Venda> CancelarAsync(int vendaId, string motivo, bool devolverEstoque = true, string? canceladoPor = null)
    {
        motivo = (motivo ?? "").Trim();
        if (motivo.Length < 3)
            throw new RegraDeNegocioException("Informe o motivo do cancelamento (pelo menos 3 letras).");

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        var venda = await db.Vendas.Include(v => v.Itens).FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException("Venda não encontrada.");
        if (venda.Cancelada)
            throw new RegraDeNegocioException("Esta venda já está cancelada.");

        if (venda.FormaPagamento == FormaPagamento.APrazo && venda.ClienteId is int cid)
        {
            var titulo = (await ClienteService.ContaAsync(db, cid)).Titulos.FirstOrDefault(t => t.VendaId == venda.Id);
            if (titulo is { PagoCentavos: > 0 })
                throw new RegraDeNegocioException(
                    $"Esta venda a prazo já recebeu {Dinheiro.Formatar(titulo.PagoCentavos)} em pagamentos e não pode ser cancelada.");
        }

        var agora = _agora();
        var ids = venda.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var grades = await db.TamanhosProduto.Where(t => ids.Contains(t.ProdutoId)).ToListAsync();

        foreach (var item in devolverEstoque ? venda.Itens.Where(i => i.BaixouEstoque).ToList() : new List<ItemVenda>())
        {
            produtos[item.ProdutoId].EstoqueAtual += item.Quantidade;

            // Devolve ao tamanho de onde saiu. Se o tamanho foi removido da grade depois da venda, ele é recriado.
            var linha = grades.FirstOrDefault(t => t.ProdutoId == item.ProdutoId && t.Tamanho == item.Tamanho);
            if (linha is null)
            {
                linha = new ProdutoTamanho { ProdutoId = item.ProdutoId, Tamanho = item.Tamanho, Estoque = 0 };
                db.TamanhosProduto.Add(linha);
                grades.Add(linha);
            }
            linha.Estoque += item.Quantidade;

            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = item.ProdutoId,
                DataHora = agora,
                Tipo = TipoMovimentacao.CancelamentoVenda,
                Quantidade = item.Quantidade,
                Tamanho = item.Tamanho,
                Motivo = $"Cancelamento da venda #{venda.Id}: {motivo}",
                VendaId = venda.Id
            });
        }

        venda.Cancelada = true;
        venda.DataCancelamento = agora;
        venda.MotivoCancelamento = motivo;
        venda.CanceladoPor = string.IsNullOrWhiteSpace(canceladoPor) ? await NomeDoOperadorAsync(db) : canceladoPor.Trim();
        venda.EstoqueDevolvido = devolverEstoque;

        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return venda;
    }

    /// <summary>Vendas de um dia (padrão: hoje), da mais recente para a mais antiga, incluindo as canceladas.</summary>
    public async Task<List<Venda>> ListarDoDiaAsync(DateTime? dia = null)
    {
        var inicio = (dia ?? _agora()).Date;
        var fim = inicio.AddDays(1);

        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Vendas.AsNoTracking().Include(v => v.Itens).Include(v => v.Cliente)
            .Where(v => v.DataHora >= inicio && v.DataHora < fim)
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .ToListAsync();
    }

    /// <summary>Soma só as vendas válidas (cancelada não conta no total do dia).</summary>
    public static long TotalValido(IEnumerable<Venda> vendas) =>
        vendas.Where(v => !v.Cancelada).Sum(v => v.TotalCentavos);

    // ================= Regras de apoio =================

    /// <summary>Venda a prazo não pode deixar a dívida do cliente passar do limite dele (zero = sem limite).</summary>
    private static async Task ValidarLimiteAsync(PdvDbContext db, int clienteId, long totalCentavos)
    {
        var limite = await db.Clientes.AsNoTracking().Where(c => c.Id == clienteId).Select(c => c.LimiteCreditoCentavos).FirstAsync();
        if (limite <= 0) return;

        var devedor = (await ClienteService.ContaAsync(db, clienteId)).SaldoDevedorCentavos;
        if (devedor + totalCentavos > limite)
            throw new RegraDeNegocioException(
                $"Limite de crédito excedido: o cliente já deve {Dinheiro.Formatar(devedor)} e o limite é {Dinheiro.Formatar(limite)}.");
    }

    /// <summary>O nome do operador salvo em Configurações ("Caixa" se ninguém configurou).</summary>
    private static async Task<string> NomeDoOperadorAsync(PdvDbContext db)
    {
        var nome = await db.ConfiguracoesLoja.AsNoTracking()
            .Where(c => c.Id == 1).Select(c => c.NomeOperador).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(nome) ? "Caixa" : nome;
    }

    /// <summary>Nulo/vazio/"sem tamanho" viram "Sem tamanho"; os demais ficam em maiúsculas (m -> M).</summary>
    private static string NomeTamanho(string? tamanho)
    {
        var nome = tamanho?.Trim();
        if (string.IsNullOrEmpty(nome) || string.Equals(nome, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            return Tamanhos.SemTamanho;
        return nome.ToUpperInvariant();
    }

    private static void ValidarPedido(PedidoVenda pedido)
    {
        if (pedido.Itens.Count == 0)
            throw new RegraDeNegocioException("O carrinho está vazio.");

        foreach (var item in pedido.Itens)
        {
            if (item.Quantidade <= 0)
                throw new RegraDeNegocioException("A quantidade de cada item deve ser maior que zero.");
            if (item.PrecoUnitarioCentavos is < 0 || item.TotalLinhaCentavos is < 0)
                throw new RegraDeNegocioException("O preço editado não pode ser negativo.");
            if (item.PrecoUnitarioCentavos is not null && item.TotalLinhaCentavos is not null)
                throw new RegraDeNegocioException("Edite o preço da unidade OU o total da linha, não os dois.");
        }

        if (pedido.FormaPagamento == FormaPagamento.CartaoCredito)
        {
            if (pedido.Parcelas is < 1 or > 4)
                throw new RegraDeNegocioException("No cartão de crédito, as parcelas vão de 1 a 4.");
        }
        else if (pedido.Parcelas != 1)
        {
            throw new RegraDeNegocioException("Só o cartão de crédito pode ser parcelado.");
        }
    }

    /// <summary>Confere o cliente. A venda a prazo exige cliente; nas demais ele é opcional.</summary>
    private static async Task<int?> ValidarClienteAsync(PdvDbContext db, PedidoVenda pedido)
    {
        if (pedido.ClienteId is null)
        {
            if (pedido.FormaPagamento == FormaPagamento.APrazo)
                throw new RegraDeNegocioException("A venda a prazo exige um cliente.");
            return null;
        }

        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pedido.ClienteId)
            ?? throw new RegraDeNegocioException("Cliente não encontrado.");
        if (!cliente.Ativo)
            throw new RegraDeNegocioException($"O cliente \"{cliente.Nome}\" está desativado.");
        return cliente.Id;
    }

    private static void CalcularDescontoEPagamento(Venda venda, PedidoVenda pedido)
    {
        // Desconto: em R$ ou em %, nunca os dois. O % vira R$ (arredondado ao centavo) na hora da venda.
        long desconto = pedido.DescontoCentavos;
        if (pedido.DescontoPercentual is decimal percentual)
        {
            if (pedido.DescontoCentavos != 0)
                throw new RegraDeNegocioException("Informe o desconto em R$ ou em %, não os dois.");
            if (percentual < 0 || percentual > 100)
                throw new RegraDeNegocioException("O desconto em % deve ficar entre 0 e 100.");
            desconto = (long)Math.Round(venda.SubtotalCentavos * percentual / 100m, MidpointRounding.AwayFromZero);
        }

        if (desconto < 0)
            throw new RegraDeNegocioException("O desconto não pode ser negativo.");
        if (desconto > venda.SubtotalCentavos)
            throw new RegraDeNegocioException("O desconto não pode ser maior que o subtotal.");

        venda.DescontoCentavos = desconto;
        venda.TotalCentavos = venda.SubtotalCentavos - desconto;

        if (pedido.FormaPagamento == FormaPagamento.Dinheiro)
        {
            var recebido = pedido.ValorRecebidoCentavos
                ?? throw new RegraDeNegocioException("Informe o valor recebido em dinheiro.");
            if (recebido < venda.TotalCentavos)
                throw new RegraDeNegocioException("O valor recebido é menor que o total da venda.");

            venda.ValorRecebidoCentavos = recebido;
            venda.TrocoCentavos = recebido - venda.TotalCentavos;
        }
        else
        {
            venda.ValorRecebidoCentavos = null; // as outras formas pagam exatamente o total
            venda.TrocoCentavos = 0;
        }
    }
}
