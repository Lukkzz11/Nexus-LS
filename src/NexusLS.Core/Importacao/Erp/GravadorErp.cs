using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>
/// Um documento a gravar no Firestore do ERP. <see cref="Caminho"/> é sempre <c>empresas/{empresa}/...</c>;
/// <see cref="Mascara"/> lista os campos que o Nexus é dono (o resto do documento do ERP não é tocado);
/// <see cref="Criar"/> indica documento novo (a máscara não se aplica).
/// </summary>
public sealed record DocumentoGravacaoErp(string Caminho, IReadOnlyDictionary<string, object?> Campos, IReadOnlyList<string> Mascara, bool Criar,
    IReadOnlyDictionary<string, double>? Incrementos = null);

/// <summary>Para onde as gravações no formato do ERP vão: <see cref="FirestoreErpDestino"/> no programa; os testes usam um destino falso. Os documentos de uma chamada entram juntos (tudo ou nada).</summary>
public interface IDestinoErp
{
    Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default);
}

/// <summary>
/// Monta os documentos no formato que o ERP PDV Life espera (ver docs/FIREBASE_MESMO_BANCO_DO_ERP.md). Só monta:
/// não envia nada. Preços em REAIS (número), grade como mapa tamanho para quantidade, datas como <see cref="DateTime"/> UTC.
/// </summary>
public static class MontadorErp
{
    public const string Origem = "nexus-ls";

    /// <summary>Campos do produto de que o Nexus é dono quando atualiza um produto que já existe no ERP.</summary>
    public static readonly string[] CamposProduto =
    {
        "nome", "categoria", "referencia", "codigoBarras", "precoVarejo", "precoAtacado", "ativo", "tipoEstoque",
        "empresaId", "origem", "nexusGuid", "atualizadoEm"
    };

    /// <summary>
    /// <c>empresas/{empresa}/produtos/{id}</c>. O id é o do ERP quando o produto veio de lá (<paramref name="idNoErp"/>),
    /// senão o Guid do Nexus. O estoque (<c>gradeTamanhos</c>, <c>estoqueTotal</c>) só entra com
    /// <paramref name="incluirEstoque"/>: com os dois sistemas gravando o saldo, quem grava por último sobrescreve o outro.
    /// </summary>
    public static DocumentoGravacaoErp Produto(string empresaId, Produto p, IEnumerable<ProdutoTamanho> tamanhos, DateTime agoraUtc,
        string? idNoErp = null, bool novo = false, bool incluirEstoque = false)
    {
        var id = string.IsNullOrWhiteSpace(idNoErp) ? p.Guid.ToString("D").ToLowerInvariant() : idNoErp.Trim();
        var campos = new Dictionary<string, object?>
        {
            ["nome"] = p.Nome,
            ["categoria"] = p.Categoria ?? "",
            ["referencia"] = p.Referencia ?? "",
            ["codigoBarras"] = p.CodigoBarras ?? "",
            ["precoVarejo"] = p.PrecoVendaCentavos / 100.0,
            ["precoAtacado"] = p.PrecoAtacadoCentavos > 0 ? p.PrecoAtacadoCentavos / 100.0 : p.PrecoVendaCentavos * 0.6 / 100.0,
            ["ativo"] = p.Ativo,
            ["tipoEstoque"] = "loja",
            ["empresaId"] = empresaId,
            ["origem"] = Origem,
            ["nexusGuid"] = p.Guid.ToString("D").ToLowerInvariant(),
            ["atualizadoEm"] = agoraUtc
        };
        var mascara = CamposProduto.ToList();
        if (incluirEstoque)
        {
            var grade = new Dictionary<string, object?>();
            foreach (var t in tamanhos.Where(t => !t.EhSemTamanho)) grade[t.Tamanho] = t.Estoque;
            campos["gradeTamanhos"] = grade;
            campos["estoqueTotal"] = p.VenderSemBaixa ? 0 : tamanhos.Sum(t => t.Estoque);
            mascara.AddRange(new[] { "gradeTamanhos", "estoqueTotal" });
        }
        if (novo)
        {
            campos["criadoEm"] = agoraUtc;
            campos["ativoNoCatalogo"] = true;
            campos["destaque"] = false;
            campos["cores"] = new[] { "Padrão" };
            campos["descricao"] = "";
        }
        return new DocumentoGravacaoErp($"empresas/{empresaId}/produtos/{id}", campos, mascara, novo);
    }

