using System.IO;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Tests;

public class CalculadoraPrecosViewModelTests
{
    [Fact]
    public async Task A_tela_abre_com_os_estilos_do_programa_e_mostra_o_resultado()
    {
        var vm = new CalculadoraPrecosViewModel { CustoCompra = "60,00", ImpostosPct = "8", MargemPct = "30" };
        await vm.CalcularPrecoCommand.ExecuteAsync(null);
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") ?? Path.GetTempPath();
        Directory.CreateDirectory(pasta);
        var png = Path.Combine(pasta, "calculadora-precos.png");

        Capturador.SalvarCalculadora(vm, png); // falha aqui se o XAML tiver estilo inexistente ou erro de binding fatal

        Assert.True(new FileInfo(png).Length > 5000);
        if (Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") is null) File.Delete(png);
    }

    [Fact]
    public async Task Preco_pela_margem_mostra_preco_lucro_e_equilibrio()
    {
        var vm = new CalculadoraPrecosViewModel { CustoCompra = "60,00", MargemPct = "40" };

        await vm.CalcularPrecoCommand.ExecuteAsync(null);

        Assert.Contains("Preço de venda: R$ 100,00", vm.ResultadoPreco);
        Assert.Contains("Lucro estimado: R$ 40,00", vm.ResultadoPreco);
        Assert.Contains("Preço de equilíbrio", vm.ResultadoPreco);
        Assert.False(vm.MensagemEhErro);
    }

    [Fact]
    public async Task Frete_e_embalagem_entram_no_custo_e_percentuais_demais_recusam_o_calculo()
    {
        var vm = new CalculadoraPrecosViewModel { CustoCompra = "50", FreteReais = "7,50", EmbalagemReais = "2,50", MargemPct = "40" };
        await vm.CalcularPrecoCommand.ExecuteAsync(null);
        Assert.Contains("Custo de aquisição: R$ 60,00", vm.ResultadoPreco);
        Assert.Contains("Preço de venda: R$ 100,00", vm.ResultadoPreco);

        vm.ImpostosPct = "70";
        await vm.CalcularPrecoCommand.ExecuteAsync(null);
        Assert.Contains("nenhum preço cobre isso", vm.ResultadoPreco);
    }

    [Fact]
    public async Task Numero_invalido_vira_mensagem_na_tela_e_nao_derruba_o_programa()
    {
        var vm = new CalculadoraPrecosViewModel { CustoCompra = "abc", MargemPct = "40" };

        await vm.CalcularPrecoCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("\"abc\" não é um número válido", vm.Mensagem);
    }

    [Fact]
    public async Task Promocao_em_porcentagem_e_em_reais_dao_o_mesmo_preco_promocional()
    {
        var vm = new CalculadoraPrecosViewModel { CustoCompra = "60", PrecoOriginal = "100", Desconto = "10" };
        await vm.CalcularPromocaoCommand.ExecuteAsync(null);
        Assert.Contains("Preço promocional: R$ 90,00", vm.ResultadoPromocao);

        vm.DescontoEmReais = true;
        await vm.CalcularPromocaoCommand.ExecuteAsync(null);
        Assert.Contains("Preço promocional: R$ 90,00", vm.ResultadoPromocao);

        vm.Desconto = "50"; // em reais: preço de R$ 50 abaixo do custo de R$ 60
        await vm.CalcularPromocaoCommand.ExecuteAsync(null);
        Assert.Contains("prejuízo", vm.ResultadoPromocao);
    }

    [Fact]
    public async Task Pagamentos_leem_uma_forma_por_linha_e_avisam_da_taxa_nao_configurada()
    {
        var vm = new CalculadoraPrecosViewModel
        {
            CustoCompra = "60", PrecoPagamento = "100", FormasPagamento = "Pix; 1\nDébito;\nCrédito em 3x; 9"
        };

        await vm.CompararPagamentosCommand.ExecuteAsync(null);

        Assert.Contains("Pix: taxa 1% — taxa R$ 1,00, recebe R$ 99,00, lucro R$ 39,00", vm.ResultadoPagamentos);
        Assert.Contains("Débito: taxa não configurada", vm.ResultadoPagamentos);
        Assert.Contains("Crédito em 3x: taxa 9% — taxa R$ 9,00, recebe R$ 91,00", vm.ResultadoPagamentos);
    }

    [Fact]
    public async Task Quantidades_mostram_meta_e_equilibrio()
    {
        var vm = new CalculadoraPrecosViewModel
        {
            CustoCompra = "60", PrecoQuantidades = "100", Quantidades = "1, 10", MetaLucro = "400"
        };

        await vm.SimularQuantidadesCommand.ExecuteAsync(null);

        Assert.Contains("Lucro por peça: R$ 40,00", vm.ResultadoQuantidades);
        Assert.Contains("10 peça(s)", vm.ResultadoQuantidades);
        Assert.Contains("Para a meta de lucro: 10 peça(s).", vm.ResultadoQuantidades);
    }

    [Fact]
    public async Task Lote_rateia_o_frete_e_recusa_linha_mal_escrita()
    {
        var vm = new CalculadoraPrecosViewModel { ItensLote = "Camiseta; 10; 20\nBoné; 10; 20", DespesasLote = "Frete; 40; proporcional" };

        await vm.RatearLoteCommand.ExecuteAsync(null);

        Assert.Contains("Camiseta: 10 × R$ 20,00 + rateio R$ 2,00 = R$ 22,00 por peça (total R$ 220,00)", vm.ResultadoLote);
        Assert.Contains("Total do lote: R$ 440,00", vm.ResultadoLote);

        vm.ItensLote = "Camiseta; 10";
        await vm.RatearLoteCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("use nome; quantidade; custo unitário", vm.Mensagem);
    }
}
