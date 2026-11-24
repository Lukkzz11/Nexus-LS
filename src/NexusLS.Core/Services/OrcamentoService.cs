using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Pdf;

namespace NexusLS.Core.Services;

public record DadosOrcamento(
    IReadOnlyList<ItemPedidoDados> Itens,
    int? ClienteId = null,
    string? ClienteNome = null,
    string? ClienteTelefone = null,
    int ValidadeDias = 7,
    long DescontoCentavos = 0,
    string? Observacao = null);

/// <summary>Orçamentos: montar, corrigir, gerar o PDF, recusar e transformar em pedido.</summary>
public class OrcamentoService
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public OrcamentoService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public DateTime Hoje => _agora().Date;

    public static string Situacao(Orcamento o, DateTime hoje) => o.Status switch
    {
        StatusOrcamento.Aprovado => "Aprovado",
        StatusOrcamento.Recusado => "Recusado",
        _ => o.ValidoAte.Date < hoje.Date ? "Vencido" : "Aguardando resposta"
    };

    public async Task<Orcamento> CriarAsync(DadosOrcamento dados)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var agora = _agora();
        var orcamento = new Orcamento { CriadoEm = agora };
        await PreencherAsync(db, orcamento, dados, agora);
        db.Orcamentos.Add(orcamento);
        await db.SaveChangesAsync();
        orcamento.Numero = $"ORC-{agora.ToString("yyMM", CultureInfo.InvariantCulture)}-{orcamento.Id:D4}";
        await db.SaveChangesAsync();
        return orcamento;
    }

    /// <summary>Corrige um orçamento ainda aberto. A validade passa a contar de hoje; o número não muda.</summary>
    public async Task<Orcamento> AlterarAsync(int id, DadosOrcamento dados)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var orcamento = await BuscarAsync(db, id);
        if (orcamento.Status != StatusOrcamento.Aberto) throw new RegraDeNegocioException("Só dá para corrigir um orçamento em aberto.");
        db.ItensOrcamento.RemoveRange(orcamento.Itens);
        orcamento.Itens = new();
        await PreencherAsync(db, orcamento, dados, _agora());
        await db.SaveChangesAsync();
        return orcamento;
    }

    public async Task RecusarAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var o = await BuscarAsync(db, id);
        if (o.Status != StatusOrcamento.Aberto) throw new RegraDeNegocioException("Só dá para recusar um orçamento em aberto.");
        o.Status = StatusOrcamento.Recusado;
        await db.SaveChangesAsync();
    }

    /// <summary>Volta para "em aberto": um recusado, ou um aprovado cujo pedido foi cancelado.</summary>
    public async Task ReabrirAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var o = await BuscarAsync(db, id);
        if (o.Status == StatusOrcamento.Aberto) throw new RegraDeNegocioException("Esse orçamento já está em aberto.");
        if (o.Status == StatusOrcamento.Aprovado
            && await db.Pedidos.AnyAsync(p => p.Id == o.PedidoId && p.Status != StatusPedido.Cancelado))
            throw new RegraDeNegocioException("Esse orçamento já virou pedido. Para reabrir, cancele o pedido primeiro.");
        o.Status = StatusOrcamento.Aberto;
        o.PedidoId = null;
        await db.SaveChangesAsync();
    }

    /// <summary>O cliente aprovou: cria o pedido (etapa "Novo", origem "Encomenda") com os mesmos itens e o mesmo total.</summary>
    public async Task<Pedido> VirarPedidoAsync(int id, string? operador = null)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await BuscarAsync(db, id);
        if (o.Status != StatusOrcamento.Aberto)
            throw new RegraDeNegocioException(o.Status == StatusOrcamento.Aprovado ? "Esse orçamento já virou pedido." : "Orçamento recusado. Reabra antes de virar pedido.");

        var pedido = PedidoService.DoOrcamento(o, _agora(), operador);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        PedidoService.FinalizarNumero(pedido);
        o.Status = StatusOrcamento.Aprovado;
        o.PedidoId = pedido.Id;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return pedido;
    }

    public async Task<Orcamento> ObterAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await BuscarAsync(db, id);
    }

    /// <summary>Do mais novo para o mais antigo. O texto procura em número, cliente, telefone e itens.</summary>
    public async Task<IReadOnlyList<Orcamento>> ListarAsync(string? texto = null, bool soAbertos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var consulta = db.Orcamentos.AsNoTracking().Include(o => o.Itens).AsQueryable();
        if (soAbertos) consulta = consulta.Where(o => o.Status == StatusOrcamento.Aberto);
        var lista = await consulta.ToListAsync();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            lista = lista.Where(o =>
                o.Numero.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                o.ClienteNome.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
                (o.ClienteTelefone?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                o.Itens.Any(i => i.Nome.Contains(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
        }
        return lista.OrderByDescending(o => o.CriadoEm).ThenByDescending(o => o.Id).ToList();
    }

    public static string NomeDoPdf(Orcamento o) => $"orcamento-{o.Numero}.pdf";

    /// <summary>O orçamento em PDF, no padrão dos documentos do Nexus.</summary>
    public static byte[] GerarPdf(Orcamento o, ConfiguracaoLoja loja, byte[]? logoJpeg = null)
    {
        var doc = new PdfDocumento($"Orçamento {o.Numero}", DocumentosPdf.Empresa(loja, logoJpeg), o.CriadoEm, "Documento sem valor fiscal");
        var campos = new List<(string, string)>
        {
            ("CLIENTE", o.ClienteNome),
            ("TELEFONE", string.IsNullOrWhiteSpace(o.ClienteTelefone) ? "—" : o.ClienteTelefone!),
            ("DATA", o.CriadoEm.ToString("dd/MM/yyyy", Br)),
            ("VÁLIDO ATÉ", o.ValidoAte.ToString("dd/MM/yyyy", Br))
        };
        doc.Campos(campos);

        doc.Secao("Itens").Tabela(
            new[]
            {
                new ColunaPdf("Qtd", 0.7, AlinhamentoPdf.Centro), new ColunaPdf("Descrição", 4.6),
                new ColunaPdf("Unitário", 1.4, AlinhamentoPdf.Direita), new ColunaPdf("Total", 1.4, AlinhamentoPdf.Direita)
            },
            o.Itens.Select(i => new[]
            {
                i.Quantidade.ToString(Br), i.Nome + (i.Tamanho == Tamanhos.SemTamanho ? "" : $" — {i.Tamanho}"),
                Dinheiro.Formatar(i.PrecoUnitarioCentavos), Dinheiro.Formatar(i.TotalCentavos)
            }));

        var resumo = new List<(string, string)>();
        if (o.DescontoCentavos > 0)
        {
            resumo.Add(("Subtotal", Dinheiro.Formatar(o.TotalCentavos + o.DescontoCentavos)));
            resumo.Add(("Desconto", "- " + Dinheiro.Formatar(o.DescontoCentavos)));
        }
        resumo.Add(("TOTAL", Dinheiro.Formatar(o.TotalCentavos)));
        doc.Resumo(resumo);

        if (!string.IsNullOrWhiteSpace(o.Observacao)) doc.Secao("Condições").Paragrafo(o.Observacao!);
        if (loja.CupomMostrarPix && !string.IsNullOrWhiteSpace(loja.ChavePix)) doc.Paragrafo("Chave Pix: " + loja.ChavePix);
        doc.Assinaturas(new[] { "De acordo (cliente)", "Loja" });
        return doc.Gerar();
    }

    private static async Task<Orcamento> BuscarAsync(PdvDbContext db, int id) =>
        await db.Orcamentos.Include(o => o.Itens).FirstOrDefaultAsync(o => o.Id == id)
        ?? throw new RegraDeNegocioException("Orçamento não encontrado.");

    private static async Task PreencherAsync(PdvDbContext db, Orcamento o, DadosOrcamento dados, DateTime agora)
    {
        if (dados.Itens.Count == 0) throw new RegraDeNegocioException("Adicione pelo menos um item ao orçamento.");
        if (dados.ValidadeDias is < 1 or > 365) throw new RegraDeNegocioException("Validade: um número de 1 a 365 dias.");

        if (dados.ClienteId is int cid)
        {
            var c = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid) ?? throw new RegraDeNegocioException("Cliente não encontrado.");
            o.ClienteId = c.Id;
            o.ClienteNome = c.Nome;
            o.ClienteTelefone = Limpar(dados.ClienteTelefone) ?? c.Telefone;
        }
        else
        {
            o.ClienteId = null;
            o.ClienteNome = Limpar(dados.ClienteNome) ?? throw new RegraDeNegocioException("Informe o nome do cliente.");
            o.ClienteTelefone = Limpar(dados.ClienteTelefone);
        }

        foreach (var i in dados.Itens)
        {
            if (i.Quantidade <= 0) throw new RegraDeNegocioException("A quantidade de cada item deve ser maior que zero.");
            if (i.PrecoUnitarioCentavos is < 0) throw new RegraDeNegocioException("O preço não pode ser negativo.");

            var item = new ItemOrcamento
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
                item.Nome = Limpar(i.Nome) ?? throw new RegraDeNegocioException("Informe o nome do item.");
                item.PrecoUnitarioCentavos = i.PrecoUnitarioCentavos ?? 0;
            }
            o.Itens.Add(item);
        }

        var subtotal = o.Itens.Sum(i => i.TotalCentavos);
        if (dados.DescontoCentavos < 0 || dados.DescontoCentavos > subtotal)
            throw new RegraDeNegocioException("O desconto não pode ser negativo nem maior que a soma dos itens.");
        o.DescontoCentavos = dados.DescontoCentavos;
        o.TotalCentavos = subtotal - dados.DescontoCentavos;
        o.ValidoAte = agora.Date.AddDays(dados.ValidadeDias);
        o.Observacao = Limpar(dados.Observacao);
        if (o.Observacao is { Length: > 500 }) throw new RegraDeNegocioException("As condições passam de 500 letras.");
    }

    private static string? Limpar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
