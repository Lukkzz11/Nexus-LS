using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class MontadorCupomTests
{
    private static ConfiguracaoLoja Loja(int largura = 42) => new()
    {
        NomeLoja = "Loja do Zé",
        Documento = "12.345.678/0001-90",
        Endereco = "Rua das Flores, 123 - Centro",
        Telefone = "(11) 3333-4444",
        MensagemCupom = "Obrigado pela preferência!",
        LarguraCupom = largura
    };

    private static Venda Venda(Action<Venda>? ajustar = null)
    {
        var v = new Venda
        {
            Id = 7,
            DataHora = new DateTime(2026, 10, 18, 14, 35, 0),
            FormaPagamento = FormaPagamento.Dinheiro,
            SubtotalCentavos = 11980,
            TotalCentavos = 11980,
            ValorRecebidoCentavos = 15000,
            TrocoCentavos = 3020,
            Itens =
            {
                new ItemVenda { NomeProduto = "Camiseta", Tamanho = "M", PrecoUnitarioCentavos = 5990, Quantidade = 2, TotalCentavos = 11980 }
            }
        };
        ajustar?.Invoke(v);
        return v;
    }

    private static string[] Linhas(string cupom) => cupom.Split('\n');

    [Fact]
    public void Traz_loja_venda_item_total_pagamento_e_rodape()
    {
        var cupom = MontadorCupom.Montar(Venda(), Loja());

        Assert.Contains("LOJA DO ZÉ", cupom);
        Assert.Contains("CNPJ/CPF: 12.345.678/0001-90", cupom);
        Assert.Contains("Rua das Flores, 123 - Centro", cupom);
        Assert.Contains("WhatsApp / Tel: (11) 3333-4444", cupom);
        Assert.Contains("CUPOM NÃO FISCAL", cupom);
        Assert.Contains("Venda / Pedido: #7", cupom);
        Assert.Contains("18/10/2026 14:35", cupom);
        Assert.Contains("Camiseta (M)", cupom);
        Assert.Contains("2x R$ 59,90", cupom);
        Assert.Contains("R$ 119,80", cupom);
        Assert.Contains("TOTAL", cupom);
        Assert.Contains("Obrigado pela preferência!", cupom);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(42)]
    [InlineData(48)]
    public void Nenhuma_linha_passa_da_largura_configurada(int largura)
    {
        var venda = Venda(v => v.Itens.Add(new ItemVenda
        {
            NomeProduto = "Camiseta Esportiva Dry Fit Manga Longa com Proteção Solar UV50",
            Tamanho = "GG", PrecoUnitarioCentavos = 12990, Quantidade = 12, TotalCentavos = 155880
        }));

        foreach (var linha in Linhas(MontadorCupom.Montar(venda, Loja(largura))))
            Assert.True(linha.Length <= largura, $"Linha com {linha.Length} caracteres (limite {largura}): \"{linha}\"");
    }

    [Fact]
    public void Nome_comprido_quebra_em_varias_linhas_sem_perder_texto()
    {
        var venda = Venda(v => v.Itens[0] = new ItemVenda
        {
            NomeProduto = "Camiseta Esportiva Dry Fit Manga Longa", Tamanho = "M",
            PrecoUnitarioCentavos = 100, Quantidade = 1, TotalCentavos = 100
        });

        var cupom = MontadorCupom.Montar(venda, Loja(24));

        foreach (var palavra in new[] { "Camiseta", "Esportiva", "Dry", "Fit", "Manga", "Longa", "(M)" })
            Assert.Contains(palavra, cupom);
    }

    [Fact]
    public void Palavra_maior_que_a_linha_e_cortada()
    {
        var venda = Venda(v => v.Itens[0].NomeProduto = new string('X', 60));
        foreach (var linha in Linhas(MontadorCupom.Montar(venda, Loja(32))))
            Assert.True(linha.Length <= 32);
    }

    [Fact]
    public void Sem_tamanho_nao_mostra_parenteses()
    {
        var venda = Venda(v => v.Itens[0].Tamanho = Tamanhos.SemTamanho);
        var cupom = MontadorCupom.Montar(venda, Loja());
        Assert.DoesNotContain("Sem tamanho", cupom);
        Assert.DoesNotContain("(", cupom.Split('\n').First(l => l.StartsWith("Camiseta")));
    }

    [Fact]
    public void Desconto_so_aparece_quando_existe()
    {
        Assert.DoesNotContain("Desconto", MontadorCupom.Montar(Venda(), Loja()));

        var comDesconto = Venda(v => { v.DescontoCentavos = 1000; v.TotalCentavos = 10980; v.TrocoCentavos = 4020; });
        var cupom = MontadorCupom.Montar(comDesconto, Loja());
        Assert.Contains("Desconto", cupom);
        Assert.Contains("- R$ 10,00", cupom);
        Assert.Contains("R$ 109,80", cupom);
    }

    [Fact]
    public void Dinheiro_mostra_recebido_e_troco_e_as_outras_formas_nao()
    {
        var dinheiro = MontadorCupom.Montar(Venda(), Loja());
        Assert.Contains("Recebido", dinheiro);
        Assert.Contains("R$ 150,00", dinheiro);
        Assert.Contains("TROCO", dinheiro);
        Assert.Contains("R$ 30,20", dinheiro);

        var pix = MontadorCupom.Montar(Venda(v => { v.FormaPagamento = FormaPagamento.Pix; v.ValorRecebidoCentavos = null; v.TrocoCentavos = 0; }), Loja());
        Assert.Contains("PIX", pix);
        Assert.DoesNotContain("Recebido", pix);
        Assert.DoesNotContain("TROCO", pix);
    }

    [Fact]
    public void Credito_parcelado_mostra_as_parcelas_e_a_soma_fecha_no_total()
    {
        var venda = Venda(v =>
        {
            v.FormaPagamento = FormaPagamento.CartaoCredito; v.Parcelas = 3;
            v.SubtotalCentavos = v.TotalCentavos = 10000; v.ValorRecebidoCentavos = null; v.TrocoCentavos = 0;
        });

        var cupom = MontadorCupom.Montar(venda, Loja());

        Assert.Contains("CARTÃO CRÉDITO 3x", cupom);
        Assert.Contains("3x de", cupom);
        Assert.Contains("R$ 33,34", cupom); // 100,00 / 3: a primeira parcela leva a sobra do centavo
    }

    [Fact]
    public void A_prazo_mostra_o_cliente()
    {
        var venda = Venda(v =>
        {
            v.FormaPagamento = FormaPagamento.APrazo; v.ValorRecebidoCentavos = null; v.TrocoCentavos = 0;
            v.Cliente = new Cliente { Nome = "Maria Souza" };
        });

        var cupom = MontadorCupom.Montar(venda, Loja());

        Assert.Contains("Cliente: Maria Souza", cupom);
        Assert.Contains("A PRAZO", cupom);
    }

    [Fact]
    public void Venda_cancelada_e_atacado_aparecem_marcados()
    {
        var cupom = MontadorCupom.Montar(Venda(v => { v.Cancelada = true; v.TabelaPreco = TabelaPreco.Atacado; }), Loja());

        Assert.Contains("*** VENDA CANCELADA ***", cupom);
        Assert.Contains("Tabela: Atacado", cupom);
    }

    [Fact]
    public void Loja_so_com_nome_nao_deixa_linhas_vazias_de_dados()
    {
        var cupom = MontadorCupom.Montar(Venda(), new ConfiguracaoLoja { NomeLoja = "Minha Loja" });

        Assert.DoesNotContain("CNPJ", cupom);
        Assert.DoesNotContain("Tel:", cupom);
        Assert.Contains("MINHA LOJA", Linhas(cupom)[0]);
        Assert.Contains("CUPOM NÃO FISCAL", Linhas(cupom)[1]); // logo abaixo do nome vem o selo, sem linhas em branco
        Assert.StartsWith("-", Linhas(cupom)[2]);
    }

    [Fact]
    public void O_nome_da_loja_fica_centralizado()
    {
        var primeira = Linhas(MontadorCupom.Montar(Venda(), new ConfiguracaoLoja { NomeLoja = "ABCD", LarguraCupom = 32 }))[0];
        Assert.Equal(new string(' ', 14) + "ABCD", primeira);
    }

    [Fact]
    public void Largura_fora_do_limite_e_ajustada_em_vez_de_quebrar()
    {
        foreach (var largura in new[] { 5, 500 })
        {
            var cupom = MontadorCupom.Montar(Venda(), Loja(largura));
            Assert.All(Linhas(cupom), l => Assert.True(l.Length <= ConfiguracaoService.LarguraMaxima));
        }
    }

    [Fact]
    public void Nome_da_loja_sai_marcado_como_maior_e_a_caixa_usa_traco_continuo()
    {
        var loja = Loja();
        loja.CupomTituloEmCaixa = true;

        var linhas = MontadorCupom.Estilizar(MontadorCupom.Montar(Venda(), loja));

        Assert.Contains(linhas[0], s => s.Grande && s.Texto.Contains("LOJA DO ZÉ")); // a primeira linha é o nome da loja
        Assert.Contains(linhas, l => l.Any(s => s.Texto.Contains("│ CUPOM")));
        Assert.Contains(linhas, l => l.Any(s => s.Texto.Contains("┌")));
        Assert.All(linhas, l => Assert.False(l.Any(s => s.Texto.Contains("+--"))));
    }
}

