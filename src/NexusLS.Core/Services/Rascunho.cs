using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Uma linha do carrinho como foi guardada no rascunho. Só o essencial: nome e preço de tabela vêm do banco ao restaurar.</summary>
public class ItemRascunho
{
    public int ProdutoId { get; set; }
    public string Tamanho { get; set; } = Tamanhos.SemTamanho;
    public int Quantidade { get; set; }
    public long? PrecoUnitarioEditadoCentavos { get; set; }
    public long? TotalEditadoCentavos { get; set; }
}

/// <summary>A venda em andamento, guardada em disco a cada mudança para sobreviver a fechar o programa.</summary>
public class Rascunho
{
    public int Versao { get; set; } = 1;
    public DateTime SalvoEm { get; set; }
    public TabelaPreco Tabela { get; set; } = TabelaPreco.Varejo;
    public string? DescontoTexto { get; set; }
    public int? ClienteId { get; set; }
    public List<ItemRascunho> Itens { get; set; } = new();
}

/// <summary>Lê e grava o arquivo do rascunho (JSON). Não conhece o banco nem a tela.</summary>
public class RascunhoStore
{
    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };
    private readonly string _caminho;
    private readonly object _trava = new();

    public RascunhoStore(string caminho) => _caminho = caminho;

    /// <summary>%LOCALAPPDATA%\NexusLS\rascunho.json</summary>
    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "rascunho.json");

    /// <summary>
    /// Grava num arquivo temporário e só depois troca pelo definitivo: se o programa ou o Windows
    /// travar no meio da gravação, o rascunho anterior continua inteiro.
    /// </summary>
    public void Salvar(Rascunho rascunho)
    {
        lock (_trava)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
            var temporario = _caminho + ".tmp";
            File.WriteAllText(temporario, JsonSerializer.Serialize(rascunho, Opcoes));
            File.Move(temporario, _caminho, overwrite: true);
        }
    }

    /// <summary>Devolve o rascunho, ou nulo se não existe ou está ilegível (nesse caso o arquivo é guardado como .corrompido).</summary>
    public Rascunho? Carregar()
    {
        lock (_trava)
        {
            if (!File.Exists(_caminho)) return null;
            try
            {
                return JsonSerializer.Deserialize<Rascunho>(File.ReadAllText(_caminho));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                try { File.Move(_caminho, _caminho + ".corrompido", overwrite: true); } catch (IOException) { }
                return null;
            }
        }
    }

    public void Descartar()
    {
        lock (_trava)
        {
            if (File.Exists(_caminho)) File.Delete(_caminho);
        }
    }
}

/// <summary>O que voltou do rascunho: o carrinho refeito, os dados soltos da tela e os avisos do que não deu para restaurar.</summary>
public record RascunhoRestaurado(Carrinho Carrinho, string? DescontoTexto, int? ClienteId, IReadOnlyList<string> Avisos);

/// <summary>
/// Salva o carrinho a cada mudança e o restaura ao abrir o programa. Na restauração tudo é conferido de novo
/// no banco (produto ainda ativo? tamanho existe? ainda há estoque?), porque o rascunho pode ser de dias atrás.
/// </summary>
public class RascunhoService
{
    private readonly RascunhoStore _store;
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public RascunhoService(RascunhoStore store, IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _store = store;
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Carrinho vazio não é rascunho: apaga o arquivo.</summary>
    public void Salvar(Carrinho carrinho, string? descontoTexto, int? clienteId)
    {
        if (carrinho.Vazio)
        {
            _store.Descartar();
            return;
        }

        _store.Salvar(new Rascunho
        {
            SalvoEm = _agora(),
            Tabela = carrinho.Tabela,
            DescontoTexto = string.IsNullOrWhiteSpace(descontoTexto) ? null : descontoTexto,
            ClienteId = clienteId,
            Itens = carrinho.Linhas.Select(l => new ItemRascunho
            {
                ProdutoId = l.ProdutoId,
                Tamanho = l.Tamanho,
                Quantidade = l.Quantidade,
                PrecoUnitarioEditadoCentavos = l.PrecoUnitarioEditadoCentavos,
                TotalEditadoCentavos = l.TotalEditadoCentavos
            }).ToList()
        });
    }

    public void Descartar() => _store.Descartar();

    /// <summary>Refaz o carrinho a partir do rascunho. Nulo se não há rascunho (ou se nada pôde ser restaurado).</summary>
    public async Task<RascunhoRestaurado?> RestaurarAsync()
    {
        var rascunho = _store.Carregar();
        if (rascunho is null || rascunho.Itens.Count == 0) return null;

        var avisos = new List<string>();
        var carrinho = new Carrinho();
        carrinho.MudarTabela(rascunho.Tabela);

        await using var db = await _fabrica.CreateDbContextAsync();
        var ids = rascunho.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Tamanhos)
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        foreach (var item in rascunho.Itens)
        {
            if (!produtos.TryGetValue(item.ProdutoId, out var produto))
            {
                avisos.Add("Um produto do rascunho não existe mais e foi removido do carrinho.");
                continue;
            }
            if (!produto.Ativo)
            {
                avisos.Add($"\"{produto.Nome}\" foi desativado e saiu do carrinho.");
                continue;
            }

            var tamanho = produto.Tamanhos.FirstOrDefault(t => t.Tamanho == item.Tamanho);
            if (tamanho is null)
            {
                avisos.Add($"\"{produto.Nome}\" não tem mais o tamanho {item.Tamanho} e saiu do carrinho.");
                continue;
            }

            if (produto.VenderSemBaixa) tamanho.Estoque = Produto.EstoqueVirtual;
            var livre = tamanho.Estoque - carrinho.QuantidadeDe(produto.Id, tamanho.Tamanho);
            if (livre <= 0)
            {
                avisos.Add($"\"{produto.Nome}\" ({tamanho.Tamanho}) está sem estoque e saiu do carrinho.");
                continue;
            }

            var quantidade = Math.Min(item.Quantidade, livre);
            if (quantidade < item.Quantidade)
                avisos.Add($"\"{produto.Nome}\" ({tamanho.Tamanho}): só há {livre} em estoque; a quantidade foi reduzida de {item.Quantidade} para {quantidade}.");

            var linha = carrinho.AdicionarLinha(produto, tamanho, quantidade);
            if (item.TotalEditadoCentavos is long total) carrinho.DefinirTotalLinha(linha, total);
            else if (item.PrecoUnitarioEditadoCentavos is long unitario) carrinho.DefinirPrecoUnitario(linha, unitario);
        }

        if (carrinho.Vazio)
        {
            _store.Descartar();
            return null;
        }

        int? clienteId = rascunho.ClienteId;
        if (clienteId is int id)
        {
            var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (cliente is null || !cliente.Ativo)
            {
                avisos.Add("O cliente do rascunho não está mais disponível e foi removido da venda.");
                clienteId = null;
            }
        }

        return new RascunhoRestaurado(carrinho, rascunho.DescontoTexto, clienteId, avisos);
    }
}
