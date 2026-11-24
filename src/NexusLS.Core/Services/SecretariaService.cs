using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public enum AssuntoTarefa { Pagar, Receber, Compromisso, Pedido, Aniversario, Estoque, Orcamento }

/// <summary>Um item da lista "o que fazer". <paramref name="Telefone"/> e <paramref name="Mensagem"/> só quando dá para chamar no WhatsApp.</summary>
public record TarefaDaSecretaria(AssuntoTarefa Assunto, string Titulo, string Detalhe, bool Urgente,
    string? Telefone = null, string? Mensagem = null);

/// <summary>O que um dia do calendário tem. <paramref name="SaldoPrevistoCentavos"/> é nulo nos dias que já passaram.</summary>
public record DiaDePagamentos(DateTime Dia, long APagarCentavos, long AReceberCentavos, long PagoCentavos, bool Atrasado,
    long? SaldoPrevistoCentavos);

/// <summary>
/// A Secretária: junta o que já está no sistema (contas, agenda, pedidos, aniversários e estoque) numa lista do que fazer
/// e num calendário de pagamentos. Só lê; quem grava são os serviços de sempre.
/// </summary>
public class SecretariaService
{
    /// <summary>Quantos dias à frente as contas aparecem na lista.</summary>
    public const int DiasAFrente = 7;

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public SecretariaService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>A lista do que fazer, urgentes primeiro. O que a pessoa não pode ver nem é lido.</summary>
    public async Task<IReadOnlyList<TarefaDaSecretaria>> TarefasAsync(bool financeiro, bool clientes, string nomeLoja = "nossa loja")
    {
        var hoje = _agora().Date;
        var limite = hoje.AddDays(DiasAFrente + 1);
        var tarefas = new List<TarefaDaSecretaria>();
        await using var db = await _fabrica.CreateDbContextAsync();

        if (financeiro)
        {
            var contas = await db.Lancamentos.AsNoTracking()
                .Where(l => l.PagoEm == null && !l.Cancelado && l.Vencimento < limite)
                .OrderBy(l => l.Vencimento).ThenBy(l => l.Id).ToListAsync();
            foreach (var l in contas)
            {
                var pagar = l.Tipo == TipoLancamento.Despesa;
                tarefas.Add(new(pagar ? AssuntoTarefa.Pagar : AssuntoTarefa.Receber,
                    $"{(pagar ? "Pagar" : "Receber")} {Dinheiro.Formatar(l.ValorCentavos)} · {l.Descricao}",
                    Quando(l.Vencimento, hoje) + (string.IsNullOrWhiteSpace(l.Contraparte) ? "" : " · " + l.Contraparte),
                    l.Vencimento.Date <= hoje));
            }
        }

        var compromissos = await db.Compromissos.AsNoTracking().Where(c => !c.Concluido && c.Inicio < hoje.AddDays(1)).ToListAsync();
        foreach (var c in compromissos.OrderBy(c => c.Inicio))
            tarefas.Add(new(AssuntoTarefa.Compromisso, c.Titulo,
                c.Inicio.Date < hoje ? $"Ficou para trás ({c.Inicio:dd/MM})" : c.DiaInteiro ? "Hoje" : $"Hoje às {c.Inicio:HH:mm}",
                c.Inicio.Date < hoje));

        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.Status != StatusPedido.Entregue && p.Status != StatusPedido.Cancelado)
            .ToListAsync();
        foreach (var p in pedidos.OrderBy(p => p.CriadoEm))
        {
            var pronto = p.Status == StatusPedido.ProntoRetirada;
            tarefas.Add(new(AssuntoTarefa.Pedido,
                pronto ? $"Avisar {p.ClienteNome}: pedido {p.Numero} está pronto" : $"Pedido {p.Numero} de {p.ClienteNome} em aberto",
                $"Aberto em {p.CriadoEm:dd/MM} · {Dinheiro.Formatar(p.TotalCentavos)}",
                false,
                pronto ? p.ClienteTelefone : null,
                pronto ? $"Olá, {PrimeiroNome(p.ClienteNome)}! Aqui é da {nomeLoja}. Seu pedido {p.Numero} está pronto para retirada." : null));
        }