public class CupomServiceTests
{
    private sealed class ImpressoraDeMentira : IImpressoraCupom
    {
        public List<(string texto, ConfiguracaoLoja loja)> Impressos { get; } = new();
        public Exception? Falha { get; set; }

        public Task ImprimirAsync(string texto, ConfiguracaoLoja loja)
        {
            if (Falha is not null) throw Falha;
            Impressos.Add((texto, loja));
            return Task.CompletedTask;
        }
    }

    private static async Task<(BancoDeTeste banco, CupomService cupons, ConfiguracaoService config, ImpressoraDeMentira impressora, int vendaId)> PrepararAsync()
    {
        var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var produto = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 10);
        var clientes = new ClienteService(banco.Fabrica);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria"));
        var venda = await new VendaService(banco.Fabrica).FinalizarAsync(new PedidoVenda(
            new[] { new ItemCarrinho(produto.Id, 2) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));

        var config = new ConfiguracaoService(banco.Fabrica);
        await config.SalvarAsync(new DadosLoja("Loja Teste", MensagemCupom: "Volte sempre!"));
        var impressora = new ImpressoraDeMentira();
        return (banco, new CupomService(banco.Fabrica, config, impressora), config, impressora, venda.Id);
    }

    [Fact]
    public async Task Gera_o_texto_da_venda_com_itens_cliente_e_dados_da_loja()
    {
        var (banco, cupons, _, _, vendaId) = await PrepararAsync();
        using var _ = banco;

        var texto = await cupons.GerarTextoAsync(vendaId);

        Assert.Contains("LOJA TESTE", texto);
        Assert.Contains("Boné", texto);
        Assert.Contains("Cliente: Maria", texto);
        Assert.Contains("Volte sempre!", texto);
    }

