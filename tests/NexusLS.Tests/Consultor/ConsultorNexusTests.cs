using NexusLS.Core.Consultor;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests.Consultor;

/// <summary>O serviço ConsultorNexus de ponta a ponta (banco em memória): leitura, permissões, cache, limite de cartões e "dispensar".</summary>
public sealed class ConsultorNexusTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-consultor-" + Guid.NewGuid().ToString("N"));
    private DateTime _agora = new(2026, 11, 19, 14, 0, 0);
    private readonly VendaService _vendas;
    private readonly ProdutoService _produtos;

    public ConsultorNexusTests()
    {
        Directory.CreateDirectory(_pasta);
        _vendas = new VendaService(_banco.Fabrica, () => _agora);
        _produtos = new ProdutoService(_banco.Fabrica);
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private string Arquivo => Path.Combine(_pasta, "consultor.json");
    private ConsultorNexus Consultor(TimeSpan? validade = null) =>
        new(_banco.Fabrica, new DispensasConsultorStore(Arquivo), () => _agora, validade: validade);

    private async Task<int> ProdutoAsync(string nome, long custo, int estoque, int minimo = 1) =>
        (await _produtos.CadastrarAsync(new DadosProduto(nome, null, 2000, custo, minimo), estoque)).Id;

    private async Task VenderAsync(int produtoId, int quantidade, int diasAtras, FormaPagamento forma = FormaPagamento.Pix)
    {
        var real = _agora;
        _agora = real.Date.AddDays(-diasAtras).AddHours(10);
        try
        {
            await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produtoId, quantidade) }, 0, forma));
        }
        finally { _agora = real; }
    }

    /// <summary>Uma loja com problemas em tudo: conta vencida, estoque acabando, estoque parado, mês no vermelho, aniversário.</summary>
    private async Task MontarLojaComProblemasAsync()
    {
        var casaco = await ProdutoAsync("Casaco de lã", 30_000, 5);
        await VenderAsync(casaco, 1, 70);                        // vendeu há 70 dias; sobram 4 (R$ 1.200 parados)
        var bone = await ProdutoAsync("Boné", 800, 12);
        for (var dia = 1; dia <= 10; dia++) await VenderAsync(bone, 1, dia); // 10 peças em 10 dias; sobram 2

        using var db = _banco.CriarContexto();
        db.Lancamentos.Add(new Lancamento
        {
            Tipo = TipoLancamento.Despesa, Descricao = "Aluguel", ValorCentavos = 250_000,
            Vencimento = _agora.Date.AddDays(-5), CriadoEm = _agora
        });
        db.LancamentosEmpresa.Add(new LancamentoEmpresa
        {
            Data = new DateTime(2026, 11, 10), Tipo = TipoMovimentoEmpresa.Saida, Categoria = "Fornecedor",
            Descricao = "Mercadoria", ValorCentavos = 900_000, CriadoEm = _agora, AlteradoEm = _agora
        });
        db.Clientes.Add(new Cliente { Nome = "Maria Souza", DataNascimento = new DateTime(1990, 11, 21), Ativo = true });
        db.SaveChanges();
    }

    [Fact]
    public async Task Banco_vazio_nao_gera_nenhum_cartao()
    {
        Assert.Empty(await Consultor().ObterAsync());
    }

    [Fact]
    public async Task Loja_com_problemas_gera_os_cartoes_certos_do_mais_grave_ao_menos()
    {
        await MontarLojaComProblemasAsync();
        var cartoes = await Consultor().ObterAsync();
        var ids = cartoes.Select(c => c.Id).ToList();

        Assert.Equal("contas-vencidas", ids[0]);   // urgente primeiro
        Assert.Contains("risco-de-ruptura", ids);
        Assert.Contains("estoque-parado", ids);
        Assert.Contains("saldo-do-mes-negativo", ids);
        Assert.Contains("aniversariantes-da-semana", ids);
        Assert.True(cartoes.Count <= ConsultorNexus.MaximoDeCartoes);
        Assert.Equal(cartoes.OrderByDescending(c => c.Gravidade).Select(c => c.Gravidade), cartoes.Select(c => c.Gravidade));

        var aluguel = cartoes.Single(c => c.Id == "contas-vencidas");
        Assert.Contains("Aluguel", aluguel.Texto);
        Assert.Contains(Dinheiro.Formatar(250_000), aluguel.Texto);
        var parado = cartoes.Single(c => c.Id == "estoque-parado");
        Assert.Contains("Casaco de lã", parado.Texto);
        Assert.Contains(Dinheiro.Formatar(120_000), parado.Texto);   // 4 peças x R$ 300
        var ruptura = cartoes.Single(c => c.Id == "risco-de-ruptura");
        Assert.Contains("Boné", ruptura.Texto);
        Assert.Contains("2 peças em estoque", ruptura.Texto);
        Assert.Contains("10 peças vendidas em 30 dias", ruptura.Texto);
    }

    [Fact]
    public async Task Sem_permissao_do_financeiro_nao_mostra_cartoes_financeiros_e_nem_le_o_financeiro()
    {
        await MontarLojaComProblemasAsync();
        var cartoes = await Consultor().ObterAsync(area => area != AreaConsultor.Financeiro);
        Assert.NotEmpty(cartoes);
        Assert.DoesNotContain(cartoes, c => c.Area == AreaConsultor.Financeiro);
        Assert.DoesNotContain(cartoes, c => c.Texto.Contains("Aluguel"));
    }

    [Fact]
    public async Task Sem_permissao_de_estoque_nao_mostra_cartoes_de_estoque()
    {
        await MontarLojaComProblemasAsync();
        var cartoes = await Consultor().ObterAsync(area => area != AreaConsultor.Estoque);
        Assert.DoesNotContain(cartoes, c => c.Area == AreaConsultor.Estoque);
        Assert.Contains(cartoes, c => c.Id == "contas-vencidas");
    }

    [Fact]
    public async Task Sem_nenhuma_permissao_nao_ha_cartoes()
    {
        await MontarLojaComProblemasAsync();
        Assert.Empty(await Consultor().ObterAsync(_ => false));
    }

    [Fact]
    public async Task Venda_cancelada_nao_entra_nas_contas_do_consultor()
    {
        var bone = await ProdutoAsync("Boné", 800, 100);
        var venda = (await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 40) }, 0, FormaPagamento.Pix)));
        await VenderAsync(bone, 1, 5);
        using (var db = _banco.CriarContexto())
        {
            var v = db.Vendas.Single(x => x.Id == venda.Id);
            v.Cancelada = true;
            db.SaveChanges();
        }
        // Só 1 peça vendida de verdade em 30 dias: abaixo do mínimo da regra, sem cartão de ruptura.
        Assert.DoesNotContain(await Consultor().ObterAsync(), c => c.Id == "risco-de-ruptura");
    }

    // ---------------------------------------------------------------- dispensar

    [Fact]
    public async Task Dispensar_esconde_o_cartao_e_a_escolha_fica_guardada_no_json()
    {
        await MontarLojaComProblemasAsync();
        var consultor = Consultor();
        Assert.Contains(await consultor.ObterAsync(), c => c.Id == "contas-vencidas");

        consultor.Dispensar("contas-vencidas");
        Assert.DoesNotContain(await consultor.ObterAsync(), c => c.Id == "contas-vencidas");
        Assert.True(File.Exists(Arquivo));

        // Outro Consultor (outra abertura do programa) lê o mesmo arquivo.
        Assert.DoesNotContain(await Consultor().ObterAsync(), c => c.Id == "contas-vencidas");
    }

    [Fact]
    public async Task Cartao_dispensado_volta_depois_de_7_dias()
    {
        await MontarLojaComProblemasAsync();
        var consultor = Consultor();
        consultor.Dispensar("contas-vencidas");

        _agora = _agora.AddDays(6);
        Assert.DoesNotContain(await consultor.ObterAsync(atualizar: true), c => c.Id == "contas-vencidas");

        _agora = _agora.AddDays(1).AddMinutes(1);
        Assert.Contains(await consultor.ObterAsync(atualizar: true), c => c.Id == "contas-vencidas");
    }

    [Fact]
    public void Arquivo_de_dispensas_estragado_e_ignorado_sem_quebrar()
    {
        File.WriteAllText(Arquivo, "{ isto não é json");
        var store = new DispensasConsultorStore(Arquivo);
        Assert.Empty(store.Ativas(_agora));
        store.Dispensar("x", _agora);                  // regrava por cima
        Assert.Contains("x", store.Ativas(_agora));
    }

    [Fact]
    public void Restaurar_traz_de_volta_os_cartoes_dispensados()
    {
        var store = new DispensasConsultorStore(Arquivo);
        store.Dispensar("a", _agora);
        store.Dispensar("b", _agora);
        store.RestaurarTodas();
        Assert.Empty(store.Ativas(_agora));
    }

    // ---------------------------------------------------------------- cache

    [Fact]
    public async Task Cache_curto_evita_ler_o_banco_de_novo_e_atualizar_ignora_o_cache()
    {
        await MontarLojaComProblemasAsync();
        var consultor = Consultor(TimeSpan.FromMinutes(5));
        await consultor.ObterAsync();
        await consultor.ObterAsync();
        Assert.Equal(1, consultor.Leituras);

        await consultor.ObterAsync(atualizar: true);
        Assert.Equal(2, consultor.Leituras);

        _agora = _agora.AddMinutes(4);
        await consultor.ObterAsync();
        Assert.Equal(2, consultor.Leituras);

        _agora = _agora.AddMinutes(2);          // passou de 5 minutos desde a última leitura
        await consultor.ObterAsync();
        Assert.Equal(3, consultor.Leituras);
    }

    [Fact]
    public async Task Cache_nao_serve_dados_lidos_para_outra_permissao()
    {
        await MontarLojaComProblemasAsync();
        var consultor = Consultor();
        await consultor.ObterAsync(area => area == AreaConsultor.Vendas);
        var cartoes = await consultor.ObterAsync();          // agora com tudo liberado: tem que reler
        Assert.Equal(2, consultor.Leituras);
        Assert.Contains(cartoes, c => c.Id == "contas-vencidas");
    }

    [Fact]
    public async Task Dispensar_nao_exige_ler_o_banco_de_novo()
    {
        await MontarLojaComProblemasAsync();
        var consultor = Consultor();
        await consultor.ObterAsync();
        consultor.Dispensar("estoque-parado");
        Assert.DoesNotContain(await consultor.ObterAsync(), c => c.Id == "estoque-parado");
        Assert.Equal(1, consultor.Leituras);
    }

    // ---------------------------------------------------------------- avaliação pura

    private sealed class RegraFalsa(string id, AreaConsultor area, GravidadeSugestao gravidade, bool dispara = true, bool quebra = false) : IRegraConsultor
    {
        public string Id => id;
        public AreaConsultor Area => area;
        public Sugestao? Avaliar(DadosConsultor dados)
        {
            if (quebra) throw new InvalidOperationException("regra com defeito");
            return dispara ? new Sugestao(id, id, "texto", gravidade, area, "Abrir") : null;
        }
    }

    [Fact]
    public void Avaliar_limita_a_cinco_cartoes_e_ordena_por_gravidade_depois_pela_ordem_das_regras()
    {
        var regras = new IRegraConsultor[]
        {
            new RegraFalsa("a", AreaConsultor.Vendas, GravidadeSugestao.Informacao),
            new RegraFalsa("b", AreaConsultor.Vendas, GravidadeSugestao.Atencao),
            new RegraFalsa("c", AreaConsultor.Estoque, GravidadeSugestao.Urgente),
            new RegraFalsa("d", AreaConsultor.Financeiro, GravidadeSugestao.Atencao),
            new RegraFalsa("e", AreaConsultor.Vendas, GravidadeSugestao.Informacao),
            new RegraFalsa("f", AreaConsultor.Vendas, GravidadeSugestao.Informacao),
            new RegraFalsa("g", AreaConsultor.Vendas, GravidadeSugestao.Urgente),
        };
        var r = ConsultorNexus.Avaliar(new DadosConsultor(_agora), regras);
        Assert.Equal(["c", "g", "b", "d", "a"], r.Select(x => x.Id));
    }

    [Fact]
    public void Avaliar_pula_regra_que_nao_dispara_que_quebra_ou_que_foi_dispensada()
    {
        var regras = new IRegraConsultor[]
        {
            new RegraFalsa("quieta", AreaConsultor.Vendas, GravidadeSugestao.Atencao, dispara: false),
            new RegraFalsa("quebrada", AreaConsultor.Vendas, GravidadeSugestao.Atencao, quebra: true),
            new RegraFalsa("dispensada", AreaConsultor.Vendas, GravidadeSugestao.Atencao),
            new RegraFalsa("boa", AreaConsultor.Vendas, GravidadeSugestao.Atencao),
        };
        var r = ConsultorNexus.Avaliar(new DadosConsultor(_agora), regras, dispensadas: new HashSet<string> { "dispensada" });
        Assert.Equal(["boa"], r.Select(x => x.Id));
    }

    [Fact]
    public void Avaliar_respeita_as_areas_permitidas()
    {
        var regras = new IRegraConsultor[]
        {
            new RegraFalsa("v", AreaConsultor.Vendas, GravidadeSugestao.Atencao),
            new RegraFalsa("f", AreaConsultor.Financeiro, GravidadeSugestao.Urgente),
        };
        var r = ConsultorNexus.Avaliar(new DadosConsultor(_agora), regras, a => a != AreaConsultor.Financeiro);
        Assert.Equal(["v"], r.Select(x => x.Id));
    }
}
