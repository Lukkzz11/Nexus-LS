using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>Quanto de cada coleção foi lido e o que aconteceu com cada documento.</summary>
public class ResumoColecaoErp
{
    public string Colecao { get; init; } = "";
    public int Lidos { get; set; }
    public int Novos { get; set; }
    public int Atualizados { get; set; }
    public int SemMudanca { get; set; }
    public int Ignorados { get; set; }
}

public class ResultadoImportacaoErp
{
    public List<ResumoColecaoErp> Colecoes { get; } = new();
    public List<string> Avisos { get; } = new();

    /// <summary>Total de documentos lidos no ERP = leituras cobradas no Firestore dele.</summary>
    public int Leituras => Colecoes.Sum(c => c.Lidos);
    public int Novos => Colecoes.Sum(c => c.Novos);
    public int Atualizados => Colecoes.Sum(c => c.Atualizados);
}

/// <summary>
/// Traz do Firebase do ERP para o SQLite: produtos (com a grade e o estoque), clientes, turnos de caixa e vendas.
/// Só lê o ERP, nunca grava nele. Para gastar poucas leituras, a primeira vez lê tudo e depois só o que foi criado ou
/// alterado desde a última vez (campo de data, em páginas). Importar duas vezes o mesmo documento atualiza o mesmo
/// registro; e o que não mudou (mesmo hash) nem é regravado. Vendas importadas NÃO mexem no estoque: o saldo vem do produto.
/// </summary>
public sealed class ImportadorErp
{
    public const int TamanhoPagina = 300;
    private const int LimiteMaximoPagina = 4800;
    private static readonly TimeSpan Folga = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly IFonteErp _fonte;
    private readonly Func<DateTime> _agora;

    public ImportadorErp(IDbContextFactory<PdvDbContext> fabrica, IFonteErp fonte, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _fonte = fonte;
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    private delegate Task<bool?> Aplicar(PdvDbContext db, DocumentoErp doc, ImportacaoErpItem? ligacao, ResultadoImportacaoErp r);

    public async Task<ResultadoImportacaoErp> ImportarAsync(CancellationToken ct = default, IProgress<string>? progresso = null)
    {
        _progresso = progresso;
        var r = new ResultadoImportacaoErp();
        // Ordem importa: vendas apontam para produtos e clientes.
        await ColecaoAsync("produtos", new[] { "atualizadoEm" }, ProdutoAsync, r, ct);
        await ColecaoAsync("clientes", new[] { "atualizadoEm" }, ClienteAsync, r, ct);
        await ColecaoAsync("caixa_turnos", new[] { "criadoEm", "atualizadoEm" }, TurnoAsync, r, ct);
        await ColecaoAsync("vendas", new[] { "criadoEm", "atualizadoEm" }, VendaAsync, r, ct);
        await ColecaoAsync("contas_receber", new[] { "criadoEm", "atualizadoEm" }, TituloAsync, r, ct);
        await ColecaoAsync("pedidos", new[] { "criadoEm", "atualizadoEm" }, PedidoAsync, r, ct);
        return r;
    }

    private IProgress<string>? _progresso;

    private async Task ColecaoAsync(string colecao, string[] campos, Aplicar aplicar, ResultadoImportacaoErp r, CancellationToken ct)
    {
        _progresso?.Report($"Lendo {colecao} do ERP...");
        var resumo = new ResumoColecaoErp { Colecao = colecao };
        r.Colecoes.Add(resumo);
        var inicio = _agora();

        var leuTudo = false;
        foreach (var campo in campos)
        {
            var chave = $"{colecao}:{campo}";
            DateTime? cursor;
            await using (var db = await _fabrica.CreateDbContextAsync(ct))
                cursor = (await db.ImportacaoErpCursores.AsNoTracking().FirstOrDefaultAsync(c => c.Chave == chave, ct))?.Valor;

            var primeira = cursor is null;
            if (primeira && leuTudo) // a leitura completa deste campo já trouxe tudo: só marca o ponto de partida
            {
                await using var d = await _fabrica.CreateDbContextAsync(ct);
                await SalvarCursorAsync(d, chave, inicio - Folga, ct);
                await d.SaveChangesAsync(ct);
                continue;
            }
            if (primeira) leuTudo = true;
            var limite = TamanhoPagina;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var docs = await _fonte.LerAsync(colecao, campo, cursor, limite, ct);
                resumo.Lidos += docs.Count;
                _progresso?.Report($"{colecao}: {resumo.Lidos} documentos lidos...");

                // Nova posição: a maior data lida; sem nenhuma data (primeira leitura) vale "agora - folga".
                DateTime? maior = docs.Select(d => LerData(d.Campos, campo)).Where(x => x is not null).Max();
                var proximo = primeira ? (maior is { } m && m > inicio - Folga ? m : inicio - Folga) : maior ?? cursor;

                await using (var db = await _fabrica.CreateDbContextAsync(ct))
                {
                    await GravarPaginaAsync(db, colecao, docs, aplicar, resumo, r, ct);
                    await SalvarCursorAsync(db, chave, proximo, ct);
                    await db.SaveChangesAsync(ct);
                }

                if (primeira || docs.Count < limite) break; // a primeira leitura traz a coleção toda, sem paginar
                if (proximo is { } p && cursor is { } c && p <= c) // página inteira com a mesma data: aumenta a página
                {
                    if (limite >= LimiteMaximoPagina) { r.Avisos.Add($"{colecao}: muitos documentos com a mesma data; parou em {p:u}."); break; }
                    limite = Math.Min(limite * 2, LimiteMaximoPagina);
                    continue;
                }
                cursor = proximo;
            }
        }
    }