        // Orçamento sem resposta: do dia seguinte até uma semana depois de vencer, vale chamar o cliente.
        var desde = hoje.AddDays(-DiasAFrente);
        var semResposta = await db.Orcamentos.AsNoTracking()
            .Where(o => o.Status == StatusOrcamento.Aberto && o.CriadoEm < hoje && o.ValidoAte >= desde).ToListAsync();
        foreach (var o in semResposta.OrderBy(o => o.ValidoAte))
            tarefas.Add(new(AssuntoTarefa.Orcamento, $"Cobrar resposta: orçamento {o.Numero} de {o.ClienteNome}",
                $"{(o.ValidoAte.Date < hoje ? "Venceu em" : "Vale até")} {o.ValidoAte:dd/MM} · {Dinheiro.Formatar(o.TotalCentavos)}",
                false, o.ClienteTelefone,
                $"Olá, {PrimeiroNome(o.ClienteNome)}! Aqui é da {nomeLoja}. Conseguiu ver o orçamento {o.Numero}?"));

        if (clientes)
        {
            var ativos = await db.Clientes.AsNoTracking().Where(c => c.Ativo && c.DataNascimento != null).ToListAsync();
            var telefones = ativos.GroupBy(c => c.Nome).ToDictionary(g => g.Key, g => g.First().Telefone);
            foreach (var a in AvisosAoAbrir.AniversariantesProximos(ativos, hoje))
                tarefas.Add(new(AssuntoTarefa.Aniversario, $"Aniversário de {a.Nome}",
                    a.DiasAte == 0 ? "Hoje" : $"Em {a.DiasAte} dia(s), {a.Data:dd/MM}",
                    false,
                    telefones.GetValueOrDefault(a.Nome),
                    $"Olá, {PrimeiroNome(a.Nome)}! A equipe da {nomeLoja} deseja um feliz aniversário!"));
        }

        var faltando = await db.Produtos.AsNoTracking().CountAsync(p => p.Ativo && p.EstoqueAtual < p.EstoqueMinimo);
        if (faltando > 0)
            tarefas.Add(new(AssuntoTarefa.Estoque, $"{faltando} produto(s) abaixo do estoque mínimo", "Veja em Estoque e planeje a compra", false));

        return tarefas.OrderByDescending(t => t.Urgente).ToList(); // ordenação estável: mantém a ordem de cada assunto
    }

    /// <summary>
    /// Cada dia do mês com o que vence e o saldo previsto: o saldo de hoje mais o que há a receber menos o que há a pagar
    /// até o dia (atrasados contam a partir de hoje). Vendas futuras não entram.
    /// </summary>
    public async Task<IReadOnlyList<DiaDePagamentos>> MesAsync(int ano, int mes, long saldoDeHojeCentavos)
    {
        var hoje = _agora().Date;
        var de = new DateTime(ano, mes, 1);
        var ate = de.AddMonths(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var abertos = await db.Lancamentos.AsNoTracking().Where(l => l.PagoEm == null && !l.Cancelado && l.Vencimento < ate).ToListAsync();
        var pagos = await db.Lancamentos.AsNoTracking()
            .Where(l => l.PagoEm != null && !l.Cancelado && l.Vencimento >= de && l.Vencimento < ate).ToListAsync();

        static long Sinal(Lancamento l) => l.Tipo == TipoLancamento.Receita ? l.ValorCentavos : -l.ValorCentavos;

        var dias = new List<DiaDePagamentos>();
        for (var dia = de; dia < ate; dia = dia.AddDays(1))
        {
            var doDia = abertos.Where(l => l.Vencimento.Date == dia).ToList();
            dias.Add(new DiaDePagamentos(dia,
                doDia.Where(l => l.Tipo == TipoLancamento.Despesa).Sum(l => l.ValorCentavos),
                doDia.Where(l => l.Tipo == TipoLancamento.Receita).Sum(l => l.ValorCentavos),
                pagos.Where(l => l.Vencimento.Date == dia).Sum(l => l.ValorCentavos),
                dia < hoje && doDia.Count > 0,
                dia < hoje ? null : saldoDeHojeCentavos + abertos.Where(l => l.Vencimento.Date <= dia).Sum(Sinal)));
        }
        return dias;
    }

    /// <summary>As contas (pagas ou não) que vencem no dia.</summary>
    public async Task<IReadOnlyList<Lancamento>> ContasDoDiaAsync(DateTime dia)
    {
        var de = dia.Date;
        var ate = de.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Lancamentos.AsNoTracking().Where(l => !l.Cancelado && l.Vencimento >= de && l.Vencimento < ate)
            .OrderBy(l => l.Id).ToListAsync();
    }

    private static string Quando(DateTime vencimento, DateTime hoje)
    {
        var dias = (vencimento.Date - hoje).Days;
        return dias switch
        {
            < 0 => $"Atrasada há {-dias} dia(s)",
            0 => "Vence hoje",
            1 => "Vence amanhã",
            _ => $"Vence em {dias} dias ({vencimento:dd/MM})"
        };
    }

    private static string PrimeiroNome(string nome) => nome.Trim().Split(' ')[0];
}
