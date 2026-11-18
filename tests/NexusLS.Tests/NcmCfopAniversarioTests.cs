using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>NCM/CFOP por produto e aniversário do cliente: campos só locais, sem tocar na sincronização com o ERP.</summary>
public sealed class NcmCfopAniversarioTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static DadosProduto Dados(string nome = "Camiseta", string? ncm = null, string? cfop = null) =>
        new(nome, null, 5990, 2000, 0, "R1", Ncm: ncm, Cfop: cfop);

    [Fact]
    public async Task Banco_migrado_abre_e_guarda_ncm_cfop_e_nascimento()
    {
        var p = await new ProdutoService(_banco.Fabrica).CadastrarAsync(Dados(ncm: "6109.1000", cfop: "5102"));
        var c = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Ana", DataNascimento: new DateTime(1990, 3, 5, 14, 0, 0)));

        await using var db = _banco.CriarContexto();
        var produto = await db.Produtos.SingleAsync(x => x.Id == p.Id);
        Assert.Equal("61091000", produto.Ncm);
        Assert.Equal("5102", produto.Cfop);
        Assert.Equal(new DateTime(1990, 3, 5), (await db.Clientes.SingleAsync(x => x.Id == c.Id)).DataNascimento);
    }

    [Fact]
    public async Task Colunas_novas_sao_anulaveis_e_registros_sem_elas_continuam_validos()
    {
        var p = await new ProdutoService(_banco.Fabrica).CadastrarAsync(Dados());
        await using var db = _banco.CriarContexto();
        var produto = await db.Produtos.SingleAsync(x => x.Id == p.Id);
        Assert.Null(produto.Ncm);
        Assert.Null(produto.Cfop);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234567A")]
    public async Task Ncm_invalido_e_recusado(string ncm) =>
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => new ProdutoService(_banco.Fabrica).CadastrarAsync(Dados(ncm: ncm)));

    [Theory]
    [InlineData("510")]
    [InlineData("51022")]
    [InlineData("51A2")]
    public async Task Cfop_invalido_e_recusado(string cfop) =>
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => new ProdutoService(_banco.Fabrica).CadastrarAsync(Dados(cfop: cfop)));

    [Fact]
    public async Task Vazio_limpa_e_nulo_preserva_na_edicao()
    {
        var servico = new ProdutoService(_banco.Fabrica);
        var p = await servico.CadastrarAsync(Dados(ncm: "61091000", cfop: "5102"));

        await servico.EditarAsync(p.Id, Dados(ncm: null, cfop: null)); // ex.: importação por CSV: não mexe
        await using (var db = _banco.CriarContexto())
            Assert.Equal("61091000", (await db.Produtos.SingleAsync()).Ncm);

        await servico.EditarAsync(p.Id, Dados(ncm: "", cfop: "")); // tela: vazio volta ao padrão da página Fiscal
        await using (var db = _banco.CriarContexto())
        {
            var x = await db.Produtos.SingleAsync();
            Assert.Null(x.Ncm);
            Assert.Null(x.Cfop);
        }
    }

    [Fact]
    public async Task Nascimento_no_futuro_e_recusado()
    {
        var servico = new ClienteService(_banco.Fabrica);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CadastrarAsync(new DadosCliente("Ana", DataNascimento: DateTime.Today.AddDays(1))));
    }

    [Fact]
    public void Alterar_so_ncm_cfop_nao_muda_o_hash_de_sincronizacao_nem_a_mascara_do_erp()
    {
        var produto = new Produto
        {
            Guid = Guid.Parse("33333333-3333-4333-8333-333333333333"), Nome = "Boné", PrecoVendaCentavos = 3000,
            Tamanhos = { new ProdutoTamanho { Tamanho = "Sem tamanho", Estoque = 4 } }
        };
        var antes = RegrasSincronizacao.Hash(RegrasSincronizacao.Montar(produto));

        produto.Ncm = "65050010";
        produto.Cfop = "5102";

        Assert.Equal(antes, RegrasSincronizacao.Hash(RegrasSincronizacao.Montar(produto)));
        Assert.DoesNotContain(FirestoreJson.CamposDoErp, c => c.Contains("ncm", StringComparison.OrdinalIgnoreCase) || c.Contains("cfop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reimportar_produto_do_erp_nao_apaga_ncm_e_cfop()
    {
        var fonte = new FonteErpFalsa();
        fonte.Por("produtos", "p1", """{"nome":"Camiseta","categoria":"Camisetas","referencia":"R-p1","precoVarejo":59.9,"ativo":true,"gradeTamanhos":{"P":3},"atualizadoEm":"2026-10-30T10:00:00Z"}""");
        var agora = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var imp = new ImportadorErp(_banco.Fabrica, fonte, () => agora);
        await imp.ImportarAsync();

        await using (var db = _banco.CriarContexto())
        {
            var p = await db.Produtos.SingleAsync();
            p.Ncm = "61091000";
            p.Cfop = "5102";
            await db.SaveChangesAsync();
        }

        agora = agora.AddHours(1);
        fonte.Por("produtos", "p1", """{"nome":"Camiseta Nova","precoVarejo":70,"gradeTamanhos":{"P":1},"atualizadoEm":"2026-10-31T12:30:00Z"}""");
        var r = await imp.ImportarAsync();

        Assert.Equal(1, r.Atualizados);
        await using var db2 = _banco.CriarContexto();
        var depois = await db2.Produtos.SingleAsync();
        Assert.Equal("Camiseta Nova", depois.Nome);
        Assert.Equal("61091000", depois.Ncm);
        Assert.Equal("5102", depois.Cfop);
    }

    [Fact]
    public async Task Reimportar_cliente_do_erp_nao_apaga_o_aniversario()
    {
        var fonte = new FonteErpFalsa();
        fonte.Por("clientes", "c1", """{"nome":"Ana","documento":"123","atualizadoEm":"2026-10-30T09:00:00Z"}""");
        var agora = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var imp = new ImportadorErp(_banco.Fabrica, fonte, () => agora);
        await imp.ImportarAsync();

        await using (var db = _banco.CriarContexto())
        {
            (await db.Clientes.SingleAsync()).DataNascimento = new DateTime(1990, 5, 20);
            await db.SaveChangesAsync();
        }

        agora = agora.AddHours(1);
        fonte.Por("clientes", "c1", """{"nome":"Ana Maria","documento":"123","atualizadoEm":"2026-10-31T12:30:00Z"}""");
        await imp.ImportarAsync();

        await using var db2 = _banco.CriarContexto();
        var c = await db2.Clientes.SingleAsync();
        Assert.Equal("Ana Maria", c.Nome);
        Assert.Equal(new DateTime(1990, 5, 20), c.DataNascimento);
    }

    // ----- aniversário do cliente -----

    private static Cliente C(string nome, DateTime? nasc, bool ativo = true) => new() { Nome = nome, DataNascimento = nasc, Ativo = ativo };

    [Fact]
    public void Aviso_de_clientes_pega_hoje_e_semana_ignora_inativos_e_sem_data()
    {
        var hoje = new DateTime(2026, 11, 18);
        var linhas = AvisosAoAbrir.AniversariantesClientes(new[]
        {
            C("Hoje", new DateTime(1990, 11, 18)), C("Sete", new DateTime(1985, 11, 25)), C("Oito", new DateTime(1985, 11, 26)),
            C("Inativo", new DateTime(1985, 11, 19), ativo: false), C("SemData", null)
        }, hoje);
        Assert.Contains("clientes", linhas[0]);
        Assert.Equal(3, linhas.Count);
        Assert.Contains("Hoje", linhas[1]);
        Assert.Contains("Sete", linhas[2]);
    }

    [Fact]
    public void Aviso_de_clientes_cobre_virada_de_ano_e_29_de_fevereiro()
    {
        var virada = AvisosAoAbrir.AniversariantesProximos(new[] { C("Ana", new DateTime(2000, 1, 3)) }, new DateTime(2026, 12, 30));
        Assert.Equal(new DateTime(2027, 1, 3), virada.Single().Data);
        var f = C("Bissexto", new DateTime(2000, 2, 29));
        Assert.Equal(new DateTime(2027, 2, 28), AvisosAoAbrir.AniversariantesProximos(new[] { f }, new DateTime(2027, 2, 25)).Single().Data);
        Assert.Equal(new DateTime(2028, 2, 29), AvisosAoAbrir.AniversariantesProximos(new[] { f }, new DateTime(2028, 2, 27)).Single().Data);
        Assert.Empty(AvisosAoAbrir.AniversariantesClientes(Array.Empty<Cliente>(), new DateTime(2026, 11, 18)));
    }

    [Fact]
    public void Filtro_do_mes_ordena_por_dia_e_inclui_29_de_fevereiro_em_fevereiro()
    {
        var lista = AvisosAoAbrir.ClientesDoMes(new[]
        {
            C("B", new DateTime(1992, 2, 29)), C("A", new DateTime(1980, 2, 3)), C("Outro", new DateTime(1980, 3, 1)), C("Sem", null)
        }, 2);
        Assert.Equal(new[] { "A", "B" }, lista.Select(c => c.Nome));
    }

    [Fact]
    public async Task Listar_com_aniversario_so_traz_ativos_com_data()
    {
        var s = new ClienteService(_banco.Fabrica);
        await s.CadastrarAsync(new DadosCliente("Com", DataNascimento: new DateTime(1990, 1, 1)));
        await s.CadastrarAsync(new DadosCliente("Sem"));
        var inativo = await s.CadastrarAsync(new DadosCliente("Inativo", DataNascimento: new DateTime(1990, 1, 1)));
        await s.DesativarAsync(inativo.Id);
        Assert.Equal(new[] { "Com" }, (await s.ListarComAniversarioAsync()).Select(c => c.Nome));
    }

    [Fact]
    public void Flag_de_aviso_de_clientes_vem_ligada_e_persiste()
    {
        Assert.True(new ConfiguracaoSeguranca().AvisarAniversariantesClientes);
        var caminho = Path.Combine(Path.GetTempPath(), "nexus-anivcli-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ConfiguracaoSegurancaStore(caminho);
            store.Salvar(new ConfiguracaoSeguranca { AvisarAniversariantesClientes = false });
            Assert.False(store.Carregar().AvisarAniversariantesClientes);
        }
        finally { File.Delete(caminho); File.Delete(caminho + ".tmp"); }
    }
}
