using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Operacional;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Clientes, fornecedores, compras e pedidos no envio operacional.</summary>
public sealed class OperacionalCadastrosTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid-ana");
    private readonly DestinoOperacionalFalso _destino = new();
    private readonly DateTime _agora = new(2026, 10, 31, 9, 0, 0);

    public void Dispose() => _banco.Dispose();

    private MontadorOperacional Montador => new(_banco.Fabrica);

    private async Task<DocumentoOperacional> Documento(string entidade, Guid guid)
    {
        var r = await Montador.MontarAsync(entidade, new[] { guid });
        Assert.Empty(r.Ausentes);
        return Assert.Single(r.Documentos);
    }

    [Fact]
    public async Task Cliente_e_fornecedor_vao_para_as_suas_colecoes()
    {
        Cliente cli; Fornecedor forn;
        await using (var db = _banco.CriarContexto())
        {
            cli = new Cliente { Nome = "Ana", Telefone = "8199", LimiteCreditoCentavos = 50000, DataCadastro = _agora };
            forn = new Fornecedor { Nome = "Malharia", Email = "m@x.com", DataCadastro = _agora };
            db.Clientes.Add(cli);
            db.Fornecedores.Add(forn);
            await db.SaveChangesAsync();
        }

        var dCli = await Documento(NomesEntidade.Cliente, cli.Guid);
        var dForn = await Documento(NomesEntidade.Fornecedor, forn.Guid);

        Assert.Equal("clientes", dCli.Colecao);
        Assert.Equal(50000L, dCli.Campos["limiteCreditoCentavos"]);
        Assert.Equal(true, dCli.Campos["ativo"]);
        Assert.Equal("fornecedores", dForn.Colecao);
        Assert.Equal("m@x.com", dForn.Campos["email"]);
    }

    [Fact]
    public async Task Compra_leva_os_itens_e_aponta_para_fornecedor_e_produtos_pelo_Guid()
    {
        Fornecedor forn; Produto prod; Compra compra;
        await using (var db = _banco.CriarContexto())
        {
            forn = new Fornecedor { Nome = "Malharia", DataCadastro = _agora };
            prod = new Produto { Nome = "Camiseta", Tamanhos = { new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho } } };
            db.Fornecedores.Add(forn);
            db.Produtos.Add(prod);
            await db.SaveChangesAsync();
            compra = new Compra
            {
                FornecedorId = forn.Id, DataHora = _agora, NumeroNota = "123", TotalCentavos = 3000,
                Itens = { new ItemCompra { ProdutoId = prod.Id, NomeProduto = "Camiseta", Quantidade = 3, CustoUnitarioCentavos = 1000 } }
            };
            db.Compras.Add(compra);
            await db.SaveChangesAsync();
        }

        var d = await Documento(NomesEntidade.Compra, compra.Guid);

        Assert.Equal("compras", d.Colecao);
        Assert.Equal(forn.Guid, d.Campos["fornecedorGuid"]);
        var item = Assert.Single((IEnumerable<object?>)d.Campos["itens"]!);
        var campos = Assert.IsAssignableFrom<IDictionary<string, object?>>(item);
        Assert.Equal(prod.Guid, campos["produtoGuid"]);
        Assert.Equal(3, campos["quantidade"]);
        Assert.Equal(3000L, campos["totalCentavos"]);
    }

    [Fact]
    public async Task Pedido_leva_itens_historico_e_referencias()
    {
        Cliente cli; Produto prod; Pedido ped;
        await using (var db = _banco.CriarContexto())
        {
            cli = new Cliente { Nome = "Ana", DataCadastro = _agora };
            prod = new Produto { Nome = "Boné", Tamanhos = { new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho } } };
            db.Clientes.Add(cli);
            db.Produtos.Add(prod);
            await db.SaveChangesAsync();
            ped = new Pedido
            {
                Numero = "PED-2610-0001", ClienteId = cli.Id, ClienteNome = "Ana", Origem = OrigemPedido.WhatsApp, Status = StatusPedido.Separado,
                TotalCentavos = 8000, CriadoEm = _agora, AtualizadoEm = _agora,
                Itens = { new ItemPedido { ProdutoId = prod.Id, Nome = "Boné", Quantidade = 2, PrecoUnitarioCentavos = 4000 },
                          new ItemPedido { Nome = "Item livre", Quantidade = 1, PrecoUnitarioCentavos = 0 } },
                Historico = { new EventoPedido { Status = StatusPedido.Novo, DataHora = _agora, Operador = "ana" } }
            };
            db.Pedidos.Add(ped);
            await db.SaveChangesAsync();
        }

        var d = await Documento(NomesEntidade.Pedido, ped.Guid);

        Assert.Equal("pedidos", d.Colecao);
        Assert.Equal(cli.Guid, d.Campos["clienteGuid"]);
        Assert.Null(d.Campos["vendaGuid"]);
        Assert.Equal("Separado", d.Campos["status"]);
        var itens = ((IEnumerable<object?>)d.Campos["itens"]!).Cast<IDictionary<string, object?>>().ToList();
        Assert.Equal(2, itens.Count);
        Assert.Equal(prod.Guid, itens[0]["produtoGuid"]);
        Assert.Null(itens[1]["produtoGuid"]); // item digitado sem produto cadastrado
        Assert.Single((IEnumerable<object?>)d.Campos["historico"]!);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)d.Campos["atualizadoEm"]!).Kind);
    }

    [Fact]
    public async Task Processador_envia_os_cadastros_e_os_antigos_entram_na_fila_uma_vez_so()
    {
        await using (var db = _banco.CriarContexto())
        {
            db.Clientes.Add(new Cliente { Nome = "Ana", DataCadastro = _agora });
            db.Fornecedores.Add(new Fornecedor { Nome = "Malharia", DataCadastro = _agora });
            await db.SaveChangesAsync();
            db.FilaSincronizacao.RemoveRange(db.FilaSincronizacao);
            await db.SaveChangesAsync();
        }
        var proc = new ProcessadorFilaOperacional(_banco.Fabrica, Montador, _destino, () => _agora);

        Assert.Equal(2, await proc.EnfileirarExistentesAsync());
        Assert.Equal(0, await proc.EnfileirarExistentesAsync());
        Assert.Equal(2, (await proc.ProcessarAsync()).Enviados);
        Assert.Equal(new[] { "clientes", "fornecedores" }, _destino.Todos.Select(d => d.Colecao).OrderBy(c => c).ToArray());
    }
}
