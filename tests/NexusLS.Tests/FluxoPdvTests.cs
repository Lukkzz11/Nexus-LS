using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class GradeTamanhosTestsFluxo
{
    private static ProdutoTamanho T(string nome, int estoque) => new() { Tamanho = nome, Estoque = estoque };

    private static GradeTamanhos Grade(int quantos)
    {
        var tamanhos = Enumerable.Range(0, quantos).Select(i => T($"T{i}", 5));
        return new GradeTamanhos(tamanhos);
    }

    [Fact]
    public void Comeca_no_primeiro_tamanho_com_estoque()
    {
        var g = new GradeTamanhos(new[] { T("P", 0), T("M", 0), T("G", 3), T("GG", 5) });
        Assert.Equal("G", g.Selecionado!.Tamanho);
    }

    [Fact]
    public void Se_tudo_esta_esgotado_comeca_no_primeiro()
    {
        var g = new GradeTamanhos(new[] { T("P", 0), T("M", 0) });
        Assert.Equal("P", g.Selecionado!.Tamanho);
    }

    [Fact]
    public void Setas_andam_na_grade_de_3_colunas_sem_passar_das_pontas()
    {
        var g = Grade(7); // T0 T1 T2 / T3 T4 T5 / T6

        Assert.False(g.Mover(DirecaoGrade.Esquerda));     // já está no começo
        Assert.False(g.Mover(DirecaoGrade.Cima));
        Assert.True(g.Mover(DirecaoGrade.Direita));       // T1
        Assert.True(g.Mover(DirecaoGrade.Baixo));         // T4
        Assert.Equal("T4", g.Selecionado!.Tamanho);
        Assert.False(g.Mover(DirecaoGrade.Baixo));        // T7 não existe
        Assert.True(g.Mover(DirecaoGrade.Direita));       // T5
        Assert.False(g.Mover(DirecaoGrade.Baixo));        // T8 não existe
        Assert.True(g.Mover(DirecaoGrade.Esquerda));      // T4
        Assert.True(g.Mover(DirecaoGrade.Cima));          // T1
        Assert.Equal("T1", g.Selecionado!.Tamanho);
    }

    [Fact]
    public void Ultima_linha_incompleta_so_aceita_o_que_existe()
    {
        var g = Grade(4); // T0 T1 T2 / T3
        g.Mover(DirecaoGrade.Baixo); // T3
        Assert.False(g.Mover(DirecaoGrade.Direita)); // não há T4
        Assert.Equal("T3", g.Selecionado!.Tamanho);
    }

    [Fact]
    public void Sem_tamanho_so_aparece_no_atacado_ou_quando_nao_ha_grade()
    {
        var grade = new[] { T(Tamanhos.SemTamanho, 4), T("P", 2), T("M", 3) };

        var varejo = GradeTamanhos.OpcoesParaVenda(grade, TabelaPreco.Varejo).Select(o => o.Tamanho);
        var atacado = GradeTamanhos.OpcoesParaVenda(grade, TabelaPreco.Atacado).Select(o => o.Tamanho);
        var semGrade = GradeTamanhos.OpcoesParaVenda(new[] { T(Tamanhos.SemTamanho, 4) }, TabelaPreco.Varejo).Select(o => o.Tamanho);

        Assert.Equal(new[] { "P", "M" }, varejo);
        Assert.Equal(new[] { "P", "M", Tamanhos.SemTamanho }, atacado);
        Assert.Equal(new[] { Tamanhos.SemTamanho }, semGrade);
    }
}

public class SeletorQuantidadeTests
{
    [Fact]
    public void Comeca_em_1_soma_ate_o_maximo_e_nunca_passa_de_1_para_baixo()
    {
        var q = new SeletorQuantidade(3);

        Assert.Equal(1, q.Valor);
        Assert.False(q.Diminuir());
        Assert.True(q.Aumentar());
        Assert.True(q.Aumentar());
        Assert.False(q.Aumentar()); // máximo 3
        Assert.Equal(3, q.Valor);
        Assert.True(q.Diminuir());
        Assert.Equal(2, q.Valor);
    }