    private async Task GravarPaginaAsync(PdvDbContext db, string colecao, IReadOnlyList<DocumentoErp> docs, Aplicar aplicar,
        ResumoColecaoErp resumo, ResultadoImportacaoErp r, CancellationToken ct)
    {
        // Um documento pode vir nas duas leituras (criadoEm e atualizadoEm); aqui vale o último da página.
        var unicos = docs.GroupBy(d => d.Id).Select(g => g.Last()).ToList();
        var ids = unicos.Select(d => d.Id).ToList();
        var ligacoes = await db.ImportacaoErp.Where(x => x.Colecao == colecao && ids.Contains(x.ErpId)).ToDictionaryAsync(x => x.ErpId, ct);

        foreach (var doc in unicos)
        {
            ligacoes.TryGetValue(doc.Id, out var ligacao);
            var hash = Hash(doc.Campos);
            if (ligacao is not null && ligacao.Hash == hash) { resumo.SemMudanca++; continue; }

            bool? novo;
            try { novo = await aplicar(db, doc, ligacao, r); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
            {
                r.Avisos.Add($"{colecao}/{doc.Id}: ignorado ({ex.Message})");
                resumo.Ignorados++;
                continue;
            }
            if (novo is null) { resumo.Ignorados++; continue; }

            if (ligacao is null)
            {
                db.ImportacaoErp.Add(new ImportacaoErpItem { Colecao = colecao, ErpId = doc.Id, EntidadeGuid = UltimoGuid, Hash = hash, ImportadoEm = _agora() });
                resumo.Novos++;
            }
            else
            {
                ligacao.EntidadeGuid = UltimoGuid; // se o registro daqui sumiu, foi recriado
                ligacao.Hash = hash;
                ligacao.ImportadoEm = _agora();
                resumo.Atualizados++;
            }
        }
    }

    /// <summary>Guid do registro criado pelo último <c>aplicar</c> (cada aplicador o informa aqui).</summary>
    private Guid UltimoGuid { get; set; }

    private static async Task SalvarCursorAsync(PdvDbContext db, string chave, DateTime? valor, CancellationToken ct)
    {
        if (valor is null) return;
        var atual = await db.ImportacaoErpCursores.FirstOrDefaultAsync(c => c.Chave == chave, ct);
        if (atual is null) db.ImportacaoErpCursores.Add(new ImportacaoErpCursor { Chave = chave, Valor = valor.Value });
        else if (valor > atual.Valor) atual.Valor = valor.Value;
    }

    // ------------------------------------------------------------------ produtos

    private async Task<bool?> ProdutoAsync(PdvDbContext db, DocumentoErp doc, ImportacaoErpItem? ligacao, ResultadoImportacaoErp r)
    {
        var c = doc.Campos;
        var nome = Texto(c, "nome");
        if (nome is null) return null;

        Produto? p = ligacao is null ? null : await db.Produtos.Include(x => x.Tamanhos).FirstOrDefaultAsync(x => x.Guid == ligacao.EntidadeGuid);
        var novo = p is null;
        if (novo)
        {
            p = new Produto { Guid = Guid.NewGuid() };
            db.Produtos.Add(p);
        }

        p!.Nome = nome;
        p.Categoria = Texto(c, "categoria");
        p.Referencia = Texto(c, "referencia");
        p.PrecoVendaCentavos = Centavos(c, "precoVarejo");
        p.PrecoAtacadoCentavos = Centavos(c, "precoAtacado");
        p.PrecoCustoCentavos = Centavos(c, "custoUnitario");
        p.Ativo = Booleano(c, "ativo") ?? true;

        var codigo = Texto(c, "codigoBarras");
        if (codigo is not null && (db.Produtos.Local.Any(x => x.CodigoBarras == codigo && x.Guid != p.Guid)
                                   || await db.Produtos.AnyAsync(x => x.CodigoBarras == codigo && x.Guid != p.Guid)))
        {
            r.Avisos.Add($"produtos/{doc.Id}: código de barras {codigo} já pertence a outro produto; importado sem código.");
            codigo = null;
        }
        p.CodigoBarras = codigo;

        // Grade: cada tamanho com o seu estoque. Sem grade, o saldo total vai para "Sem tamanho".
        var grade = new Dictionary<string, int>();
        if (c["gradeTamanhos"] is JsonObject g)
            foreach (var (tam, qtd) in g) grade[tam.Trim()] = Math.Max(0, (int)Math.Round(Numero(qtd)));
        if (grade.Count == 0) grade[Tamanhos.SemTamanho] = Math.Max(0, (int)Math.Round(Numero(c["estoqueTotal"])));

        foreach (var antigo in p.Tamanhos.Where(t => !grade.ContainsKey(t.Tamanho) && !t.EhSemTamanho).ToList())
            antigo.Estoque = 0; // tamanho que sumiu no ERP: zera (não apaga, pode haver venda no histórico)
        foreach (var (tam, qtd) in grade)
        {
            var linha = p.Tamanhos.FirstOrDefault(t => t.Tamanho == tam);
            if (linha is null) p.Tamanhos.Add(new ProdutoTamanho { Tamanho = tam, Estoque = qtd });
            else linha.Estoque = qtd;
        }
        if (!p.Tamanhos.Any(t => t.EhSemTamanho)) p.Tamanhos.Add(new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho, Estoque = 0 }); // todo produto tem esta linha
        p.EstoqueAtual = p.Tamanhos.Sum(t => t.Estoque);

        UltimoGuid = p.Guid;
        return novo;
    }

