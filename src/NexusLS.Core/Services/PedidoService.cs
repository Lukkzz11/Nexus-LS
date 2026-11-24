using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Um item do pedido. Com ProdutoId, o nome e o preço (varejo) vêm do produto se não forem informados.</summary>
public record ItemPedidoDados(int? ProdutoId, int Quantidade, string? Nome = null, long? PrecoUnitarioCentavos = null, string? Tamanho = null);

public record DadosPedido(
    OrigemPedido Origem,
    IReadOnlyList<ItemPedidoDados> Itens,
    int? ClienteId = null,
    string? ClienteNome = null,
    string? ClienteTelefone = null,
    string? Observacao = null);

public record FiltroPedidos(StatusPedido? Status = null, string? Texto = null, bool IncluirFinalizados = true);

public static class RegrasPedido
{
    /// <summary>As etapas na ordem em que o pedido anda.</summary>
    public static readonly IReadOnlyList<StatusPedido> Fluxo = new[]
    {
        StatusPedido.Novo, StatusPedido.EmSeparacao, StatusPedido.Separado, StatusPedido.ProntoRetirada, StatusPedido.Entregue
    };

    public static string Nome(StatusPedido s) => s switch
    {
        StatusPedido.Novo => "Novo",
        StatusPedido.EmSeparacao => "Em separação",
        StatusPedido.Separado => "Separado",
        StatusPedido.ProntoRetirada => "Pronto para retirada",
        StatusPedido.Entregue => "Entregue",
        _ => "Cancelado"
    };

    public static string Nome(OrigemPedido o) => o switch
    {
        OrigemPedido.Balcao => "Balcão",
        OrigemPedido.WhatsApp => "WhatsApp",
        OrigemPedido.Catalogo => "Catálogo",
        _ => "Encomenda"
    };

    public static StatusPedido? Proximo(StatusPedido atual) =>
        atual is StatusPedido.Entregue or StatusPedido.Cancelado ? null : Fluxo[Array.IndexOf(Fluxo.ToArray(), atual) + 1];

    /// <summary>Voltar uma etapa: não passa de "Novo". Pedido entregue pode ser reaberto (volta a "Pronto"); cancelado não volta.</summary>
    public static StatusPedido? Anterior(StatusPedido atual) =>
        atual is StatusPedido.Novo or StatusPedido.Cancelado ? null : Fluxo[Array.IndexOf(Fluxo.ToArray(), atual) - 1];

    /// <summary>
    /// Arrastar o cartão para outra etapa: só vale para a etapa vizinha (a mesma regra dos botões Avançar e Voltar).
    /// Devolve nulo se pode, ou o motivo da recusa. Cancelar também é permitido (pede motivo, como o botão).
    /// </summary>
    public static string? MotivoDeNaoPoderMover(StatusPedido atual, StatusPedido destino)
    {
        if (atual == destino) return "O pedido já está nessa etapa.";
        if (atual == StatusPedido.Cancelado) return "Pedido cancelado não volta.";
        if (destino == StatusPedido.Cancelado)
            return PodeCancelar(atual) ? null : "Pedido entregue não pode ser cancelado.";
        if (Proximo(atual) == destino || Anterior(atual) == destino) return null;
        return "Só dá para mover o pedido uma etapa por vez.";
    }

    public static bool PodeCancelar(StatusPedido atual) => atual is not (StatusPedido.Entregue or StatusPedido.Cancelado);

    public static string NumeroDoPedido(DateTime criadoEm, int id) =>
        $"PED-{criadoEm.ToString("yyMM", CultureInfo.InvariantCulture)}-{id:D4}";
}