    [Fact]
    public void Sem_estoque_nao_abre()
    {
        Assert.Throws<RegraDeNegocioException>(() => new SeletorQuantidade(0));
    }
}

public class SelecaoDescontoTests
{
    [Fact]
    public void Vazio_e_sem_desconto()
    {
        var d = new SelecaoDesconto { Texto = "  " }.Resolver(10000);
        Assert.Equal(new DescontoResolvido(0, null, 0), d);
    }

    [Fact]
    public void Setas_escolhem_reais_e_percentual()
    {
        var d = new SelecaoDesconto();
        Assert.Equal(TipoDesconto.Reais, d.Tipo);
        Assert.False(d.Esquerda());   // já é R$
        Assert.True(d.Direita());
        Assert.Equal(TipoDesconto.Percentual, d.Tipo);
        Assert.False(d.Direita());
        Assert.True(d.Esquerda());
    }

    [Fact]
    public void Desconto_em_reais()
    {
        var d = new SelecaoDesconto { Texto = "5,50" }.Resolver(10000);
        Assert.Equal(550, d.Centavos);
        Assert.Null(d.Percentual);
        Assert.Equal(550, d.ValorEmCentavos);
    }

    [Theory]
    [InlineData("10", 10, 1000)]
    [InlineData("7,5", 7.5, 750)]
    [InlineData("7.5", 7.5, 750)]
    [InlineData("10%", 10, 1000)]
    [InlineData(" 100 ", 100, 10000)]
    public void Desconto_em_percentual_vira_valor(string texto, double percentual, long valor)
    {
        var selecao = new SelecaoDesconto { Texto = texto };
        selecao.Direita();

        var d = selecao.Resolver(10000);

        Assert.Equal(0, d.Centavos);               // o pedido leva o %, não o R$
        Assert.Equal((decimal)percentual, d.Percentual);
        Assert.Equal(valor, d.ValorEmCentavos);
    }

    [Theory]
    [InlineData(TipoDesconto.Reais, "abc")]
    [InlineData(TipoDesconto.Reais, "100,01")]   // maior que o subtotal de R$ 100,00
    [InlineData(TipoDesconto.Reais, "-5")]
    [InlineData(TipoDesconto.Percentual, "abc")]
    [InlineData(TipoDesconto.Percentual, "101")]
    [InlineData(TipoDesconto.Percentual, "-1")]
    public void Entradas_invalidas_sao_recusadas(TipoDesconto tipo, string texto)
    {
        var selecao = new SelecaoDesconto { Texto = texto };
        if (tipo == TipoDesconto.Percentual) selecao.Direita();
        Assert.Throws<RegraDeNegocioException>(() => selecao.Resolver(10000));
    }
}

public class SelecaoPagamentoTests
{
    [Fact]
    public void Comeca_em_dinheiro_com_o_valor_recebido_igual_ao_total()
    {
        var p = new SelecaoPagamento(12550);

        Assert.Equal(FormaPagamento.Dinheiro, p.Forma);
        Assert.Equal(12550, p.ValorRecebidoCentavos);
        Assert.Equal(0, p.TrocoCentavos);
        Assert.Null(p.Validar());
    }

    [Fact]
    public void Setas_percorrem_as_seis_formas_dando_a_volta()
    {
        var p = new SelecaoPagamento(1000);
        var vistas = new List<FormaPagamento> { p.Forma };
        for (var i = 0; i < 5; i++) { p.Proxima(); vistas.Add(p.Forma); }

        Assert.Equal(SelecaoPagamento.Formas, vistas);
        p.Proxima();
        Assert.Equal(FormaPagamento.Dinheiro, p.Forma);   // deu a volta
        p.Anterior();
        Assert.Equal(FormaPagamento.APrazo, p.Forma);     // e voltou para a última
    }