    // ------------------------------------------------------------------ clientes

    private async Task<bool?> ClienteAsync(PdvDbContext db, DocumentoErp doc, ImportacaoErpItem? ligacao, ResultadoImportacaoErp r)
    {
        var c = doc.Campos;
        var nome = Texto(c, "nome");
        if (nome is null) return null;

        Cliente? cli = ligacao is null ? null : await db.Clientes.FirstOrDefaultAsync(x => x.Guid == ligacao.EntidadeGuid);
        var novo = cli is null;
        if (novo)
        {
            cli = new Cliente { DataCadastro = LerData(c, "criadoEm")?.ToLocalTime() ?? _agora().ToLocalTime() };
            db.Clientes.Add(cli);
        }
        cli!.Nome = nome;
        cli.Documento = Texto(c, "documento");
        cli.Telefone = Texto(c, "telefone");
        cli.LimiteCreditoCentavos = Centavos(c, "limiteCredito");
        cli.Ativo = Booleano(c, "ativo") ?? true;

        UltimoGuid = cli.Guid;
        return novo;
    }

    // ------------------------------------------------------------------ turnos de caixa

    private async Task<bool?> TurnoAsync(PdvDbContext db, DocumentoErp doc, ImportacaoErpItem? ligacao, ResultadoImportacaoErp r)
    {
        var c = doc.Campos;
        var aberto = DataLocal(c, "abertoEm") ?? LerData(c, "criadoEm")?.ToLocalTime();
        if (aberto is null) return null;

        TurnoCaixa? t = ligacao is null ? null : await db.TurnosCaixa.FirstOrDefaultAsync(x => x.Guid == ligacao.EntidadeGuid);
        var novo = t is null;
        if (novo) { t = new TurnoCaixa(); db.TurnosCaixa.Add(t); }
        t!.DataAbertura = aberto.Value;
        t.TrocoInicialCentavos = Centavos(c, "trocoInicial");
        t.Operador = Texto(c, "operador");
        t.DataFechamento = Texto(c, "status") == "aberto" ? null : DataLocal(c, "fechadoEm");

        UltimoGuid = t.Guid;
        return novo;
    }

