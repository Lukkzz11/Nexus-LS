using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class FinanceiroViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-fin-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _agora = new(2026, 10, 20, 10, 0, 0);
    private readonly FinanceiroService _servico;

    public FinanceiroViewModelTests()
    {
        Directory.CreateDirectory(_pasta);
        _servico = new FinanceiroService(_banco.Fabrica, () => _agora);
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private async Task<FinanceiroViewModel> CriarAsync()
    {
        var vm = new FinanceiroViewModel(_servico, () => _agora);
        await vm.Carregamento;
        return vm;
    }

    private async Task LancarAsync(FinanceiroViewModel vm, string descricao, string valor, DateTime vencimento, bool despesa = true, string parcelas = "1")
    {
        vm.NovoCommand.Execute(null);
        vm.FormEhDespesa = despesa;
        vm.FormDescricao = descricao;
        vm.FormValor = valor;
        vm.FormVencimento = vencimento;
        vm.FormParcelas = parcelas;
        await vm.SalvarCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Abre_no_mes_atual()
    {
        var vm = await CriarAsync();

        Assert.Equal(new DateTime(2026, 10, 1), vm.De);
        Assert.Equal(new DateTime(2026, 10, 31), vm.Ate);
    }

    [Fact]
    public async Task Salvar_cria_a_conta_limpa_o_formulario_e_atualiza_a_lista_e_o_resumo()
    {
        var vm = await CriarAsync();

        await LancarAsync(vm, "Aluguel", "1.500,00", new DateTime(2026, 10, 25));

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        var linha = Assert.Single(vm.Linhas);
        Assert.Equal(("Aluguel", "−R$ 1.500,00", "Em aberto"), (linha.Descricao, linha.ValorTexto.Replace(' ', ' '), linha.SituacaoTexto));
        Assert.Equal("", vm.FormDescricao);
        Assert.Contains("1.500,00", vm.APagarTexto.Replace(' ', ' '));
    }

    [Fact]
    public async Task Parcelado_gera_uma_linha_por_parcela()
    {
        var vm = await CriarAsync();

        await LancarAsync(vm, "Estoque", "300,00", new DateTime(2026, 10, 25), parcelas: "3");

        Assert.Contains("3 parcelas", vm.Mensagem);
        // só a primeira cai em outubro; as outras vencem em novembro e dezembro
        Assert.Single(vm.Linhas);
        vm.ProximosTrintaDiasCommand.Execute(null);
        await vm.Carregamento;
    }

    [Fact]
    public async Task Valor_ou_descricao_invalidos_mostram_o_erro_e_nao_gravam()
    {
        var vm = await CriarAsync();

        await LancarAsync(vm, "Luz", "abc", new DateTime(2026, 10, 25));
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("valor", vm.Mensagem, StringComparison.OrdinalIgnoreCase);

        await LancarAsync(vm, "", "10,00", new DateTime(2026, 10, 25));
        Assert.True(vm.MensagemEhErro);
        Assert.Empty(vm.Linhas);
    }

    [Fact]
    public async Task Pagar_muda_a_situacao_e_estornar_volta_a_em_aberto()
    {
        var vm = await CriarAsync();
        await LancarAsync(vm, "Luz", "200,00", new DateTime(2026, 10, 22));
        vm.Selecionada = vm.Linhas.Single();
        Assert.True(vm.PodePagar);
        Assert.False(vm.PodeEstornar);

        await vm.PagarCommand.ExecuteAsync(null);
        Assert.Equal("Pago", vm.Linhas.Single().SituacaoTexto);
        Assert.Contains("200,00", vm.DespesasTexto.Replace(' ', ' '));
        vm.Selecionada = vm.Linhas.Single();
        Assert.True(vm.PodeEstornar);
        Assert.False(vm.PodePagar);

        await vm.EstornarCommand.ExecuteAsync(null);
        Assert.Equal("Em aberto", vm.Linhas.Single().SituacaoTexto);
    }

    [Fact]
    public async Task Cancelar_pede_o_motivo_e_a_conta_continua_no_historico()
    {
        var vm = await CriarAsync();
        await LancarAsync(vm, "Luz", "200,00", new DateTime(2026, 10, 22));
        vm.Selecionada = vm.Linhas.Single();

        vm.PedirMotivo = _ => null;                                 // desistiu
        await vm.CancelarLancamentoCommand.ExecuteAsync(null);
        Assert.Equal("Em aberto", vm.Linhas.Single().SituacaoTexto);

        vm.PedirMotivo = _ => "lançada em duplicidade";
        await vm.CancelarLancamentoCommand.ExecuteAsync(null);
        Assert.Equal("Cancelado", vm.Linhas.Single().SituacaoTexto);
        Assert.False(vm.PodePagar);
    }

    [Fact]
    public async Task Conta_vencida_aparece_como_atrasada_e_no_resumo()
    {
        var vm = await CriarAsync();

        await LancarAsync(vm, "Velha", "100,00", new DateTime(2026, 10, 5));

        Assert.True(vm.Linhas.Single().Atrasado);
        Assert.True(vm.TemAtrasados);
        Assert.Contains("1 conta(s) atrasada(s)", vm.AtrasadosTexto);
    }

    [Fact]
    public async Task Editar_carrega_a_conta_no_formulario_e_salvar_atualiza_sem_duplicar()
    {
        var vm = await CriarAsync();
        await LancarAsync(vm, "Luz", "200,00", new DateTime(2026, 10, 22));
        vm.Selecionada = vm.Linhas.Single();

        vm.EditarSelecionadaCommand.Execute(null);
        Assert.True(vm.Editando);
        Assert.Equal("Luz", vm.FormDescricao);
        vm.FormValor = "250,00";
        await vm.SalvarCommand.ExecuteAsync(null);

        var linha = Assert.Single(vm.Linhas);
        Assert.Contains("250,00", linha.ValorTexto.Replace(' ', ' '));
        Assert.False(vm.Editando);
    }

    [Fact]
    public async Task Filtro_de_situacao_e_busca_reduzem_a_lista()
    {
        var vm = await CriarAsync();
        await LancarAsync(vm, "Aluguel", "1.000,00", new DateTime(2026, 10, 25));
        await LancarAsync(vm, "Conserto", "80,00", new DateTime(2026, 10, 26), despesa: false);

        vm.FiltroTipo = vm.FiltrosTipo.Single(f => f.Valor == TipoLancamento.Receita);
        await Task.Delay(200);
        Assert.Equal("Conserto", Assert.Single(vm.Linhas).Descricao);

        vm.FiltroTipo = vm.FiltrosTipo[0];
        vm.Busca = "alug";
        await Task.Delay(200);
        Assert.Equal("Aluguel", Assert.Single(vm.Linhas).Descricao);
    }

    [Fact]
    public async Task Exportar_grava_o_csv_com_as_contas_listadas()
    {
        var vm = await CriarAsync();
        await LancarAsync(vm, "Aluguel", "1.000,00", new DateTime(2026, 10, 25));
        var caminho = Path.Combine(_pasta, "fin.csv");
        vm.EscolherArquivoParaSalvar = _ => caminho;

        await vm.ExportarCommand.ExecuteAsync(null);

        var texto = File.ReadAllText(caminho);
        Assert.Contains("Aluguel", texto);
        Assert.Contains("A pagar", texto);
        Assert.Contains("1000,00", texto);
    }
}