    [Fact]
    public void Parcelas_so_no_credito_de_1_a_4()
    {
        var p = new SelecaoPagamento(1000);
        Assert.False(p.MaisParcelas());                    // dinheiro não parcela

        p.Proxima(); p.Proxima(); p.Proxima();             // crédito
        Assert.Equal(FormaPagamento.CartaoCredito, p.Forma);
        Assert.False(p.MenosParcelas());                   // já está em 1x
        Assert.True(p.MaisParcelas()); Assert.True(p.MaisParcelas()); Assert.True(p.MaisParcelas());
        Assert.False(p.MaisParcelas());                    // máximo 4x
        Assert.Equal(4, p.Parcelas);
        Assert.True(p.MenosParcelas());
        Assert.Equal(3, p.Parcelas);
    }

    [Fact]
    public void Trocar_de_forma_zera_as_parcelas()
    {
        var p = new SelecaoPagamento(1000);
        p.Proxima(); p.Proxima(); p.Proxima();
        p.MaisParcelas(); p.MaisParcelas();

        p.Proxima(); // cheque
        p.Anterior(); // volta ao crédito

        Assert.Equal(1, p.Parcelas);
    }

    [Fact]
    public void Dinheiro_calcula_troco_e_bloqueia_valor_menor_que_o_total()
    {
        var p = new SelecaoPagamento(8000) { ValorRecebidoTexto = "100" };
        Assert.Equal(2000, p.TrocoCentavos);
        Assert.Null(p.Validar());

        p.ValorRecebidoTexto = "79,99";
        Assert.Equal(0, p.TrocoCentavos);
        Assert.Contains("faltam", p.Validar());

        p.ValorRecebidoTexto = "";
        Assert.Contains("Informe", p.Validar());
        p.ValorRecebidoTexto = "abc";
        Assert.Contains("Informe", p.Validar());
    }

    [Fact]
    public void A_prazo_exige_cliente()
    {
        var p = new SelecaoPagamento(1000);
        p.Anterior(); // a prazo
        Assert.Equal(FormaPagamento.APrazo, p.Forma);
        Assert.Contains("cliente", p.Validar());

        p.ClienteId = 5;
        Assert.Null(p.Validar());
    }

    [Fact]
    public void Valor_recebido_menor_nao_atrapalha_pix_nem_cartao()
    {
        var p = new SelecaoPagamento(8000) { ValorRecebidoTexto = "1" };
        p.Proxima(); // pix
        Assert.Null(p.Validar());
    }

    [Fact]
    public async Task O_pedido_sai_com_forma_parcelas_cliente_e_desconto_certos_e_vira_venda()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var item = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 10000, 4000, 1), 5);
        var maria = await new ClienteService(banco.Fabrica).CadastrarAsync(new DadosCliente("Maria"));
        var carrinho = new Carrinho();
        carrinho.Adicionar((await produtos.BuscarAsync("Boné")).Single(),
            (await produtos.ListarTamanhosAsync(item.Id)).Single(), 1);
        var desconto = new SelecaoDesconto { Texto = "10" };
        desconto.Direita();
        var resolvido = desconto.Resolver(carrinho.SubtotalCentavos);
        var vendas = new VendaService(banco.Fabrica);

        // crédito em 3x
        var credito = new SelecaoPagamento(carrinho.SubtotalCentavos - resolvido.ValorEmCentavos);
        credito.Proxima(); credito.Proxima(); credito.Proxima();
        credito.MaisParcelas(); credito.MaisParcelas();
        var v1 = await vendas.FinalizarAsync(credito.ParaPedido(carrinho, resolvido));
        Assert.Equal((FormaPagamento.CartaoCredito, 3, 9810L), (v1.FormaPagamento, v1.Parcelas, v1.TotalCentavos));

        // a prazo com cliente
        var prazo = new SelecaoPagamento(9000) { ClienteId = maria.Id };
        prazo.Anterior();
        var v2 = await vendas.FinalizarAsync(prazo.ParaPedido(carrinho, resolvido));
        Assert.Equal((FormaPagamento.APrazo, maria.Id), (v2.FormaPagamento, v2.ClienteId));

        // dinheiro com troco
        var dinheiro = new SelecaoPagamento(9000) { ValorRecebidoTexto = "100" };
        var v3 = await vendas.FinalizarAsync(dinheiro.ParaPedido(carrinho, resolvido));
        Assert.Equal((100_00L, 10_00L), (v3.ValorRecebidoCentavos!.Value, v3.TrocoCentavos));
    }
}

