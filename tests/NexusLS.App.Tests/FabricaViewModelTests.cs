using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("OperadorAtual")]
public sealed class FabricaViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private FabricaViewModel Nova() =>
        new(new ProducaoService(_banco.Fabrica, new ProdutoService(_banco.Fabrica), () => new DateTime(2026, 11, 1, 9, 0, 0)));

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    [Fact]
    public void Grade_em_texto_aceita_varios_formatos_e_recusa_o_que_nao_entende()
    {
        Assert.Equal(new[] { "G:5", "M:20", "P:10" },
            FabricaViewModel.LerGrade("P:10, M:20; g = 5").OrderBy(g => g.Key).Select(g => $"{g.Key.ToUpper()}:{g.Value}").ToArray());
        Assert.Equal(15, FabricaViewModel.LerGrade("P:10, p:5").Single().Value);   // repetido soma
        Assert.Empty(FabricaViewModel.LerGrade("  "));
        Assert.Equal(7, FabricaViewModel.LerGrade("Sem tamanho: 7")["Sem tamanho"]);

        var ex = Assert.Throws<RegraDeNegocioException>(() => FabricaViewModel.LerGrade("P:10, M"));
        Assert.Contains("use tamanho:quantidade", ex.Message);
        Assert.Throws<RegraDeNegocioException>(() => FabricaViewModel.LerGrade("P:-3"));
    }

    [Fact]
    public async Task Criar_avancar_e_transferir_a_ordem_pela_tela_leva_as_pecas_para_o_estoque_da_loja()
    {
        var vm = Nova();
        await Esperar(() => vm.PdMotivo is not null);

        vm.OpModelo = "Camiseta Surf"; vm.OpReferencia = "CS-01"; vm.OpGrade = "P:10, M:20"; vm.OpCustoPeca = "25,00";
        await vm.CriarOrdemCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("OP-2026-0001", vm.Mensagem);
        var linha = Assert.Single(vm.Ordens);
        Assert.Equal("1. Planejamento", linha.Etapa);
        Assert.Equal(30, linha.Total);
        Assert.Equal("R$ 750,00", linha.CustoTotal);
        Assert.Equal("", vm.OpModelo);   // o formulário limpa

        vm.OrdemSelecionada = vm.Ordens[0];
        Assert.Equal("Camiseta Surf", vm.TrNome);  // a transferência já vem preenchida com a ordem
        await vm.AvancarEtapaCommand.ExecuteAsync(null);
        Assert.Equal("2. Corte", vm.Ordens[0].Etapa);

        vm.OrdemSelecionada = vm.Ordens[0];
        vm.TrPrecoVarejo = "89,90";
        await vm.TransferirParaLojaCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("30 peça(s)", vm.Mensagem);
        Assert.Equal("6. Concluída (na loja)", vm.Ordens[0].Etapa);

        await using var db = _banco.CriarContexto();
        var produto = await db.Produtos.SingleAsync();
        Assert.Equal(30, produto.EstoqueAtual);
        Assert.Equal(8990, produto.PrecoVendaCentavos);

        // segunda transferência da mesma ordem: recusada, estoque intacto
        vm.OrdemSelecionada = vm.Ordens[0];
        await vm.TransferirParaLojaCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("já foi transferida", vm.Mensagem);
        Assert.Equal(30, (await db.Produtos.AsNoTracking().SingleAsync()).EstoqueAtual);
    }

    [Fact]
    public async Task Sem_ordem_escolhida_ou_com_grade_mal_escrita_a_tela_avisa_e_nao_grava()
    {
        var vm = Nova();
        await Esperar(() => vm.PdMotivo is not null);

        await vm.AvancarEtapaCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Escolha uma ordem", vm.Mensagem);

        vm.OpModelo = "Boné"; vm.OpGrade = "tudo";
        await vm.CriarOrdemCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Empty(vm.Ordens);
    }

    [Fact]
    public async Task Materia_prima_cadastra_da_entrada_saida_e_o_alerta_de_minimo_aparece()
    {
        var vm = Nova();
        await Esperar(() => vm.PdMotivo is not null);

        vm.MpNome = "Malha PV"; vm.MpTipo = "Tecido / Malha"; vm.MpUnidade = "metros"; vm.MpMinimo = "50"; vm.MpCusto = "18,90"; vm.MpEstoqueInicial = "30,5";
        await vm.CadastrarMateriaCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Equal("30,5 metros", vm.Materias.Single().Estoque);
        Assert.Equal("Abaixo do mínimo", vm.Materias.Single().Situacao);
        Assert.Contains("Malha PV", vm.ResumoAlertas);

        vm.MateriaSelecionada = vm.Materias[0];
        vm.MpMovimento = "30";
        await vm.EntradaMateriaCommand.ExecuteAsync(null);
        Assert.Equal("60,5 metros", vm.Materias.Single().Estoque);
        Assert.Equal("", vm.ResumoAlertas);

        vm.MateriaSelecionada = vm.Materias[0];
        vm.MpMovimento = "100";
        await vm.SaidaMateriaCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);                              // não deixa o saldo ficar negativo
        Assert.Equal("60,5 metros", vm.Materias.Single().Estoque);
    }

    [Fact]
    public async Task Perda_pela_tela_baixa_a_materia_e_aparece_no_historico()
    {
        var vm = Nova();
        await Esperar(() => vm.PdMotivo is not null);
        vm.MpNome = "Malha PV"; vm.MpUnidade = "metros"; vm.MpCusto = "18,90"; vm.MpEstoqueInicial = "20";
        await vm.CadastrarMateriaCommand.ExecuteAsync(null);

        vm.PdMateria = vm.MateriasAtivas[0];
        vm.PdMotivo = vm.Motivos.Single(m => m.Valor == MotivoPerda.DefeitoNoTecido);
        vm.PdQuantidade = "2,5";
        await vm.RegistrarPerdaCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        var perda = Assert.Single(vm.Perdas);
        Assert.Equal("Defeito no tecido", perda.Motivo);
        Assert.Equal("2,5 metros", perda.Quantidade);
        Assert.Equal("R$ 47,25", perda.Custo);
        Assert.Equal("17,5 metros", vm.Materias.Single().Estoque);
    }

    [Fact]
    public async Task A_tela_abre_com_os_estilos_do_programa_e_mostra_as_ordens()
    {
        var servico = new ProducaoService(_banco.Fabrica, new ProdutoService(_banco.Fabrica), () => new DateTime(2026, 11, 1, 9, 0, 0));
        await servico.CriarOrdemAsync(new DadosOrdemProducao("Camiseta Surf", new Dictionary<string, int> { ["P"] = 10, ["M"] = 20, ["G"] = 5 }, "CS-01",
            PrevisaoTermino: new DateTime(2026, 11, 20), CustoEstimadoPecaCentavos: 2500));
        var segunda = await servico.CriarOrdemAsync(new DadosOrdemProducao("Bermuda Tactel", new Dictionary<string, int> { ["38"] = 8, ["40"] = 8 }));
        await servico.AvancarEtapaAsync(segunda.Id);
        await servico.CadastrarMateriaAsync(new DadosMateriaPrima("Malha PV", "Tecido", "metros", 50, 1890), 30);

        var vm = new FabricaViewModel(servico);
        await Esperar(() => vm.Ordens.Count == 2 && vm.Materias.Count == 1);
        vm.OrdemSelecionada = vm.Ordens[0];
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") ?? Path.GetTempPath();
        Directory.CreateDirectory(pasta);
        var png = Path.Combine(pasta, "fabrica.png");

        Capturador.SalvarFabrica(vm, png);

        Assert.True(new FileInfo(png).Length > 5000);
        if (Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") is null) File.Delete(png);
    }
}
