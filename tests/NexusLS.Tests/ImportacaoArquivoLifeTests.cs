using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class ImportacaoArquivoLifeTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid");
    private static readonly Func<DateTime> Agora = () => new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _banco.Dispose();

    private static string Exemplo() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Dados", "life-export-exemplo.json"));

    private Task<ResultadoImportacaoErp> Importar(ArquivoLife a) => ImportacaoArquivoLife.ImportarAsync(a, _banco.Fabrica, agora: Agora);

    [Theory]
    [InlineData("isto nao e json")]
    [InlineData("[1,2]")]
    [InlineData("""{"formato":"outro","versao":1,"colecoes":{}}""")]
    [InlineData("""{"formato":"nexus-life-export","colecoes":{}}""")]
    [InlineData("""{"formato":"nexus-life-export","versao":1}""")]
    [InlineData("""{"formato":"nexus-life-export","versao":1,"colecoes":{"produtos":{"a":1}}}""")]
    public void Formato_invalido_gera_mensagem_clara(string json)
    {
        var ex = Assert.Throws<RegraDeNegocioException>(() => ArquivoLife.Interpretar(json));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Versao_futura_pede_atualizar_o_nexus()
    {
        var ex = Assert.Throws<RegraDeNegocioException>(() =>
            ArquivoLife.Interpretar("""{"formato":"nexus-life-export","versao":2,"colecoes":{}}"""));
        Assert.Contains("mais nova", ex.Message);
        Assert.Contains("Atualize o Nexus", ex.Message);
    }

    [Fact]
    public void Exemplo_e_lido_e_separa_o_que_o_nexus_nao_importa()
    {
        var a = ArquivoLife.Interpretar(Exemplo());
        Assert.Equal("lifesurf-principal", a.EmpresaId);
        Assert.Equal(2, a.Colecoes["produtos"].Count);
        Assert.Single(a.Colecoes["caixa_turnos"]); // caixas_fechamentos
        Assert.Contains(a.NaoImportadas, n => n.Colecao == "funcionarios" && n.Quantidade == 1);
        Assert.Contains(a.NaoImportadas, n => n.Colecao == "fornecedores");
        Assert.DoesNotContain("id", a.Colecoes["produtos"][0].Campos.Select(p => p.Key));
    }

    [Fact]
    public void Pin_e_senha_nunca_passam_nem_dentro_de_objetos()
    {
        var a = ArquivoLife.Interpretar("""
            {"formato":"nexus-life-export","versao":1,"colecoes":{"clientes":[{"id":"c","nome":"X","pin":"1","aninhado":{"senha":"s","ok":1},"lista":[{"passwordHash":"h","k":2}]}]}}
            """);
        var json = a.Colecoes["clientes"][0].Campos.ToJsonString();
        Assert.DoesNotContain("pin", json);
        Assert.DoesNotContain("senha", json);
        Assert.DoesNotContain("passwordHash", json);
        Assert.Contains("\"ok\":1", json);
    }

    [Fact]
    public void Timestamp_do_firestore_vira_texto_iso()
    {
        var a = ArquivoLife.Interpretar("""
            {"formato":"nexus-life-export","versao":1,"colecoes":{"clientes":[{"id":"c","nome":"X","criadoEm":{"seconds":1767225600,"nanoseconds":0}}]}}
            """);
        Assert.StartsWith("2026-01-01T00:00:00", a.Colecoes["clientes"][0].Campos["criadoEm"]!.GetValue<string>());
    }

    [Fact]
    public async Task Mapeia_produtos_clientes_turno_e_venda_sem_baixar_estoque()
    {
        var r = await Importar(ArquivoLife.Interpretar(Exemplo()));

        Assert.Equal(5, r.Novos);
        await using var db = _banco.CriarContexto();
        var p = await db.Produtos.Include(x => x.Tamanhos).SingleAsync(x => x.Nome == "Camiseta Surf");
        Assert.Equal(5990, p.PrecoVendaCentavos);
        Assert.Equal(4500, p.PrecoAtacadoCentavos);
        Assert.Equal(2250, p.PrecoCustoCentavos);
        Assert.Equal("7891234567895", p.CodigoBarras);
        Assert.Equal(10, p.EstoqueAtual);
        Assert.Equal(5, p.Tamanhos.Single(t => t.Tamanho == "M").Estoque);
        Assert.Equal(7, (await db.Produtos.Include(x => x.Tamanhos).SingleAsync(x => x.Nome == "Bone Aba Reta")).EstoqueAtual);

        var cli = await db.Clientes.SingleAsync();
        Assert.Equal("Ana Souza", cli.Nome);
        Assert.Equal("PE", cli.Estado);
        Assert.Equal(50000, cli.LimiteCreditoCentavos);

        Assert.Equal(10000, (await db.TurnosCaixa.SingleAsync()).TrocoInicialCentavos);

        var venda = await db.Vendas.Include(v => v.Itens).SingleAsync();
        Assert.Equal(11980, venda.TotalCentavos);
        Assert.Equal(cli.Id, venda.ClienteId);
        Assert.Equal(p.Id, venda.Itens.Single().ProdutoId);
    }

    [Fact]
    public async Task Importar_duas_vezes_nao_duplica()
    {
        await Importar(ArquivoLife.Interpretar(Exemplo()));
        var r2 = await Importar(ArquivoLife.Interpretar(Exemplo()));

        Assert.Equal(0, r2.Novos);
        Assert.Equal(0, r2.Atualizados);
        await using var db = _banco.CriarContexto();
        Assert.Equal(2, await db.Produtos.CountAsync());
        Assert.Equal(1, await db.Clientes.CountAsync());
        Assert.Equal(1, await db.Vendas.CountAsync());
        Assert.Equal(1, await db.TurnosCaixa.CountAsync());
    }

    [Fact]
    public async Task Documento_alterado_no_arquivo_atualiza_o_mesmo_registro()
    {
        await Importar(ArquivoLife.Interpretar(Exemplo()));
        var json = JsonNode.Parse(Exemplo())!;
        json["colecoes"]!["produtos"]![0]!["precoVarejo"] = 70;
        var r = await Importar(ArquivoLife.Interpretar(json.ToJsonString()));

        Assert.Equal(1, r.Atualizados);
        await using var db = _banco.CriarContexto();
        Assert.Equal(2, await db.Produtos.CountAsync());
        Assert.Equal(7000, (await db.Produtos.SingleAsync(x => x.Nome == "Camiseta Surf")).PrecoVendaCentavos);
    }

    [Fact]
    public async Task Previa_conta_sem_gravar_nada_e_bate_com_a_importacao_real()
    {
        var arquivo = ArquivoLife.Interpretar(Exemplo());
        var previa = await ImportacaoArquivoLife.PreverAsync(arquivo, _banco.Fabrica, agora: Agora);

        await using (var db = _banco.CriarContexto())
        {
            Assert.Equal(0, await db.Produtos.CountAsync());
            Assert.Equal(0, await db.ImportacaoErp.CountAsync());
        }
        var real = await Importar(arquivo);
        Assert.Equal(real.Novos, previa.Novos);
        Assert.Equal(5, previa.Novos);

        var previa2 = await ImportacaoArquivoLife.PreverAsync(arquivo, _banco.Fabrica, agora: Agora);
        Assert.Equal(0, previa2.Novos);
        Assert.Equal(5, previa2.Colecoes.Sum(c => c.SemMudanca));
    }

    [Fact]
    public async Task Importar_arquivo_nao_mexe_nos_cursores_do_erp()
    {
        await Importar(ArquivoLife.Interpretar(Exemplo()));
        await using var db = _banco.CriarContexto();
        Assert.Equal(0, await db.ImportacaoErpCursores.CountAsync());
    }

    [Fact]
    public async Task Documento_sem_nome_e_contado_como_ignorado()
    {
        var a = ArquivoLife.Interpretar("""{"formato":"nexus-life-export","versao":1,"colecoes":{"produtos":[{"id":"x","precoVarejo":1}]}}""");
        var r = await Importar(a);
        Assert.Equal(1, r.Colecoes.Single(c => c.Colecao == "produtos").Ignorados);
    }
}