public class SelecaoSucessoTests
{
    [Fact]
    public void Nasce_em_nova_venda_e_as_setas_alternam()
    {
        var s = new SelecaoSucesso();
        Assert.Equal(OpcaoSucesso.NovaVenda, s.Opcao);
        Assert.False(s.Direita());
        Assert.True(s.Esquerda());
        Assert.Equal(OpcaoSucesso.Reimprimir, s.Opcao);
        Assert.False(s.Esquerda());
        Assert.True(s.Direita());
        Assert.Equal(OpcaoSucesso.NovaVenda, s.Opcao);
    }
}

public class EscolhaNaBuscaTests
{
    private static async Task<(BancoDeTeste banco, ProdutoService produtos)> Preparar()
    {
        var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        await produtos.CadastrarAsync(new DadosProduto("Camiseta Azul", "789", 5000, 2000, 1));
        await produtos.CadastrarAsync(new DadosProduto("Camiseta Branca", "123", 5000, 2000, 1));
        await produtos.CadastrarAsync(new DadosProduto("Camisa 789 Polo", null, 8000, 3000, 1));
        return (banco, produtos);
    }

    [Fact]
    public async Task Item_destacado_com_as_setas_vale_mais_que_tudo()
    {
        var (banco, produtos) = await Preparar();
        using var _ = banco;
        var destacado = (await produtos.BuscarAsync("Branca")).Single();

        var escolhido = await produtos.EscolherParaVendaAsync("789", destacado);

        Assert.Equal("Camiseta Branca", escolhido!.Nome);
    }

    [Fact]
    public async Task Codigo_de_barras_exato_tem_prioridade_sobre_o_primeiro_resultado_do_nome()
    {
        var (banco, produtos) = await Preparar();
        using var _ = banco;

        // "789" aparece no nome "Camisa 789 Polo" (que vem antes por ordem alfabética?) e é o código da Camiseta Azul.
        var escolhido = await produtos.EscolherParaVendaAsync("789");

        Assert.Equal("Camiseta Azul", escolhido!.Nome);
    }

    [Fact]
    public async Task Sem_codigo_exato_vale_o_primeiro_resultado()
    {
        var (banco, produtos) = await Preparar();
        using var _ = banco;

        var escolhido = await produtos.EscolherParaVendaAsync("camiseta");

        Assert.Equal("Camiseta Azul", escolhido!.Nome); // ordem alfabética
    }

    [Fact]
    public async Task Nao_achou_ou_busca_vazia_devolve_nulo()
    {
        var (banco, produtos) = await Preparar();
        using var _ = banco;

        Assert.Null(await produtos.EscolherParaVendaAsync("zzz"));
        Assert.Null(await produtos.EscolherParaVendaAsync("   "));
    }

    [Fact]
    public async Task Produto_desativado_nao_e_escolhido()
    {
        var (banco, produtos) = await Preparar();
        using var _ = banco;
        var azul = (await produtos.BuscarAsync("Azul")).Single();
        await produtos.DesativarAsync(azul.Id);

        Assert.Null(await produtos.EscolherParaVendaAsync("789 Azul"));
        Assert.Equal("Camisa 789 Polo", (await produtos.EscolherParaVendaAsync("789"))!.Nome);
    }
}
