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

    public IReadOnlyCollection<string> Entidades { get; } = new[] { NomesEntidade.Venda };

    public Task<DocumentosMontados> MontarAsync(string entidade, IReadOnlyList<Guid> guids) =>
        entidade == NomesEntidade.Venda
            ? MontarVendasAsync(guids)
            : throw new ArgumentException($"Entidade sem montador: {entidade}", nameof(entidade));

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

    /// <summary>As datas da venda são gravadas na hora local do computador; a nuvem recebe sempre UTC.</summary>
    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime()
    };
}
