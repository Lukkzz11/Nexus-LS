using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Sincronizacao.Operacional;

/// <summary>
/// Onde fica o Firebase OPERACIONAL (projeto B, privado): cópia na nuvem das vendas, caixa etc. É outro projeto que o
/// do catálogo. Os usuários do programa entram nele (Authentication do projeto B); o catálogo (projeto A) continua com
/// o login dele. Nada secreto aqui: a chave da API do Firebase é pública por natureza.
/// </summary>
public class ConfiguracaoOperacional
{
    public string ProjectId { get; set; } = "";
    public string ApiKey { get; set; } = "";

    /// <summary>Nome da loja nos caminhos (<c>lojas/{lojaId}/vendas/...</c>): letras minúsculas, números e hífen.</summary>
    public string LojaId { get; set; } = "";

    public bool EstaCompleta =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(LojaId);

    public void Validar()
    {
        NexusLS.Core.Importacao.Erp.GuardaErp.ExigirNaoSerOErp(ProjectId);
        if (!System.Text.RegularExpressions.Regex.IsMatch(ProjectId.Trim(), "^[a-z][a-z0-9\\-]{4,29}$"))
            throw new RegraDeNegocioException("O id do projeto Firebase operacional deve ter de 5 a 30 letras minúsculas, números ou hífens.");
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new RegraDeNegocioException("Informe a chave da API (Web API Key) do projeto operacional.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(LojaId.Trim(), "^[a-z0-9][a-z0-9\\-]{1,39}$"))
            throw new RegraDeNegocioException("O nome da loja nos caminhos deve ter de 2 a 40 letras minúsculas, números ou hífens (ex.: minha-loja).");
    }
}

public class ConfiguracaoOperacionalStore
{
    private readonly string _caminho;
    public ConfiguracaoOperacionalStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "operacional.json");