    private static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string CodigoForma(FormaPagamento f) => f switch
    {
        FormaPagamento.Dinheiro => "dinheiro",
        FormaPagamento.Pix => "pix",
        FormaPagamento.CartaoDebito => "cartao_debito",
        FormaPagamento.CartaoCredito => "cartao_credito",
        FormaPagamento.Cheque => "cheque",
        _ => "prazo"
    };

    /// <summary>Endereço do cliente numa linha só (o ERP tem um campo único, <c>endereco</c>).</summary>
    public static string EnderecoDoErp(Cliente c)
    {
        var rua = string.Join(", ", new[] { c.Endereco, c.Numero }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var resto = new[] { c.Complemento, c.Bairro, c.Cep }.Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" - ", new[] { rua }.Concat(resto).Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public static readonly string[] CamposCliente =
    {
        "nome", "razaoSocial", "documento", "inscricaoEstadual", "telefone", "email", "cidade", "estado", "endereco",
        "limiteCredito", "observacoes", "empresaId", "origem", "nexusGuid", "atualizadoEm"
    };

    /// <summary>
    /// <c>empresas/{empresa}/clientes/{id}</c>. Nunca leva <c>saldoDevedor</c>, <c>totalGasto</c> nem <c>qtdCompras</c>: quem muda a
    /// dívida são as vendas a prazo e os pagamentos (incremento atômico), não uma cópia do cadastro.
    /// </summary>
    public static DocumentoGravacaoErp Cliente(string empresaId, Cliente c, DateTime agoraUtc, string? idNoErp = null, bool novo = false)
    {
        var id = string.IsNullOrWhiteSpace(idNoErp) ? c.Guid.ToString("D").ToLowerInvariant() : idNoErp.Trim();
        var campos = new Dictionary<string, object?>
        {
            ["nome"] = c.Nome,
            ["razaoSocial"] = c.RazaoSocial ?? "",
            ["documento"] = c.Documento ?? "",
            ["inscricaoEstadual"] = c.InscricaoEstadual ?? "",
            ["telefone"] = c.Telefone ?? "",
            ["email"] = c.Email ?? "",
            ["cidade"] = c.Cidade ?? "",
            ["estado"] = c.Estado ?? "",
            ["endereco"] = EnderecoDoErp(c),
            ["limiteCredito"] = c.LimiteCreditoCentavos / 100.0,
            ["observacoes"] = c.Observacao ?? "",
            ["empresaId"] = empresaId,
            ["origem"] = Origem,
            ["nexusGuid"] = c.Guid.ToString("D").ToLowerInvariant(),
            ["atualizadoEm"] = agoraUtc
        };
        if (novo)
        {
            campos["tipo"] = "varejo";
            campos["saldoDevedor"] = 0;
            campos["descontoAtacadoPadrao"] = 0;
            campos["totalGasto"] = 0;
            campos["qtdCompras"] = 0;
            campos["ultimaCompra"] = null;
            campos["criadoEm"] = c.DataCadastro.ToUniversalTime();
        }
        return new DocumentoGravacaoErp($"empresas/{empresaId}/clientes/{id}", campos, CamposCliente, novo);
    }

    /// <summary><c>empresas/{empresa}/vendas/{guid}</c>: a venda no formato que o Caixa Loja do ERP lê (valores em reais). Só criação.</summary>
    public static DocumentoGravacaoErp Venda(string empresaId, Venda v, IEnumerable<(ItemVenda Item, Produto? Produto)> itens,
        string? clienteIdErp, Func<Guid, string> idProduto, DateTime agoraUtc)
    {
        var numero = $"NX-{v.Id}";
        var linhas = itens.Select(x => (object?)new Dictionary<string, object?>
        {
            ["id"] = x.Produto is null ? "" : idProduto(x.Produto.Guid),
            ["nome"] = x.Item.NomeProduto,
            ["referencia"] = x.Produto?.Referencia ?? "",
            ["tamanho"] = x.Item.Tamanho == Tamanhos.SemTamanho ? "" : x.Item.Tamanho,
            ["quantidade"] = x.Item.Quantidade,
            ["precoUnitario"] = x.Item.PrecoUnitarioCentavos / 100.0,
            ["subtotal"] = x.Item.TotalCentavos / 100.0
        }).ToList();

        var aPrazo = v.FormaPagamento == FormaPagamento.APrazo;
        var cliente = new Dictionary<string, object?> { ["nome"] = v.Cliente?.Nome ?? "Consumidor Final" };
        if (v.Cliente is not null)
        {
            if (aPrazo && clienteIdErp is not null) cliente["id"] = clienteIdErp;
            cliente["documento"] = v.Cliente.Documento ?? "";
            cliente["telefone"] = v.Cliente.Telefone ?? "";
        }

        var utc = v.DataHora.ToUniversalTime();
        var baseCartao = v.SubtotalCentavos - v.DescontoCentavos;
        var campos = new Dictionary<string, object?>
        {
            ["numeroVenda"] = numero,
            ["dataHoraVenda"] = Iso(utc),
            ["itens"] = linhas,
            ["subtotal"] = v.SubtotalCentavos / 100.0,
            ["desconto"] = v.DescontoCentavos / 100.0,
            ["tipoDesconto"] = "reais",
            ["total"] = v.TotalCentavos / 100.0,
            ["formaPagamento"] = CodigoForma(v.FormaPagamento),
            ["infoPagamento"] = new Dictionary<string, object?>
            {
                ["valorEntregue"] = (v.ValorRecebidoCentavos ?? v.TotalCentavos) / 100.0,
                ["troco"] = v.TrocoCentavos / 100.0,
                ["parcelas"] = v.FormaPagamento == FormaPagamento.CartaoCredito ? v.Parcelas : 1,
                ["taxaPercentual"] = baseCartao > 0 ? v.AcrescimoCentavos * 100.0 / baseCartao : 0.0
            },
            ["tipoVenda"] = v.TabelaPreco == TabelaPreco.Atacado ? "atacado" : "varejo",
            ["origemVenda"] = "pdv",
            ["cliente"] = cliente,
            ["operador"] = v.Operador ?? "Nexus",
            ["operadorId"] = null,
            ["unidadeId"] = "matriz",
            ["empresaId"] = empresaId,
            ["criadoEm"] = utc,
            ["origem"] = Origem,
            ["nexusGuid"] = v.Guid.ToString("D").ToLowerInvariant(),
            ["nexusVendaId"] = v.Id
        };
        return new DocumentoGravacaoErp($"empresas/{empresaId}/vendas/{v.Guid:D}", campos, campos.Keys.ToList(), true);
    }

    /// <summary>
    /// O que o ERP faz junto com uma venda: baixa do estoque (incremento atômico em <c>estoqueTotal</c> e na grade; nunca regrava o
    /// saldo), linha no Kardex e, a prazo, título em <c>contas_receber</c> e dívida do cliente. Vão na MESMA operação da venda.
    /// </summary>
    public static IEnumerable<DocumentoGravacaoErp> EfeitosDaVenda(string empresaId, Venda v, IReadOnlyDictionary<int, Produto> produtos,
        string? clienteIdErp, Func<Guid, string> idProduto, DateTime agoraUtc)
    {
        var numero = $"NX-{v.Id}";
        var n = 0;
        foreach (var g in v.Itens.Where(i => i.BaixouEstoque && produtos.ContainsKey(i.ProdutoId)).GroupBy(i => i.ProdutoId))
        {
            var produto = produtos[g.Key];
            var id = idProduto(produto.Guid);
            var baixas = new Dictionary<string, double> { ["estoqueTotal"] = -g.Sum(i => i.Quantidade) };
            var movimentada = new Dictionary<string, object?>();
            foreach (var t in g.Where(i => i.Tamanho != Tamanhos.SemTamanho).GroupBy(i => i.Tamanho))
            {
                var q = t.Sum(i => i.Quantidade);
                baixas[$"gradeTamanhos.{t.Key}"] = -q;
                movimentada[t.Key] = q;
            }
            yield return new DocumentoGravacaoErp($"empresas/{empresaId}/produtos/{id}", new Dictionary<string, object?>(), Array.Empty<string>(), false, baixas);

            yield return new DocumentoGravacaoErp($"empresas/{empresaId}/movimentacoes_estoque/nx-{v.Guid:N}-{++n}",
                new Dictionary<string, object?>
                {
                    ["tipo"] = "SAIDA_VENDA",
                    ["produtoId"] = id,
                    ["produtoNome"] = produto.Nome,
                    ["referencia"] = produto.Referencia ?? "",
                    ["quantidade"] = -g.Sum(i => i.Quantidade),
                    ["gradeMovimentada"] = movimentada,
                    ["origem"] = "Estoque Loja (Balcão)",
                    ["destino"] = $"Venda {numero}",
                    ["motivo"] = $"Venda {numero}",
                    ["justificativa"] = "Movimento gerado pelo Nexus",
                    ["vendaId"] = v.Guid.ToString("D"),
                    ["responsavel"] = new Dictionary<string, object?> { ["nome"] = v.Operador ?? "Nexus" },
                    ["empresaId"] = empresaId,
                    ["criadoEm"] = agoraUtc
                }, new[] { "tipo" }, true);
        }

        if (v.FormaPagamento == FormaPagamento.APrazo && clienteIdErp is not null && v.Cliente is not null)
        {
            var total = v.TotalCentavos / 100.0;
            var dia = v.DataHora.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            yield return new DocumentoGravacaoErp($"empresas/{empresaId}/contas_receber/nx-tit-{v.Guid:N}",
                new Dictionary<string, object?>
                {
                    ["numeroDocumento"] = $"TIT-{numero}",
                    ["origemVendaId"] = v.Guid.ToString("D"),
                    ["cliente"] = new Dictionary<string, object?> { ["id"] = clienteIdErp, ["nome"] = v.Cliente.Nome, ["documento"] = v.Cliente.Documento ?? "", ["telefone"] = v.Cliente.Telefone ?? "" },
                    ["descricao"] = $"Venda Nexus #{numero} ({v.Itens.Count} itens)",
                    ["valorOriginal"] = total,
                    ["valorPago"] = 0,
                    ["saldoRestante"] = total,
                    ["dataVenda"] = dia,
                    ["dataVencimento"] = v.DataHora.AddDays(30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["status"] = "pendente",
                    ["historicoPagamentos"] = new List<object?>(),
                    ["empresaId"] = empresaId,
                    ["criadoEm"] = agoraUtc
                }, new[] { "numeroDocumento" }, true);

            yield return new DocumentoGravacaoErp($"empresas/{empresaId}/clientes/{clienteIdErp}",
                new Dictionary<string, object?> { ["ultimaCompra"] = dia, ["atualizadoEm"] = agoraUtc },
                new[] { "ultimaCompra", "atualizadoEm" }, false,
                new Dictionary<string, double> { ["saldoDevedor"] = total, ["totalGasto"] = total, ["qtdCompras"] = 1 });
        }
    }

    /// <summary>
    /// <c>empresas/{empresa}</c> campo <c>vitrine</c>: a vitrine do editor (o JSON do editor, que já segue o formato do
    /// <c>vitrineConfig.js</c> do ERP) vai como mapa no campo <c>vitrine</c>, mais o carimbo <c>atualizadoEm</c>. Só estes dois campos entram na máscara.
    /// </summary>
    public static DocumentoGravacaoErp Vitrine(string empresaId, string vitrineJson, DateTime agoraUtc)
    {
        object? mapa;
        try { mapa = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(vitrineJson); }
        catch (System.Text.Json.JsonException) { throw new RegraDeNegocioException("A vitrine é inválida e não pode ser montada."); }
        return new DocumentoGravacaoErp($"empresas/{empresaId}",
            new Dictionary<string, object?> { ["vitrine"] = mapa, ["atualizadoEm"] = agoraUtc },
            new[] { "vitrine", "atualizadoEm" }, false);
    }
}

/// <summary>
/// A única porta de saída para gravar no ERP. Recusa tudo enquanto <see cref="ConfiguracaoErp.GravacaoAtivada"/> for falso
/// (o padrão) e recusa documentos fora de <c>empresas/{empresa}/...</c>.
/// </summary>
public sealed class GravadorErp
{
    private readonly ConfiguracaoErp _config;
    private readonly IDestinoErp _destino;

    public GravadorErp(ConfiguracaoErp config, IDestinoErp destino)
    {
        _config = config;
        _destino = destino;
    }

    public Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default)
    {
        if (!_config.GravacaoAtivada)
            throw new RegraDeNegocioException("A gravação no ERP está desligada (o Nexus só lê o ERP). Veja docs/FIREBASE_MESMO_BANCO_DO_ERP.md antes de ligar.");
        var prefixo = $"empresas/{_config.EmpresaId}";
        foreach (var d in documentos)
            if (d.Caminho != prefixo && !d.Caminho.StartsWith(prefixo + "/", StringComparison.Ordinal))
                throw new RegraDeNegocioException($"Documento fora da empresa configurada: {d.Caminho}.");
        return documentos.Count == 0 ? Task.CompletedTask : _destino.GravarAsync(documentos, ct);
    }
}
