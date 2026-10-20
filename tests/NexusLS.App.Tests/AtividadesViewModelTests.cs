using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class AtividadesViewModelTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 10, 20);
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private async Task GravarAsync(TipoAtividade tipo, string descricao, string operador, DateTime quando)
    {
        await using var db = _banco.CriarContexto();
        AtividadeService.Registrar(db, tipo, descricao, null, operador, quando);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Abre_com_os_ultimos_sete_dias_e_mostra_a_mais_recente_primeiro()
    {
        await GravarAsync(TipoAtividade.VendaCancelada, "Cancelou a venda #1", "Ana", Hoje.AddDays(-2).AddHours(10));
        await GravarAsync(TipoAtividade.PedidoCancelado, "Cancelou o pedido X", "Bruno", Hoje.AddHours(9));
        await GravarAsync(TipoAtividade.VendaCancelada, "Antiga demais", "Ana", Hoje.AddDays(-30));

        var vm = new AtividadesViewModel(new AtividadeService(_banco.Fabrica), Hoje);
        await vm.Carregamento;

        Assert.Equal(new[] { "Cancelou o pedido X", "Cancelou a venda #1" }, vm.Linhas.Select(l => l.Descricao));
        Assert.Equal("2 atividade(s)", vm.Resumo);
        Assert.Equal("Bruno", vm.Linhas[0].Operador);
    }

    [Fact]
    public async Task Filtrar_por_tipo_e_data_final_antes_da_inicial_mostra_aviso()
    {
        await GravarAsync(TipoAtividade.VendaCancelada, "Cancelou a venda #1", "Ana", Hoje.AddHours(10));
        await GravarAsync(TipoAtividade.PedidoCancelado, "Cancelou o pedido X", "Ana", Hoje.AddHours(11));
        var vm = new AtividadesViewModel(new AtividadeService(_banco.Fabrica), Hoje);
        await vm.Carregamento;

        vm.TipoEscolhido = AtividadeService.NomeDoTipo(TipoAtividade.PedidoCancelado);
        await vm.CarregarCommand.ExecuteAsync(null);
        Assert.Single(vm.Linhas);
        Assert.Equal("Pedido cancelado", vm.Linhas[0].Tipo);

        vm.Ate = Hoje.AddDays(-20); // antes da data inicial
        await vm.CarregarCommand.ExecuteAsync(null);
        Assert.Contains("data final", vm.Mensagem);
    }

    [Fact]
    public async Task Sem_atividades_diz_que_nao_ha_nada()
    {
        var vm = new AtividadesViewModel(new AtividadeService(_banco.Fabrica), Hoje);
        await vm.Carregamento;

        Assert.Empty(vm.Linhas);
        Assert.Equal("Nenhuma atividade neste período.", vm.Resumo);
    }
}