    public ConfiguracaoOperacional Carregar()
    {
        try
        {
            return File.Exists(_caminho) ? JsonSerializer.Deserialize<ConfiguracaoOperacional>(File.ReadAllText(_caminho)) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Salvar(ConfiguracaoOperacional configuracao)
    {
        configuracao.ProjectId = configuracao.ProjectId.Trim();
        configuracao.ApiKey = configuracao.ApiKey.Trim();
        configuracao.LojaId = configuracao.LojaId.Trim();
        configuracao.Validar();
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(configuracao, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}

/// <summary>Um documento a gravar no projeto operacional: coleção, id (o Guid da entidade) e os campos, em tipos simples.</summary>
public record DocumentoOperacional(string Colecao, Guid Guid, IReadOnlyDictionary<string, object?> Campos);

public interface IDestinoOperacional
{
    /// <summary>Confere conexão, login e permissão sem gravar nada. Lança <see cref="FalhaDestinoException"/> se não der.</summary>
    Task VerificarAsync(CancellationToken ct = default);

    /// <summary>Grava os documentos (sobrescreve se já existirem: reenviar nunca duplica). Tudo ou nada.</summary>
    Task EnviarLoteAsync(IReadOnlyList<DocumentoOperacional> lote, CancellationToken ct = default);
}

public record DocumentosMontados(IReadOnlyList<DocumentoOperacional> Documentos, IReadOnlyList<Guid> Ausentes);

/// <summary>Transforma entidades do SQLite em documentos para a nuvem. Só lê o banco.</summary>
public interface IMontadorDocumentos
{
    /// <summary>Quais entidades este montador sabe montar.</summary>
    IReadOnlyCollection<string> Entidades { get; }

    Task<DocumentosMontados> MontarAsync(string entidade, IReadOnlyList<Guid> guids);
}

/// <summary>
/// Monta os documentos do projeto operacional. Hoje: vendas (com os itens dentro do documento, já que a venda é um fato
/// fechado). O cancelamento aparece no mesmo documento (campos de cancelamento), e o estado é sempre o atual do SQLite.
/// </summary>
public sealed class MontadorOperacional : IMontadorDocumentos
{
    public const int VersaoDoFormato = 1;
    public const string ColecaoVendas = "vendas";

    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public MontadorOperacional(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    public IReadOnlyCollection<string> Entidades { get; } = new[]
    {
        NomesEntidade.Venda, NomesEntidade.TurnoCaixa, NomesEntidade.MovimentoCaixa, NomesEntidade.FechamentoCaixa,
        NomesEntidade.PagamentoCliente, NomesEntidade.Lancamento, NomesEntidade.ContaFinanceira,
        NomesEntidade.TransferenciaConta, NomesEntidade.LiquidacaoCartao, NomesEntidade.ChequeRecebido,
        NomesEntidade.Cliente, NomesEntidade.Fornecedor, NomesEntidade.Compra, NomesEntidade.Pedido,
        NomesEntidade.MateriaPrima, NomesEntidade.OrdemProducao, NomesEntidade.PerdaProducao
    };

    public Task<DocumentosMontados> MontarAsync(string entidade, IReadOnlyList<Guid> guids) => entidade switch
    {
        NomesEntidade.Venda => MontarVendasAsync(guids),
        NomesEntidade.TurnoCaixa => MontarTurnosAsync(guids),
        NomesEntidade.MovimentoCaixa => MontarMovimentosCaixaAsync(guids),
        NomesEntidade.FechamentoCaixa => MontarFechamentosAsync(guids),
        NomesEntidade.PagamentoCliente => MontarPagamentosClienteAsync(guids),
        NomesEntidade.Lancamento => MontarLancamentosAsync(guids),
        NomesEntidade.ContaFinanceira => MontarContasAsync(guids),
        NomesEntidade.TransferenciaConta => MontarTransferenciasAsync(guids),
        NomesEntidade.LiquidacaoCartao => MontarLiquidacoesAsync(guids),
        NomesEntidade.ChequeRecebido => MontarChequesAsync(guids),
        NomesEntidade.Cliente => MontarClientesAsync(guids),
        NomesEntidade.Fornecedor => MontarFornecedoresAsync(guids),
        NomesEntidade.Compra => MontarComprasAsync(guids),
        NomesEntidade.Pedido => MontarPedidosAsync(guids),
        NomesEntidade.MateriaPrima => MontarMateriasAsync(guids),
        NomesEntidade.OrdemProducao => MontarOrdensAsync(guids),
        NomesEntidade.PerdaProducao => MontarPerdasAsync(guids),
        _ => throw new ArgumentException($"Entidade sem montador: {entidade}", nameof(entidade))
    };
    private async Task<DocumentosMontados> MontarVendasAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens).Where(v => guids.Contains(v.Guid)).ToListAsync();

        var idsClientes = vendas.Where(v => v.ClienteId != null).Select(v => v.ClienteId!.Value).Distinct().ToList();
        var guidsClientes = await db.Clientes.AsNoTracking().Where(c => idsClientes.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Guid);
        var idsProdutos = vendas.SelectMany(v => v.Itens).Select(i => i.ProdutoId).Distinct().ToList();
        var guidsProdutos = await db.Produtos.AsNoTracking().Where(p => idsProdutos.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Guid);

        var documentos = vendas.Select(v =>
        {
            var campos = new Dictionary<string, object?>
            {
                ["guid"] = v.Guid,
                ["numero"] = v.Id,
                ["dataHora"] = Utc(v.DataHora),
                ["operador"] = v.Operador,
                ["formaPagamento"] = v.FormaPagamento.ToString(),
                ["tabelaPreco"] = v.TabelaPreco.ToString(),
                ["parcelas"] = v.Parcelas,
                ["subtotalCentavos"] = v.SubtotalCentavos,
                ["descontoCentavos"] = v.DescontoCentavos,
                ["acrescimoCentavos"] = v.AcrescimoCentavos,
                ["acrescimoCentavos"] = v.AcrescimoCentavos,
                ["totalCentavos"] = v.TotalCentavos,
                ["valorRecebidoCentavos"] = v.ValorRecebidoCentavos,
                ["trocoCentavos"] = v.TrocoCentavos,
                ["clienteGuid"] = v.ClienteId is int cid && guidsClientes.TryGetValue(cid, out var gc) ? gc : (Guid?)null,
                ["cancelada"] = v.Cancelada,
                ["canceladaEm"] = v.DataCancelamento is { } dc ? Utc(dc) : (DateTime?)null,
                ["motivoCancelamento"] = v.MotivoCancelamento,
                ["canceladoPor"] = v.CanceladoPor,
                ["estoqueDevolvido"] = v.EstoqueDevolvido,
                ["itens"] = v.Itens.OrderBy(i => i.Id).Select(i => (object?)new Dictionary<string, object?>
                {
                    ["produtoGuid"] = guidsProdutos.TryGetValue(i.ProdutoId, out var gp) ? gp : (Guid?)null,
                    ["nome"] = i.NomeProduto,
                    ["tamanho"] = i.Tamanho,
                    ["precoUnitarioCentavos"] = i.PrecoUnitarioCentavos,
                    ["quantidade"] = i.Quantidade,
                    ["totalCentavos"] = i.TotalCentavos,
                    ["custoUnitarioCentavos"] = i.CustoUnitarioCentavos,
                    ["baixouEstoque"] = i.BaixouEstoque
                }).ToList(),
                ["atualizadoEm"] = DateTime.SpecifyKind(v.AtualizadoEm, DateTimeKind.Utc), // o carimbo já é gravado em UTC
                ["origem"] = "nexus-ls",
                ["versaoDoFormato"] = VersaoDoFormato
            };
            return new DocumentoOperacional(ColecaoVendas, v.Guid, campos);
        }).ToList();

        var achados = vendas.Select(v => v.Guid).ToHashSet();
        return new DocumentosMontados(documentos, guids.Where(g => !achados.Contains(g)).ToList());
    }

    // ---------- caixa e financeiro ----------
    // Chaves locais (TurnoId, ContaId...) viram o Guid da entidade referenciada: o id inteiro só existe neste SQLite.
    // Momentos (DataHora...) vão em UTC; datas de calendário (vencimento, bom para...) vão como texto "AAAA-MM-DD".

    private async Task<DocumentosMontados> MontarTurnosAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<TurnoCaixa>(db, guids, "turnos", (t, _) => new()
        {
            ["abertoEm"] = Utc(t.DataAbertura), ["fechadoEm"] = Utc(t.DataFechamento), ["operador"] = t.Operador,
            ["trocoInicialCentavos"] = t.TrocoInicialCentavos
        });
    }

    private async Task<DocumentosMontados> MontarMovimentosCaixaAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<MovimentoCaixa>(db, guids, "movimentosCaixa", (m, g) => new()
        {
            ["turnoGuid"] = g.De<TurnoCaixa>(m.TurnoId), ["dataHora"] = Utc(m.DataHora), ["tipo"] = m.Tipo.ToString(),
            ["valorCentavos"] = m.ValorCentavos, ["descricao"] = m.Descricao
        }, ("turno", e => ((MovimentoCaixa)e).TurnoId, typeof(TurnoCaixa)));
    }

    private async Task<DocumentosMontados> MontarFechamentosAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<FechamentoCaixa>(db, guids, "fechamentosCaixa", (f, g) => new()
        {
            ["turnoGuid"] = g.De<TurnoCaixa>(f.TurnoId), ["versao"] = f.Versao, ["dataHora"] = Utc(f.DataHora), ["operador"] = f.Operador,
            ["esperadoDinheiroCentavos"] = f.EsperadoDinheiroCentavos, ["conferidoDinheiroCentavos"] = f.ConferidoDinheiroCentavos,
            ["esperadoCartaoCentavos"] = f.EsperadoCartaoCentavos, ["conferidoCartaoCentavos"] = f.ConferidoCartaoCentavos,
            ["esperadoPixCentavos"] = f.EsperadoPixCentavos, ["conferidoPixCentavos"] = f.ConferidoPixCentavos, ["observacao"] = f.Observacao
        }, ("turno", e => ((FechamentoCaixa)e).TurnoId, typeof(TurnoCaixa)));
    }