/// <summary>
/// Pedidos e o quadro de etapas (Novo → Em separação → Separado → Pronto → Entregue, ou Cancelado). Todo movimento
/// fica no histórico com data e operador. Não mexe em estoque nem em caixa.
/// </summary>
public class PedidoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public PedidoService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<Pedido> CriarAsync(DadosPedido dados, string? operador = null)
    {
        if (dados.Itens.Count == 0) throw new RegraDeNegocioException("Adicione pelo menos um item ao pedido.");

        await using var db = await _fabrica.CreateDbContextAsync();
        var agora = _agora();
        var pedido = new Pedido
        {
            Origem = dados.Origem,
            Status = StatusPedido.Novo,
            Observacao = Limpar(dados.Observacao),
            CriadoEm = agora,
            AtualizadoEm = agora
        };

        if (dados.ClienteId is int cid)
        {
            var c = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid) ?? throw new RegraDeNegocioException("Cliente não encontrado.");
            pedido.ClienteId = c.Id;
            pedido.ClienteNome = c.Nome;
            pedido.ClienteTelefone = Limpar(dados.ClienteTelefone) ?? c.Telefone;
        }
        else
        {
            pedido.ClienteNome = Limpar(dados.ClienteNome) ?? "Consumidor final";
            pedido.ClienteTelefone = Limpar(dados.ClienteTelefone);
        }

        foreach (var i in dados.Itens)
        {
            if (i.Quantidade <= 0) throw new RegraDeNegocioException("A quantidade de cada item deve ser maior que zero.");
            if (i.PrecoUnitarioCentavos is < 0) throw new RegraDeNegocioException("O preço não pode ser negativo.");

            var item = new ItemPedido
            {
                Quantidade = i.Quantidade,
                Tamanho = string.IsNullOrWhiteSpace(i.Tamanho) || string.Equals(i.Tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase)
                    ? Tamanhos.SemTamanho : i.Tamanho.Trim().ToUpperInvariant()
            };
            if (i.ProdutoId is int pid)
            {
                var p = await db.Produtos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid) ?? throw new RegraDeNegocioException("Produto não encontrado.");
                item.ProdutoId = p.Id;
                item.Nome = Limpar(i.Nome) ?? p.Nome;
                item.PrecoUnitarioCentavos = i.PrecoUnitarioCentavos ?? p.PrecoVendaCentavos;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(i.Nome)) throw new RegraDeNegocioException("Informe o nome do item.");
                item.Nome = i.Nome.Trim();
                item.PrecoUnitarioCentavos = i.PrecoUnitarioCentavos ?? 0;
            }
            pedido.Itens.Add(item);
        }
        pedido.TotalCentavos = pedido.Itens.Sum(i => i.TotalCentavos);

        pedido.Historico.Add(new EventoPedido { Status = StatusPedido.Novo, DataHora = agora, Operador = Operador(operador), Observacao = "Pedido criado" });
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        pedido.Numero = RegrasPedido.NumeroDoPedido(agora, pedido.Id);
        await db.SaveChangesAsync();
        return pedido;
    }

    /// <summary>
    /// Cria o pedido de uma venda a prazo, dentro da transação da venda (quem chama grava). O número é preenchido
    /// quando o Id existir: por isso devolve o pedido e o chamador chama <see cref="FinalizarNumero"/> depois de salvar.
    /// </summary>
    internal static Pedido DaVenda(Venda venda, string? clienteNome, string? clienteTelefone, DateTime agora, string? operador)
    {
        var pedido = new Pedido
        {
            Origem = OrigemPedido.Balcao,
            Status = StatusPedido.Novo,
            ClienteId = venda.ClienteId,
            ClienteNome = string.IsNullOrWhiteSpace(clienteNome) ? "Consumidor final" : clienteNome,
            ClienteTelefone = clienteTelefone,
            TotalCentavos = venda.TotalCentavos,
            VendaId = venda.Id,
            CriadoEm = agora,
            AtualizadoEm = agora,
            Observacao = $"Venda a prazo #{venda.Id}"
        };
        foreach (var i in venda.Itens)
            pedido.Itens.Add(new ItemPedido
            {
                ProdutoId = i.ProdutoId, Nome = i.NomeProduto, Tamanho = i.Tamanho,
                Quantidade = i.Quantidade, PrecoUnitarioCentavos = i.PrecoUnitarioCentavos
            });
        pedido.Historico.Add(new EventoPedido { Status = StatusPedido.Novo, DataHora = agora, Operador = operador, Observacao = $"Criado pela venda #{venda.Id}" });
        return pedido;
    }

    /// <summary>O pedido de um orçamento aprovado. O total é o do orçamento (já com o desconto).</summary>
    internal static Pedido DoOrcamento(Orcamento o, DateTime agora, string? operador)
    {
        var nota = $"Orçamento {o.Numero}"
            + (o.DescontoCentavos > 0 ? $" (desconto de {Dinheiro.Formatar(o.DescontoCentavos)})" : "")
            + (string.IsNullOrWhiteSpace(o.Observacao) ? "" : $". {o.Observacao}");
        var pedido = new Pedido
        {
            Origem = OrigemPedido.Encomenda,
            Status = StatusPedido.Novo,
            ClienteId = o.ClienteId,
            ClienteNome = o.ClienteNome,
            ClienteTelefone = o.ClienteTelefone,
            TotalCentavos = o.TotalCentavos,
            Observacao = nota.Length > 300 ? nota[..300] : nota,
            CriadoEm = agora,
            AtualizadoEm = agora
        };
        foreach (var i in o.Itens)
            pedido.Itens.Add(new ItemPedido
            {
                ProdutoId = i.ProdutoId, Nome = i.Nome, Tamanho = i.Tamanho,
                Quantidade = i.Quantidade, PrecoUnitarioCentavos = i.PrecoUnitarioCentavos
            });
        pedido.Historico.Add(new EventoPedido { Status = StatusPedido.Novo, DataHora = agora, Operador = Operador(operador), Observacao = $"Criado pelo orçamento {o.Numero}" });
        return pedido;
    }

    internal static void FinalizarNumero(Pedido pedido) => pedido.Numero = RegrasPedido.NumeroDoPedido(pedido.CriadoEm, pedido.Id);

    public async Task<Pedido> AvancarAsync(int id, string? operador = null)
    {
        return await MudarAsync(id, p => RegrasPedido.Proximo(p.Status)
            ?? throw new RegraDeNegocioException(p.Status == StatusPedido.Cancelado ? "Pedido cancelado não anda." : "Esse pedido já foi entregue."), operador, "Avançou");
    }

    public async Task<Pedido> VoltarAsync(int id, string? operador = null)
    {
        return await MudarAsync(id, p => RegrasPedido.Anterior(p.Status)
            ?? throw new RegraDeNegocioException(p.Status == StatusPedido.Cancelado ? "Pedido cancelado não volta." : "O pedido já está na primeira etapa."), operador, "Voltou");
    }

    public async Task<Pedido> CancelarAsync(int id, string motivo, string? operador = null)
    {
        if ((motivo ?? "").Trim().Length < 3) throw new RegraDeNegocioException("Informe o motivo do cancelamento (pelo menos 3 letras).");
        await using var db = await _fabrica.CreateDbContextAsync();
        var p = await db.Pedidos.Include(x => x.Historico).FirstOrDefaultAsync(x => x.Id == id) ?? throw new RegraDeNegocioException("Pedido não encontrado.");
        if (!RegrasPedido.PodeCancelar(p.Status))
            throw new RegraDeNegocioException(p.Status == StatusPedido.Cancelado ? "Esse pedido já está cancelado." : "Pedido entregue não pode ser cancelado.");

        var agora = _agora();
        p.Status = StatusPedido.Cancelado;
        p.MotivoCancelamento = motivo!.Trim();
        p.AtualizadoEm = agora;
        p.Historico.Add(new EventoPedido { Status = StatusPedido.Cancelado, DataHora = agora, Operador = Operador(operador), Observacao = motivo.Trim() });
        AtividadeService.Registrar(db, TipoAtividade.PedidoCancelado,
            $"Cancelou o pedido {RegrasPedido.NumeroDoPedido(p.CriadoEm, p.Id)}: {motivo.Trim()}",
            $"Pedido {RegrasPedido.NumeroDoPedido(p.CriadoEm, p.Id)}", Operador(operador), agora);
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>
    /// Envia uma venda do Caixa para o quadro de Pedidos (etapa "Novo"), com os itens, o cliente e a observação.
    /// A venda continua valendo no caixa e no faturamento: só ganha o vínculo (Pedido.VendaId). Enviar duas vezes é recusado.
    /// </summary>
    public async Task<Pedido> EnviarVendaAsync(int vendaId, string? observacao = null, string? operador = null)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var venda = await db.Vendas.Include(v => v.Itens).Include(v => v.Cliente).FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException("Venda não encontrada.");
        if (venda.Cancelada) throw new RegraDeNegocioException("Venda cancelada não pode ser enviada para Pedidos.");

        var existente = await db.Pedidos.AsNoTracking()
            .Where(p => p.VendaId == vendaId && p.Status != StatusPedido.Cancelado)
            .Select(p => new { p.Id, p.Numero }).FirstOrDefaultAsync();
        if (existente is not null)
            throw new RegraDeNegocioException($"A venda #{vendaId} já está nos Pedidos ({existente.Numero}).");

        var agora = _agora();
        var op = Operador(operador);
        var pedido = DaVenda(venda, venda.Cliente?.Nome, venda.Cliente?.Telefone, agora, op);
        pedido.Observacao = Limpar(observacao) is { } o ? $"Enviada do Caixa: venda #{vendaId}. {o}" : $"Enviada do Caixa: venda #{vendaId}";
        pedido.Historico[0].Observacao = $"Enviado do Caixa (venda #{vendaId})";
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        FinalizarNumero(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    /// <summary>Para cada venda da lista que já foi enviada, o número do pedido (ignora pedidos cancelados).</summary>
    public async Task<IReadOnlyDictionary<int, string>> NumerosPorVendaAsync(IEnumerable<int> vendaIds)
    {
        var ids = vendaIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.Pedidos.AsNoTracking()
            .Where(p => p.VendaId != null && ids.Contains(p.VendaId.Value) && p.Status != StatusPedido.Cancelado)
            .OrderBy(p => p.Id).Select(p => new { VendaId = p.VendaId!.Value, p.Numero }).ToListAsync();
        return lista.GroupBy(x => x.VendaId).ToDictionary(g => g.Key, g => g.First().Numero);
    }

    private async Task<Pedido> MudarAsync(int id, Func<Pedido, StatusPedido> novoStatus, string? operador, string verbo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var p = await db.Pedidos.Include(x => x.Historico).FirstOrDefaultAsync(x => x.Id == id) ?? throw new RegraDeNegocioException("Pedido não encontrado.");
        var destino = novoStatus(p);
        var agora = _agora();
        p.Status = destino;
        p.AtualizadoEm = agora;
        p.Historico.Add(new EventoPedido { Status = destino, DataHora = agora, Operador = Operador(operador), Observacao = $"{verbo} para {RegrasPedido.Nome(destino)}" });
        await db.SaveChangesAsync();
        return p;
    }

    public async Task<Pedido> ObterAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Pedidos.AsNoTracking().Include(x => x.Itens).Include(x => x.Historico).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new RegraDeNegocioException("Pedido não encontrado.");
    }

    /// <summary>Lista do mais novo para o mais antigo. Sem filtro de etapa, traz todas (inclusive entregues e canceladas, se pedido).</summary>
    public async Task<IReadOnlyList<Pedido>> ListarAsync(FiltroPedidos? filtro = null)
    {
        filtro ??= new FiltroPedidos();
        await using var db = await _fabrica.CreateDbContextAsync();
        var consulta = db.Pedidos.AsNoTracking().Include(p => p.Itens).AsQueryable();
        if (filtro.Status is { } s) consulta = consulta.Where(p => p.Status == s);
        if (!filtro.IncluirFinalizados) consulta = consulta.Where(p => p.Status != StatusPedido.Entregue && p.Status != StatusPedido.Cancelado);
        var lista = await consulta.ToListAsync();
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var t = filtro.Texto.Trim();
            lista = lista.Where(p =>
                p.Numero.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                p.ClienteNome.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
                (p.ClienteTelefone?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                p.Itens.Any(i => i.Nome.Contains(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
        }
        return lista.OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id).ToList();
    }

    private static string? Operador(string? informado) =>
        Limpar(informado) ?? Limpar(OperadorAtual.Nome);

    private static string? Limpar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