    // ------------------------------------------------------------------ vendas

    private async Task<bool?> VendaAsync(PdvDbContext db, DocumentoErp doc, ImportacaoErpItem? ligacao, ResultadoImportacaoErp r)
    {
        var c = doc.Campos;
        var cancelada = Texto(c, "status") == "cancelada";

        if (ligacao is not null) // venda já importada: só pode ter mudado o cancelamento
        {
            var existente = await db.Vendas.FirstOrDefaultAsync(v => v.Guid == ligacao.EntidadeGuid);
            if (existente is null) return null;
            AplicarCancelamento(existente, c, cancelada);
            UltimoGuid = existente.Guid;
            return false;
        }

        var forma = Forma(Texto(c, "formaPagamento"));
        if (forma is null)
        {
            r.Avisos.Add($"vendas/{doc.Id}: forma de pagamento \"{Texto(c, "formaPagamento")}\" não reconhecida; venda ignorada.");
            return null;
        }
        if (c["itens"] is not JsonArray itens || itens.Count == 0) return null;
        var quando = DataLocal(c, "dataHoraVenda") ?? LerData(c, "criadoEm")?.ToLocalTime();
        if (quando is null) return null;

        var venda = new Venda
        {
            DataHora = quando.Value,
            TabelaPreco = Texto(c, "tipoVenda") == "atacado" ? TabelaPreco.Atacado : TabelaPreco.Varejo,
            FormaPagamento = forma.Value,
            Operador = Texto(c, "operador"),
        };

        var info = c["infoPagamento"] as JsonObject;
        venda.Parcelas = Math.Clamp((int)Math.Round(Numero(info?["parcelas"])), 1, 4);
        if (info?["valorEntregue"] is { } entregue && forma == FormaPagamento.Dinheiro) venda.ValorRecebidoCentavos = Reais(entregue);
        venda.TrocoCentavos = Reais(info?["troco"]);

        foreach (var node in itens.OfType<JsonObject>())
        {
            var produtoId = await ProdutoLocalAsync(db, Texto(node, "id"), Texto(node, "nome") ?? "Produto", r);
            if (produtoId is null) continue;
            var qtd = (int)Math.Round(Numero(node["quantidade"]));
            if (qtd <= 0) continue;
            var preco = Centavos(node, "precoUnitario");
            var subtotalItem = node["subtotal"] is null ? preco * qtd : Centavos(node, "subtotal");
            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = produtoId.Value,
                NomeProduto = Texto(node, "nome") ?? "Produto",
                Tamanho = Texto(node, "tamanho") ?? Tamanhos.SemTamanho,
                PrecoUnitarioCentavos = preco,
                Quantidade = qtd,
                TotalCentavos = subtotalItem,
                BaixouEstoque = c["semBaixaEstoque"] is not JsonValue sb || !sb.GetValue<bool>()
            });
        }
        if (venda.Itens.Count == 0) return null;

        var soma = venda.Itens.Sum(i => i.TotalCentavos);
        venda.TotalCentavos = c["total"] is null ? soma : Centavos(c, "total");
        venda.SubtotalCentavos = c["subtotal"] is null ? soma : Centavos(c, "subtotal");
        // Desconto como o ERP gravou; o que sobra acima de "subtotal - desconto" é o acréscimo do crédito.
        venda.DescontoCentavos = c["desconto"] is null ? Math.Max(0, venda.SubtotalCentavos - venda.TotalCentavos) : Math.Clamp(Centavos(c, "desconto"), 0, venda.SubtotalCentavos);
        venda.AcrescimoCentavos = Math.Max(0, venda.TotalCentavos - (venda.SubtotalCentavos - venda.DescontoCentavos));

        var clienteErp = (c["cliente"] as JsonObject) is { } cl ? Texto(cl, "id") : null;
        if (clienteErp is not null)
        {
            var ligCliente = await db.ImportacaoErp.AsNoTracking().FirstOrDefaultAsync(x => x.Colecao == "clientes" && x.ErpId == clienteErp);
            if (ligCliente is not null) venda.ClienteId = (await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Guid == ligCliente.EntidadeGuid))?.Id;
        }
        AplicarCancelamento(venda, c, cancelada);

        db.Vendas.Add(venda);
        UltimoGuid = venda.Guid;
        return true;
    }

    private static void AplicarCancelamento(Venda v, JsonObject c, bool cancelada)
    {
        v.Cancelada = cancelada;
        if (!cancelada) return;
        v.DataCancelamento = DataLocal(c, "canceladoEm") ?? v.DataCancelamento;
        v.MotivoCancelamento = Texto(c, "motivoCancelamento") ?? v.MotivoCancelamento ?? "Cancelada no ERP";
        v.CanceladoPor = Texto(c, "canceladoPor") ?? v.CanceladoPor;
        v.EstoqueDevolvido = Booleano(c, "estoqueRestaurado") ?? false;
    }

    /// <summary>Id do produto aqui. Se o produto não existe mais no ERP, cria um inativo só para o histórico da venda.</summary>
    private async Task<int?> ProdutoLocalAsync(PdvDbContext db, string? erpId, string nome, ResultadoImportacaoErp r)
    {
        if (erpId is null) return null;
        var lig = db.ImportacaoErp.Local.FirstOrDefault(x => x.Colecao == "produtos" && x.ErpId == erpId)
                  ?? await db.ImportacaoErp.FirstOrDefaultAsync(x => x.Colecao == "produtos" && x.ErpId == erpId);
        if (lig is not null)
        {
            var existente = db.Produtos.Local.FirstOrDefault(x => x.Guid == lig.EntidadeGuid)
                            ?? await db.Produtos.FirstOrDefaultAsync(x => x.Guid == lig.EntidadeGuid);
            if (existente is not null)
            {
                if (existente.Id == 0) { await db.SaveChangesAsync(); } // produto novo da mesma página: precisa do Id
                return existente.Id;
            }
        }

        var p = new Produto { Nome = nome, Ativo = false, Tamanhos = { new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho, Estoque = 0 } } };
        db.Produtos.Add(p);
        await db.SaveChangesAsync();
        db.ImportacaoErp.Add(new ImportacaoErpItem { Colecao = "produtos", ErpId = erpId, EntidadeGuid = p.Guid, Hash = "historico", ImportadoEm = _agora() });
        r.Avisos.Add($"Produto {erpId} ({nome}) não está mais no ERP; criado inativo só para o histórico das vendas.");
        return p.Id;
    }

    // ------------------------------------------------------------------ pagamentos de dívidas (contas_receber)

    /// <summary>
    /// A dívida aqui é "vendas a prazo - pagamentos". As vendas a prazo já vieram em <c>vendas</c>; do título só interessam os
    /// pagamentos recebidos, que viram <see cref="PagamentoCliente"/> (um por entrada do histórico). Pagamento estornado no ERP
    /// deixa de existir aqui (o registro importado é removido). Título sem venda de origem (saldo anterior) não tem
    /// onde entrar na dívida daqui e fica de fora, com aviso.
    /// </summary>
    private async Task<bool?> TituloAsync(PdvDbContext db, DocumentoErp doc, ImportacaoErpItem? ligacao, ResultadoImportacaoErp r)
    {
        var c = doc.Campos;
        UltimoGuid = ligacao?.EntidadeGuid ?? Guid.NewGuid();

        var vendaErp = Texto(c, "origemVendaId");
        var clienteErp = c["cliente"] is JsonObject cl ? Texto(cl, "id") : null;
        var ligVenda = vendaErp is null ? null : await db.ImportacaoErp.AsNoTracking().FirstOrDefaultAsync(x => x.Colecao == "vendas" && x.ErpId == vendaErp);
        var ligCliente = clienteErp is null ? null : await db.ImportacaoErp.AsNoTracking().FirstOrDefaultAsync(x => x.Colecao == "clientes" && x.ErpId == clienteErp);
        if (ligVenda is null || ligCliente is null)
        {
            r.Avisos.Add($"contas_receber/{doc.Id}: sem venda ou cliente importados (saldo anterior ou venda ignorada); fica fora da dívida daqui.");
            return null;
        }
        var clienteId = (await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Guid == ligCliente.EntidadeGuid))?.Id;
        var vendaCancelada = await db.Vendas.AsNoTracking().AnyAsync(v => v.Guid == ligVenda.EntidadeGuid && v.Cancelada);
        if (clienteId is null) return null;

        var historico = c["historicoPagamentos"] as JsonArray ?? new JsonArray();
        var i = 0;
        foreach (var entrada in historico.OfType<JsonObject>())
        {
            var id = Texto(entrada, "id") ?? $"{doc.Id}#{i}";
            i++;
            var valor = Centavos(entrada, "valor");
            var vale = !vendaCancelada && Texto(c, "status") != "cancelado" && Booleano(entrada, "estornado") != true && valor > 0;

            var lig = db.ImportacaoErp.Local.FirstOrDefault(x => x.Colecao == PagamentosDivida && x.ErpId == id)
                      ?? await db.ImportacaoErp.FirstOrDefaultAsync(x => x.Colecao == PagamentosDivida && x.ErpId == id);
            if (vale && lig is null)
            {
                var forma = Forma(Texto(entrada, "forma"));
                if (forma is null or FormaPagamento.APrazo)
                {
                    r.Avisos.Add($"contas_receber/{doc.Id}: forma de recebimento \"{Texto(entrada, "forma")}\" não reconhecida; gravada como dinheiro.");
                    forma = FormaPagamento.Dinheiro;
                }
                var pagamento = new PagamentoCliente
                {
                    ClienteId = clienteId.Value, DataHora = DataDoPagamento(entrada) ?? _agora().ToLocalTime(), ValorCentavos = valor,
                    Forma = forma.Value, Observacao = Texto(entrada, "observacao"), Operador = Texto(entrada, "operador")
                };
                db.PagamentosCliente.Add(pagamento);
                db.ImportacaoErp.Add(new ImportacaoErpItem { Colecao = PagamentosDivida, ErpId = id, EntidadeGuid = pagamento.Guid, Hash = "pagamento", ImportadoEm = _agora() });
            }
            else if (!vale && lig is not null)
            {
                var existente = await db.PagamentosCliente.FirstOrDefaultAsync(p => p.Guid == lig.EntidadeGuid);
                if (existente is not null) db.PagamentosCliente.Remove(existente);
                db.ImportacaoErp.Remove(lig);
                r.Avisos.Add($"contas_receber/{doc.Id}: pagamento de {valor / 100m:N2} foi estornado ou cancelado no ERP; removido daqui.");
            }
        }
        return ligacao is null;
    }

    private const string PagamentosDivida = "pagamentos_divida";

    private static DateTime? DataDoPagamento(JsonObject entrada)
    {
        var data = Texto(entrada, "data");
        if (data is null) return null;
        var texto = Texto(entrada, "hora") is { } h ? $"{data} {h}" : data;
        return DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // ------------------------------------------------------------------ pedidos (atacado, WhatsApp, catálogo, venda a prazo)

    private async Task<bool?> PedidoAsync(PdvDbContext db, DocumentoErp doc, ImportacaoErpItem? ligacao, ResultadoImportacaoErp r)
    {
        var c = doc.Campos;
        var status = StatusDoPedido(Texto(c, "status"));
        var agora = _agora().ToLocalTime(); // o Pedido guarda a hora local

        Pedido? p = ligacao is null ? null : await db.Pedidos.Include(x => x.Historico).FirstOrDefaultAsync(x => x.Guid == ligacao.EntidadeGuid);
        if (p is not null)
        {
            if (p.Status != status)
            {
                p.Status = status;
                p.Historico.Add(new EventoPedido { Status = status, DataHora = agora, Operador = "Importado do ERP", Observacao = "Mudança de etapa no ERP" });
            }
            p.MotivoCancelamento = status == StatusPedido.Cancelado ? Texto(c, "motivoCancelamento") ?? p.MotivoCancelamento ?? "Cancelado no ERP" : p.MotivoCancelamento;
            p.AtualizadoEm = agora;
            UltimoGuid = p.Guid;
            return false;
        }

        var cliente = c["cliente"] as JsonObject;
        var clienteErp = cliente is null ? null : Texto(cliente, "id");
        int? clienteId = null;
        if (clienteErp is not null)
        {
            var lig = await db.ImportacaoErp.AsNoTracking().FirstOrDefaultAsync(x => x.Colecao == "clientes" && x.ErpId == clienteErp);
            if (lig is not null) clienteId = (await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Guid == lig.EntidadeGuid))?.Id;
        }
        int? vendaId = null;
        if (Texto(c, "vendaId") is { } vendaErp)
        {
            var lig = await db.ImportacaoErp.AsNoTracking().FirstOrDefaultAsync(x => x.Colecao == "vendas" && x.ErpId == vendaErp);
            if (lig is not null) vendaId = (await db.Vendas.AsNoTracking().FirstOrDefaultAsync(x => x.Guid == lig.EntidadeGuid))?.Id;
        }

        var criado = DataLocal(c, "criadoEm") ?? agora;
        p = new Pedido
        {
            Numero = Texto(c, "numeroPedido") ?? $"ERP-{doc.Id}",
            ClienteId = clienteId,
            ClienteNome = (cliente is null ? null : Texto(cliente, "nome")) ?? "Consumidor final",
            ClienteTelefone = cliente is null ? null : Texto(cliente, "telefone"),
            Origem = OrigemDoPedido(Texto(c, "origem")),
            Status = status,
            TotalCentavos = Centavos(c, "total"),
            Observacao = Texto(c, "observacoes"),
            VendaId = vendaId,
            CriadoEm = criado,
            AtualizadoEm = DataLocal(c, "atualizadoEm") ?? criado,
            MotivoCancelamento = status == StatusPedido.Cancelado ? Texto(c, "motivoCancelamento") ?? "Cancelado no ERP" : null
        };
        foreach (var item in (c["itens"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var qtd = (int)Math.Round(Numero(item["quantidade"]));
            if (qtd <= 0) continue;
            int? produtoId = null;
            if (Texto(item, "id") is { } pErp)
            {
                var lig = await db.ImportacaoErp.AsNoTracking().FirstOrDefaultAsync(x => x.Colecao == "produtos" && x.ErpId == pErp);
                if (lig is not null) produtoId = (await db.Produtos.AsNoTracking().FirstOrDefaultAsync(x => x.Guid == lig.EntidadeGuid))?.Id;
            }
            p.Itens.Add(new ItemPedido
            {
                ProdutoId = produtoId, Nome = Texto(item, "nome") ?? "Item", Tamanho = Texto(item, "tamanho") ?? Tamanhos.SemTamanho,
                Quantidade = qtd, PrecoUnitarioCentavos = Centavos(item, "precoUnitario")
            });
        }
        p.Historico.Add(new EventoPedido { Status = status, DataHora = criado, Operador = "Importado do ERP", Observacao = "Pedido importado do ERP" });
        db.Pedidos.Add(p);
        UltimoGuid = p.Guid;
        return true;
    }

    private static OrigemPedido OrigemDoPedido(string? origem) => origem switch
    {
        "balcao" => OrigemPedido.Balcao,
        "catalogo" => OrigemPedido.Catalogo,
        "encomenda" => OrigemPedido.Encomenda,
        _ => OrigemPedido.WhatsApp // whatsapp, venda_prazo e desconhecidos
    };

    private static StatusPedido StatusDoPedido(string? status) => status?.ToLowerInvariant() switch
    {
        "em_separacao" or "em_producao" => StatusPedido.EmSeparacao,
        "separado" => StatusPedido.Separado,
        "pronto" or "pronto_retirada" => StatusPedido.ProntoRetirada,
        "entregue" => StatusPedido.Entregue,
        "cancelado" => StatusPedido.Cancelado,
        _ => StatusPedido.Novo // novo, pendente, aprovado e desconhecidos
    };

    // ------------------------------------------------------------------ conversões

    private static FormaPagamento? Forma(string? texto) => texto switch
    {
        "dinheiro" => FormaPagamento.Dinheiro,
        "pix" => FormaPagamento.Pix,
        "cartao_debito" or "cartao" or "debito" => FormaPagamento.CartaoDebito,
        "cartao_credito" or "credito" => FormaPagamento.CartaoCredito,
        "cheque" => FormaPagamento.Cheque,
        "prazo" or "a_prazo" => FormaPagamento.APrazo,
        _ => null
    };

    private static string? Texto(JsonObject o, string campo) =>
        o[campo] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static bool? Booleano(JsonObject o, string campo) =>
        o[campo] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static double Numero(JsonNode? n)
    {
        if (n is not JsonValue v) return 0;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<long>(out var l)) return l;
        return v.TryGetValue<string>(out var s) && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
    }

    private static long Reais(JsonNode? n) => (long)Math.Round(Numero(n) * 100, MidpointRounding.AwayFromZero);
    private static long Centavos(JsonObject o, string campo) => Reais(o[campo]);

    private static DateTime? LerData(JsonObject o, string campo) =>
        o[campo] is JsonValue v && v.TryGetValue<string>(out var s)
        && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime : null;

    private static DateTime? DataLocal(JsonObject o, string campo) => LerData(o, campo)?.ToLocalTime();

    private static string Hash(JsonObject o) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(o.ToJsonString())));
}