    private async Task<DocumentosMontados> MontarPagamentosClienteAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<PagamentoCliente>(db, guids, "pagamentosCliente", (p, g) => new()
        {
            ["clienteGuid"] = g.De<Cliente>(p.ClienteId), ["dataHora"] = Utc(p.DataHora), ["valorCentavos"] = p.ValorCentavos,
            ["forma"] = p.Forma.ToString(), ["observacao"] = p.Observacao, ["operador"] = p.Operador
        }, ("cliente", e => ((PagamentoCliente)e).ClienteId, typeof(Cliente)));
    }

    private async Task<DocumentosMontados> MontarLancamentosAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<Lancamento>(db, guids, "lancamentos", (l, g) => new()
        {
            ["tipo"] = l.Tipo.ToString(), ["descricao"] = l.Descricao, ["categoria"] = l.Categoria, ["contraparte"] = l.Contraparte,
            ["valorCentavos"] = l.ValorCentavos, ["vencimento"] = Dia(l.Vencimento), ["pagoEm"] = Utc(l.PagoEm),
            ["formaPagamento"] = l.FormaPagamento?.ToString(), ["contaGuid"] = g.De<ContaFinanceira>(l.ContaId),
            ["grupoGuid"] = l.GrupoId, ["parcela"] = l.Parcela, ["totalParcelas"] = l.TotalParcelas,
            ["compraGuid"] = g.De<Compra>(l.CompraId), ["observacao"] = l.Observacao, ["criadoEm"] = Utc(l.CriadoEm), ["operador"] = l.Operador,
            ["cancelado"] = l.Cancelado, ["motivoCancelamento"] = l.MotivoCancelamento, ["canceladoEm"] = Utc(l.CanceladoEm)
        }, ("conta", e => ((Lancamento)e).ContaId, typeof(ContaFinanceira)), ("compra", e => ((Lancamento)e).CompraId, typeof(Compra)));
    }

    private async Task<DocumentosMontados> MontarContasAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<ContaFinanceira>(db, guids, "contas", (c, _) => new()
        {
            ["nome"] = c.Nome, ["tipo"] = c.Tipo.ToString(), ["saldoInicialCentavos"] = c.SaldoInicialCentavos,
            ["dataSaldoInicial"] = c.DataSaldoInicial is { } d ? Dia(d) : null, ["padrao"] = c.Padrao, ["ativa"] = c.Ativa, ["criadaEm"] = Utc(c.CriadaEm)
        });
    }

    private async Task<DocumentosMontados> MontarTransferenciasAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<TransferenciaConta>(db, guids, "transferencias", (t, g) => new()
        {
            ["contaOrigemGuid"] = g.De<ContaFinanceira>(t.ContaOrigemId), ["contaDestinoGuid"] = g.De<ContaFinanceira>(t.ContaDestinoId),
            ["valorCentavos"] = t.ValorCentavos, ["data"] = Dia(t.Data), ["descricao"] = t.Descricao, ["operador"] = t.Operador,
            ["criadaEm"] = Utc(t.CriadaEm), ["cancelada"] = t.Cancelada, ["motivoCancelamento"] = t.MotivoCancelamento, ["canceladaEm"] = Utc(t.CanceladaEm)
        }, ("conta", e => ((TransferenciaConta)e).ContaOrigemId, typeof(ContaFinanceira)), ("conta", e => ((TransferenciaConta)e).ContaDestinoId, typeof(ContaFinanceira)));
    }

    private async Task<DocumentosMontados> MontarLiquidacoesAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<LiquidacaoCartao>(db, guids, "liquidacoesCartao", (l, g) => new()
        {
            ["vendaGuid"] = g.De<Venda>(l.VendaId), ["data"] = Dia(l.Data), ["contaGuid"] = g.De<ContaFinanceira>(l.ContaId),
            ["taxaCentavos"] = l.TaxaCentavos, ["operador"] = l.Operador, ["criadaEm"] = Utc(l.CriadaEm),
            ["cancelada"] = l.Cancelada, ["motivoCancelamento"] = l.MotivoCancelamento
        }, ("venda", e => ((LiquidacaoCartao)e).VendaId, typeof(Venda)), ("conta", e => ((LiquidacaoCartao)e).ContaId, typeof(ContaFinanceira)));
    }

    private async Task<DocumentosMontados> MontarChequesAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<ChequeRecebido>(db, guids, "cheques", (c, g) => new()
        {
            ["valorCentavos"] = c.ValorCentavos, ["numero"] = c.Numero, ["banco"] = c.Banco, ["emitente"] = c.Emitente,
            ["bomPara"] = Dia(c.BomPara), ["situacao"] = c.Situacao.ToString(), ["vendaGuid"] = g.De<Venda>(c.VendaId),
            ["pagamentoClienteGuid"] = g.De<PagamentoCliente>(c.PagamentoClienteId), ["compensadoEm"] = Utc(c.CompensadoEm),
            ["contaGuid"] = g.De<ContaFinanceira>(c.ContaId), ["observacao"] = c.Observacao, ["motivoDevolucao"] = c.MotivoDevolucao,
            ["operador"] = c.Operador, ["criadoEm"] = Utc(c.CriadoEm)
        }, ("venda", e => ((ChequeRecebido)e).VendaId, typeof(Venda)), ("pagamentoCliente", e => ((ChequeRecebido)e).PagamentoClienteId, typeof(PagamentoCliente)),
           ("conta", e => ((ChequeRecebido)e).ContaId, typeof(ContaFinanceira)));
    }

    // ---------- cadastros, compras e pedidos ----------

    private async Task<DocumentosMontados> MontarClientesAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<Cliente>(db, guids, "clientes", (c, _) => new()
        {
            ["nome"] = c.Nome, ["documento"] = c.Documento, ["telefone"] = c.Telefone, ["observacao"] = c.Observacao,
            ["ativo"] = c.Ativo, ["cadastradoEm"] = Utc(c.DataCadastro), ["limiteCreditoCentavos"] = c.LimiteCreditoCentavos
        });
    }

    private async Task<DocumentosMontados> MontarFornecedoresAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<Fornecedor>(db, guids, "fornecedores", (f, _) => new()
        {
            ["nome"] = f.Nome, ["documento"] = f.Documento, ["telefone"] = f.Telefone, ["email"] = f.Email,
            ["observacao"] = f.Observacao, ["ativo"] = f.Ativo, ["cadastradoEm"] = Utc(f.DataCadastro)
        });
    }

    private async Task<DocumentosMontados> MontarComprasAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<Compra>(db, guids, "compras", (c, g) => new()
        {
            ["fornecedorGuid"] = g.De<Fornecedor>(c.FornecedorId), ["dataHora"] = Utc(c.DataHora), ["numeroNota"] = c.NumeroNota,
            ["totalCentavos"] = c.TotalCentavos, ["observacao"] = c.Observacao, ["operador"] = c.Operador,
            ["cancelada"] = c.Cancelada, ["canceladaEm"] = Utc(c.CanceladaEm), ["motivoCancelamento"] = c.MotivoCancelamento,
            ["itens"] = c.Itens.OrderBy(i => i.Id).Select(i => (object?)new Dictionary<string, object?>
            {
                ["produtoGuid"] = g.De<Produto>(i.ProdutoId), ["nome"] = i.NomeProduto, ["tamanho"] = i.Tamanho,
                ["quantidade"] = i.Quantidade, ["custoUnitarioCentavos"] = i.CustoUnitarioCentavos, ["totalCentavos"] = i.TotalCentavos
            }).ToList()
        }, q => q.Include(c => c.Itens), c => c.Itens.Select(i => (typeof(Produto), (int?)i.ProdutoId)),
           ("fornecedor", e => ((Compra)e).FornecedorId, typeof(Fornecedor)));
    }

    private async Task<DocumentosMontados> MontarMateriasAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<MateriaPrima>(db, guids, "materias_primas", (m, _) => new()
        {
            ["nome"] = m.Nome, ["tipo"] = m.Tipo, ["unidade"] = m.Unidade, ["estoqueAtual"] = m.EstoqueAtual,
            ["estoqueMinimo"] = m.EstoqueMinimo, ["custoUnitarioCentavos"] = m.CustoUnitarioCentavos,
            ["fornecedor"] = m.Fornecedor, ["localizacao"] = m.Localizacao, ["ativo"] = m.Ativo
        });
    }

    private async Task<DocumentosMontados> MontarOrdensAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<OrdemProducao>(db, guids, "ordens_producao", (o, g) => new()
        {
            ["codigo"] = o.Codigo, ["modelo"] = o.Modelo, ["referencia"] = o.Referencia, ["categoria"] = o.Categoria,
            ["status"] = o.Status.ToString(), ["previsaoTermino"] = Utc(o.PrevisaoTermino), ["tecidoUtilizado"] = o.TecidoUtilizado,
            ["responsavel"] = o.Responsavel, ["custoEstimadoPecaCentavos"] = o.CustoEstimadoPecaCentavos, ["observacoes"] = o.Observacoes,
            ["criadoEm"] = Utc(o.CriadoEm), ["transferidaEm"] = Utc(o.TransferidaEm), ["produtoDestinoGuid"] = g.De<Produto>(o.ProdutoDestinoId),
            ["grade"] = o.Grade.OrderBy(i => i.Id).Select(i => (object?)new Dictionary<string, object?>
            {
                ["tamanho"] = i.Tamanho, ["quantidade"] = i.Quantidade
            }).ToList()
        }, q => q.Include(o => o.Grade), null, ("produtoDestino", e => ((OrdemProducao)e).ProdutoDestinoId, typeof(Produto)));
    }

    private async Task<DocumentosMontados> MontarPerdasAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<PerdaProducao>(db, guids, "perdas_producao", (p, g) => new()
        {
            ["dataHora"] = Utc(p.DataHora), ["ordemGuid"] = g.De<OrdemProducao>(p.OrdemProducaoId), ["ordemCodigo"] = p.OrdemCodigo,
            ["motivo"] = p.Motivo.ToString(), ["materiaGuid"] = g.De<MateriaPrima>(p.MateriaPrimaId), ["materialNome"] = p.MaterialNome,
            ["quantidade"] = p.Quantidade, ["unidade"] = p.Unidade, ["custoEstimadoCentavos"] = p.CustoEstimadoCentavos,
            ["baixouEstoque"] = p.BaixouEstoque, ["observacao"] = p.Observacao, ["registradoPor"] = p.RegistradoPor
        }, ("ordem", e => ((PerdaProducao)e).OrdemProducaoId, typeof(OrdemProducao)), ("materia", e => ((PerdaProducao)e).MateriaPrimaId, typeof(MateriaPrima)));
    }

    private async Task<DocumentosMontados> MontarPedidosAsync(IReadOnlyList<Guid> guids)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await Montar<Pedido>(db, guids, "pedidos", (p, g) => new()
        {
            ["numero"] = p.Numero, ["clienteGuid"] = g.De<Cliente>(p.ClienteId), ["clienteNome"] = p.ClienteNome,
            ["clienteTelefone"] = p.ClienteTelefone, ["origem"] = p.Origem.ToString(), ["status"] = p.Status.ToString(),
            ["totalCentavos"] = p.TotalCentavos, ["observacao"] = p.Observacao, ["vendaGuid"] = g.De<Venda>(p.VendaId),
            ["criadoEm"] = Utc(p.CriadoEm), ["motivoCancelamento"] = p.MotivoCancelamento,
            ["itens"] = p.Itens.OrderBy(i => i.Id).Select(i => (object?)new Dictionary<string, object?>
            {
                ["produtoGuid"] = g.De<Produto>(i.ProdutoId), ["nome"] = i.Nome, ["tamanho"] = i.Tamanho,
                ["quantidade"] = i.Quantidade, ["precoUnitarioCentavos"] = i.PrecoUnitarioCentavos, ["totalCentavos"] = i.TotalCentavos
            }).ToList(),
            ["historico"] = p.Historico.OrderBy(e => e.Id).Select(e => (object?)new Dictionary<string, object?>
            {
                ["status"] = e.Status.ToString(), ["dataHora"] = Utc(e.DataHora), ["operador"] = e.Operador, ["observacao"] = e.Observacao
            }).ToList()
        }, q => q.Include(p => p.Itens).Include(p => p.Historico), p => p.Itens.Select(i => (typeof(Produto), i.ProdutoId)),
           ("cliente", e => ((Pedido)e).ClienteId, typeof(Cliente)), ("venda", e => ((Pedido)e).VendaId, typeof(Venda)));
    }

    /// <summary>Guids das entidades referenciadas (por tipo e id local), carregados de uma vez para o lote.</summary>
    private sealed class Guids
    {
        private readonly Dictionary<(Type, int), Guid> _mapa = new();
        public void Adicionar(Type tipo, int id, Guid guid) => _mapa[(tipo, id)] = guid;
        public Guid? De<T>(int? id) => id is int i && _mapa.TryGetValue((typeof(T), i), out var g) ? g : null;
    }

    /// <summary>
    /// Lê as entidades do lote, resolve as chaves locais para Guid e monta os documentos (campos comuns: guid, atualizadoEm,
    /// origem, versaoDoFormato). Registro que não existe mais é devolvido em "Ausentes".
    /// </summary>
    private Task<DocumentosMontados> Montar<T>(
        PdvDbContext db, IReadOnlyList<Guid> guids, string colecao,
        Func<T, Guids, Dictionary<string, object?>> campos,
        params (string Nome, Func<object, int?> Id, Type Tipo)[] referencias) where T : class, IEntidadeSincronizavel =>
        Montar(db, guids, colecao, campos, null, null, referencias);

    /// <param name="incluir">Carrega as partes (itens, histórico) junto com a entidade.</param>
    /// <param name="extras">Referências que ficam dentro das partes (ex.: o produto de cada item).</param>
    private async Task<DocumentosMontados> Montar<T>(
        PdvDbContext db, IReadOnlyList<Guid> guids, string colecao,
        Func<T, Guids, Dictionary<string, object?>> campos,
        Func<IQueryable<T>, IQueryable<T>>? incluir,
        Func<T, IEnumerable<(Type Tipo, int? Id)>>? extras,
        params (string Nome, Func<object, int?> Id, Type Tipo)[] referencias) where T : class, IEntidadeSincronizavel
    {
        IQueryable<T> consulta = db.Set<T>().AsNoTracking();
        if (incluir is not null) consulta = incluir(consulta);
        var linhas = await consulta.Where(e => guids.Contains(EF.Property<Guid>(e, "Guid"))).ToListAsync();

        var g = new Guids();
        var pedidos = referencias.SelectMany(r => linhas.Select(l => (Tipo: r.Tipo, Id: r.Id(l))))
            .Concat(extras is null ? Enumerable.Empty<(Type Tipo, int? Id)>() : linhas.SelectMany(l => extras(l)));
        foreach (var grupo in pedidos.Where(x => x.Id != null).GroupBy(x => x.Tipo))
        {
            var ids = grupo.Select(x => x.Id!.Value).Distinct().ToList();
            foreach (var (id, guid) in await GuidsDeAsync(db, grupo.Key, ids)) g.Adicionar(grupo.Key, id, guid);
        }

        var documentos = linhas.Select(l =>
        {
            var c = campos(l, g);
            c["guid"] = l.Guid;
            c["atualizadoEm"] = l is Pedido pedido ? Utc(pedido.AtualizadoEm) : DateTime.SpecifyKind(l.AtualizadoEm, DateTimeKind.Utc); // o carimbo já é gravado em UTC (o do Pedido é hora local)
            c["origem"] = "nexus-ls";
            c["versaoDoFormato"] = VersaoDoFormato;
            return new DocumentoOperacional(colecao, l.Guid, c);
        }).ToList();

        var achados = linhas.Select(l => l.Guid).ToHashSet();
        return new DocumentosMontados(documentos, guids.Where(x => !achados.Contains(x)).ToList());
    }

    private static async Task<List<(int Id, Guid Guid)>> GuidsDeAsync(PdvDbContext db, Type tipo, List<int> ids)
    {
        if (tipo == typeof(TurnoCaixa)) return await Ler<TurnoCaixa>(db, ids);
        if (tipo == typeof(Cliente)) return await Ler<Cliente>(db, ids);
        if (tipo == typeof(ContaFinanceira)) return await Ler<ContaFinanceira>(db, ids);
        if (tipo == typeof(Compra)) return await Ler<Compra>(db, ids);
        if (tipo == typeof(Venda)) return await Ler<Venda>(db, ids);
        if (tipo == typeof(PagamentoCliente)) return await Ler<PagamentoCliente>(db, ids);
        if (tipo == typeof(Produto)) return await Ler<Produto>(db, ids);
        if (tipo == typeof(Fornecedor)) return await Ler<Fornecedor>(db, ids);
        if (tipo == typeof(OrdemProducao)) return await Ler<OrdemProducao>(db, ids);
        if (tipo == typeof(MateriaPrima)) return await Ler<MateriaPrima>(db, ids);
        throw new ArgumentException($"Referência sem resolução: {tipo.Name}");
    }

    private static async Task<List<(int, Guid)>> Ler<T>(PdvDbContext db, List<int> ids) where T : class, IEntidadeSincronizavel =>
        (await db.Set<T>().AsNoTracking().Where(e => ids.Contains(EF.Property<int>(e, "Id")))
            .Select(e => new { Id = EF.Property<int>(e, "Id"), e.Guid }).ToListAsync()).Select(x => (x.Id, x.Guid)).ToList();

    private static DateTime? Utc(DateTime? d) => d is { } v ? Utc(v) : null;

    /// <summary>Data de calendário, sem hora nem fuso: "2026-10-30".</summary>
    private static string Dia(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>As datas da venda são gravadas na hora local do computador; a nuvem recebe sempre UTC.</summary>
    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime()
    };
}