    [Fact]
    public async Task Imprimir_manda_o_texto_e_a_loja_para_a_impressora()
    {
        var (banco, cupons, _, impressora, vendaId) = await PrepararAsync();
        using var _ = banco;

        await cupons.ImprimirAsync(vendaId);

        var impresso = Assert.Single(impressora.Impressos);
        Assert.Contains($"Venda / Pedido: #{vendaId}", impresso.texto);
        Assert.Equal("Loja Teste", impresso.loja.NomeLoja);
    }

    [Fact]
    public async Task Ao_finalizar_imprime_so_se_a_impressao_automatica_estiver_ligada()
    {
        var (banco, cupons, config, impressora, vendaId) = await PrepararAsync();
        using var _ = banco;

        Assert.True(await cupons.ImprimirAoFinalizarAsync(vendaId));     // padrão: ligada
        Assert.Single(impressora.Impressos);

        await config.SalvarAsync(new DadosLoja("Loja Teste", ImprimirCupomAoFinalizar: false));
        Assert.False(await cupons.ImprimirAoFinalizarAsync(vendaId));
        Assert.Single(impressora.Impressos);                              // nada novo

        await cupons.ImprimirAsync(vendaId);                              // reimprimir funciona mesmo desligada
        Assert.Equal(2, impressora.Impressos.Count);
    }

    [Fact]
    public async Task Venda_inexistente_e_falha_da_impressora_viram_erros_claros()
    {
        var (banco, cupons, _, impressora, vendaId) = await PrepararAsync();
        using var _ = banco;

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => cupons.ImprimirAsync(9999));

        impressora.Falha = new InvalidOperationException("Impressora offline");
        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => cupons.ImprimirAsync(vendaId));
        Assert.Equal("Impressora offline", erro.Message);
    }

    [Fact]
    public async Task Cupom_de_teste_usa_os_dados_do_formulario_sem_salvar()
    {
        var (banco, cupons, config, impressora, _) = await PrepararAsync();
        using var _ = banco;

        await cupons.ImprimirTesteAsync(new ConfiguracaoLoja { NomeLoja = "Outro Nome", LarguraCupom = 32 });

        var impresso = Assert.Single(impressora.Impressos);
        Assert.Contains("OUTRO NOME", impresso.texto);
        Assert.Equal("Loja Teste", (await config.ObterAsync()).NomeLoja); // o salvo não mudou
    }
}

public class ConfiguracaoCupomTests
{
    [Fact]
    public async Task Salva_impressora_largura_e_impressao_automatica()
    {
        using var banco = new BancoDeTeste();
        var servico = new ConfiguracaoService(banco.Fabrica);

        await servico.SalvarAsync(new DadosLoja("Loja", ImpressoraCupom: "  EPSON TM-T20  ", LarguraCupom: 48, ImprimirCupomAoFinalizar: false));

        var config = await servico.ObterAsync();
        Assert.Equal("EPSON TM-T20", config.ImpressoraCupom);
        Assert.Equal(48, config.LarguraCupom);
        Assert.False(config.ImprimirCupomAoFinalizar);
    }

    [Fact]
    public async Task Padroes_de_quem_nunca_configurou()
    {
        using var banco = new BancoDeTeste();
        var config = await new ConfiguracaoService(banco.Fabrica).ObterAsync();

        Assert.Null(config.ImpressoraCupom);       // impressora padrão do Windows
        Assert.Equal(42, config.LarguraCupom);
        Assert.True(config.ImprimirCupomAoFinalizar);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(81)]
    public async Task Largura_fora_de_24_a_80_e_recusada(int largura)
    {
        using var banco = new BancoDeTeste();
        await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => new ConfiguracaoService(banco.Fabrica).SalvarAsync(new DadosLoja("Loja", LarguraCupom: largura)));
    }
}
