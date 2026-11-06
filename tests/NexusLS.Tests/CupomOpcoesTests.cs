using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>As opções do cupom que vieram do ERP PDV Life (e a ordem das linhas, igual à dele).</summary>
public sealed class CupomOpcoesTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static ConfiguracaoLoja Loja(Action<ConfiguracaoLoja>? ajustar = null)
    {
        var l = new ConfiguracaoLoja
        {
            NomeLoja = "Casa Nexus", RazaoSocial = "Casa Nexus Confecções LTDA", Endereco = "Rua das Flores, 123 - Centro",
            CidadeUf = "Caruaru - PE", Documento = "12.345.678/0001-90", InscricaoEstadual = "123456789", Telefone = "(81) 99999-1234",
            Email = "contato@casanexus.com.br", CupomMensagemCabecalho = "Moda praia e fitness", MensagemCupom = "Obrigado pela preferência!",
            ChavePix = "pix@casa.com", CupomMostrarPix = true, CupomMostrarEmail = true, CupomMostrarAssinatura = true, LarguraCupom = 42
        };
        ajustar?.Invoke(l);
        return l;
    }

    private static Venda Venda() => new()
    {
        Id = 9, DataHora = new DateTime(2026, 10, 20, 14, 35, 0), FormaPagamento = FormaPagamento.Dinheiro, Operador = "Ana",
        Cliente = new Cliente { Nome = "Maria Souza" },
        SubtotalCentavos = 11980, DescontoCentavos = 1000, TotalCentavos = 10980, ValorRecebidoCentavos = 15000, TrocoCentavos = 4020,
        Itens = { new ItemVenda { NomeProduto = "Camiseta", Tamanho = "M", PrecoUnitarioCentavos = 5990, Quantidade = 2, TotalCentavos = 11980 } }
    };

    private static string[] Linhas(string cupom) => cupom.Split('\n');

    private sealed class ImpressoraDeMentira : IImpressoraCupom
    {
        public List<string> Impressos { get; } = new();
        public Task ImprimirAsync(string texto, ConfiguracaoLoja loja) { Impressos.Add(texto); return Task.CompletedTask; }
    }

    [Fact]
    public void As_linhas_seguem_a_ordem_do_cupom_do_erp()
    {
        var linhas = Linhas(MontadorCupom.Montar(Venda(), Loja()));
        int Posicao(string trecho) { var i = Array.FindIndex(linhas, l => l.Contains(trecho)); Assert.True(i >= 0, $"faltou: {trecho}"); return i; }

        var ordem = new[]
        {
            "Casa Nexus", "Confecções LTDA", "Rua das Flores", "Caruaru - PE", "CNPJ/CPF:", "IE: 123456789", "WhatsApp / Tel:", "contato@casanexus",
            "Moda praia", "CUPOM NÃO FISCAL", "Venda nº 9", "Data/Hora: 20/10/2026 14:35", "Operador: Ana", "Cliente: Maria Souza",
            "ITENS DA VENDA", "Camiseta (M)", "Subtotal:", "Desconto:", "TOTAL A PAGAR:", "Pagamento:", "Recebido:", "TROCO:",
            "Obrigado pela preferência!", "Pix: pix@casa.com", "Impresso pelo Nexus LS"
        };
        var posicoes = ordem.Select(Posicao).ToList();
        Assert.Equal(posicoes.OrderBy(p => p), posicoes);
        Assert.Equal(posicoes.Distinct().Count(), posicoes.Count); // cada coisa na sua linha
    }

    [Theory]
    [InlineData("Confecções LTDA", nameof(ConfiguracaoLoja.CupomMostrarRazaoSocial))]
    [InlineData("Rua das Flores", nameof(ConfiguracaoLoja.CupomMostrarEndereco))]
    [InlineData("Caruaru - PE", nameof(ConfiguracaoLoja.CupomMostrarCidade))]
    [InlineData("CNPJ/CPF:", nameof(ConfiguracaoLoja.CupomMostrarDocumento))]
    [InlineData("IE: 123456789", nameof(ConfiguracaoLoja.CupomMostrarInscricao))]
    [InlineData("WhatsApp / Tel:", nameof(ConfiguracaoLoja.CupomMostrarTelefone))]
    [InlineData("contato@casanexus", nameof(ConfiguracaoLoja.CupomMostrarEmail))]
    [InlineData("Operador: Ana", nameof(ConfiguracaoLoja.CupomMostrarOperador))]
    [InlineData("Cliente: Maria Souza", nameof(ConfiguracaoLoja.CupomMostrarCliente))]
    [InlineData("Pagamento:", nameof(ConfiguracaoLoja.CupomMostrarPagamento))]
    [InlineData("TROCO:", nameof(ConfiguracaoLoja.CupomMostrarPagamento))]
    [InlineData("Pix: pix@casa.com", nameof(ConfiguracaoLoja.CupomMostrarPix))]
    [InlineData("Impresso pelo Nexus LS", nameof(ConfiguracaoLoja.CupomMostrarAssinatura))]
    public void Cada_opcao_liga_e_desliga_a_sua_linha(string trecho, string opcao)
    {
        var propriedade = typeof(ConfiguracaoLoja).GetProperty(opcao)!;

        Assert.Contains(trecho, MontadorCupom.Montar(Venda(), Loja(l => propriedade.SetValue(l, true))));
        Assert.DoesNotContain(trecho, MontadorCupom.Montar(Venda(), Loja(l => propriedade.SetValue(l, false))));
    }

    [Fact]
    public void Desligar_pagamento_tira_a_forma_o_recebido_e_o_troco_mas_deixa_o_total()
    {
        var cupom = MontadorCupom.Montar(Venda(), Loja(l => l.CupomMostrarPagamento = false));

        Assert.DoesNotContain("Pagamento:", cupom);
        Assert.DoesNotContain("Recebido:", cupom);
        Assert.Contains("TOTAL A PAGAR:", cupom);
    }

    [Fact]
    public void Mensagens_de_varias_linhas_saem_uma_por_linha_centralizadas_e_cabem_na_largura()
    {
        var cupom = MontadorCupom.Montar(Venda(), Loja(l =>
        {
            l.LarguraCupom = 32;
            l.CupomMensagemCabecalho = "Moda praia\ne fitness";
            l.MensagemCupom = "Trocas em até 7 dias\ncom a etiqueta.";
        }));

        var linhas = Linhas(cupom);
        Assert.Contains(linhas, l => l.Trim() == "Moda praia");
        Assert.Contains(linhas, l => l.Trim() == "e fitness");
        Assert.Contains(linhas, l => l.Trim() == "Trocas em até 7 dias");
        Assert.Contains(linhas, l => l.Trim() == "com a etiqueta.");
        Assert.All(linhas, l => Assert.True(l.Length <= 32, $"passou da largura: \"{l}\""));
    }

    [Fact]
    public void Sem_dado_cadastrado_a_linha_nao_aparece_mesmo_com_a_opcao_ligada()
    {
        var cupom = MontadorCupom.Montar(Venda(), new ConfiguracaoLoja { NomeLoja = "Loja" });

        Assert.DoesNotContain("IE:", cupom);
        Assert.DoesNotContain("WhatsApp", cupom);
        Assert.DoesNotContain("@", cupom);
        Assert.DoesNotContain("Impresso pelo", cupom); // assinatura vem desligada de fábrica
        Assert.DoesNotContain(Linhas(cupom).Take(2), l => l.Trim() == "");
    }

    [Fact]
    public void Cupom_antigo_sem_as_opcoes_novas_continua_com_o_mesmo_conteudo_de_antes()
    {
        var padrao = new ConfiguracaoLoja();
        Assert.True(padrao.CupomMostrarCidade && padrao.CupomMostrarInscricao && padrao.CupomMostrarOperador
                    && padrao.CupomMostrarCliente && padrao.CupomMostrarPagamento);
        Assert.False(padrao.CupomMostrarEmail || padrao.CupomMostrarAssinatura);
        Assert.Null(padrao.CupomMensagemCabecalho);
    }

    [Fact]
    public async Task As_opcoes_novas_sao_salvas_e_lidas_de_volta()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);

        await config.SalvarAsync(new DadosLoja("Casa Nexus", CupomMostrarCidade: false, CupomMostrarInscricao: false, CupomMostrarEmail: true,
            CupomMostrarOperador: false, CupomMostrarCliente: false, CupomMostrarPagamento: false, CupomMostrarAssinatura: true,
            CupomMensagemCabecalho: "  Moda praia  "));
        var salvo = await config.ObterAsync();

        Assert.False(salvo.CupomMostrarCidade);
        Assert.False(salvo.CupomMostrarInscricao);
        Assert.True(salvo.CupomMostrarEmail);
        Assert.False(salvo.CupomMostrarOperador);
        Assert.False(salvo.CupomMostrarCliente);
        Assert.False(salvo.CupomMostrarPagamento);
        Assert.True(salvo.CupomMostrarAssinatura);
        Assert.Equal("Moda praia", salvo.CupomMensagemCabecalho);
    }

    [Fact]
    public async Task Mensagem_do_cabecalho_acima_de_200_letras_e_recusada()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            config.SalvarAsync(new DadosLoja("Casa", CupomMensagemCabecalho: new string('x', 201))));

        Assert.Contains("200", erro.Message);
        await config.SalvarAsync(new DadosLoja("Casa", CupomMensagemCabecalho: new string('x', 200))); // no limite passa
    }

    [Fact]
    public async Task O_cupom_de_teste_impresso_e_a_previa_da_tela_usam_o_mesmo_texto()
    {
        var impressora = new ImpressoraDeMentira();
        var config = new ConfiguracaoService(_banco.Fabrica);
        var cupons = new CupomService(_banco.Fabrica, config, impressora);
        var loja = Loja(l => l.NomeOperador = "Ana");

        await cupons.ImprimirTesteAsync(loja);

        var exemplo = MontadorCupom.VendaDeExemplo();
        exemplo.Operador = "Ana";
        Assert.Equal(MontadorCupom.Montar(exemplo, loja).Replace(exemplo.DataHora.ToString("dd/MM/yyyy HH:mm"), "#"),
                     impressora.Impressos.Single().Replace(exemplo.DataHora.ToString("dd/MM/yyyy HH:mm"), "#"));
        Assert.Contains("Operador: Ana", impressora.Impressos.Single());
        Assert.Contains("Cliente: Consumidor Final", impressora.Impressos.Single());
    }
}
