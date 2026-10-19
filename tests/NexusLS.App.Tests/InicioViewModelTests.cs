using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Backup;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>A tela Início: indicadores do dia e atalhos para os módulos.</summary>
public sealed class InicioViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly DateTime _agora = new(2026, 10, 19, 14, 30, 0);
    private readonly List<ItemMenu> _aberturas = new();

    public void Dispose() => _banco.Dispose();

    private async Task<InicioViewModel> CriarAsync(GestorBackup? backup = null)
    {
        var vm = new InicioViewModel(new InicioService(_banco.Fabrica, () => _agora), backup, _aberturas.Add, () => _agora);
        await vm.Carregamento; // garante que o carregamento terminou
        return vm;
    }

    private async Task<int> ProdutoAsync(string nome = "Boné", int estoque = 100, int minimo = 1) =>
        (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto(nome, null, 2000, 800, minimo), estoque)).Id;

    [Fact]
    public async Task Sem_vendas_mostra_zeros_e_textos_claros()
    {
        var vm = await CriarAsync();

        Assert.Equal("Segunda-feira, 19 de outubro de 2026", vm.DataTexto);
        Assert.Equal("R$ 0,00", vm.VendasHojeTexto.Replace(' ', ' '));
        Assert.Equal("Nenhuma venda hoje ainda", vm.VendasHojeDetalhe);
        Assert.Equal("Fechado", vm.CaixaTexto);
        Assert.False(vm.CaixaAberto);
        Assert.Equal("Tudo acima do mínimo", vm.EstoqueBaixoTexto);
        Assert.False(vm.EstoqueBaixoAlerta);
        Assert.Equal("Nenhuma dívida em aberto", vm.DividaTexto);
        Assert.False(vm.TemBackup);
    }

    [Fact]
    public async Task Mostra_vendas_do_dia_com_canceladas_a_parte()
    {
        var produto = await ProdutoAsync();
        var vendas = new VendaService(_banco.Fabrica, () => _agora);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produto, 2) }, 0, FormaPagamento.Pix)); // 40,00
        var cancelada = await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produto, 1) }, 0, FormaPagamento.Pix));
        await vendas.CancelarAsync(cancelada.Id, "Teste");

        var vm = await CriarAsync();

        Assert.Contains("40,00", vm.VendasHojeTexto);
        Assert.Equal("1 venda · 1 cancelada", vm.VendasHojeDetalhe);
        Assert.Contains("40,00", vm.TicketMedioTexto);
    }

    [Fact]
    public async Task Caixa_aberto_mostra_o_horario_de_abertura()
    {
        await new CaixaService(_banco.Fabrica, () => _agora).AbrirAsync(5000);

        var vm = await CriarAsync();

        Assert.True(vm.CaixaAberto);
        Assert.Equal("Aberto desde 14:30", vm.CaixaTexto);
    }

    [Fact]
    public async Task Estoque_baixo_vira_alerta_com_lista_dos_produtos()
    {
        await ProdutoAsync("Camisa", estoque: 1, minimo: 5);
        await ProdutoAsync("Meia", estoque: 50, minimo: 5);

        var vm = await CriarAsync();

        Assert.True(vm.EstoqueBaixoAlerta);
        Assert.Equal("1 produto abaixo do mínimo", vm.EstoqueBaixoTexto);
        Assert.Equal(new[] { "Camisa  ·  1 em estoque (mínimo 5)" }, vm.EstoquesBaixos);
    }

    [Fact]
    public async Task Divida_vencida_acende_o_alerta()
    {
        var produto = await ProdutoAsync();
        var maria = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Maria"));
        await new VendaService(_banco.Fabrica, () => _agora.AddDays(-60)).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(produto, 2) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id)); // 40,00 vencido

        var vm = await CriarAsync();

        Assert.True(vm.DividaAlerta);
        Assert.Contains("40,00", vm.DividaTexto);
        Assert.Equal("1 cliente · R$ 40,00 vencido (1 cliente)", vm.DividaDetalhe.Replace(' ', ' '));
    }

    [Fact]
    public async Task Atalhos_pedem_ao_menu_para_abrir_cada_tela()
    {
        var vm = await CriarAsync();

        vm.AbrirPdvCommand.Execute(null);
        vm.AbrirCaixaCommand.Execute(null);
        vm.AbrirFechamentoCommand.Execute(null);
        vm.AbrirEstoqueCommand.Execute(null);
        vm.AbrirClientesCommand.Execute(null);
        vm.AbrirRelatoriosCommand.Execute(null);
        vm.AbrirEtiquetasCommand.Execute(null);
        vm.AbrirMaisVendidosCommand.Execute(null);
        vm.AbrirConfiguracoesCommand.Execute(null);

        Assert.Equal(new[]
        {
            ItemMenu.Pdv, ItemMenu.Caixa, ItemMenu.Fechamento, ItemMenu.EstoqueDaLoja, ItemMenu.Clientes,
            ItemMenu.Relatorios, ItemMenu.Etiquetas, ItemMenu.MaisVendidos, ItemMenu.Configuracoes
        }, _aberturas);
    }

    [Fact]
    public async Task Backup_nunca_feito_alerta_e_depois_de_um_backup_fica_em_dia()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "nexusls-inicio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var arquivoBanco = Path.Combine(pasta, "nexus.db");
            using (var db = new PdvDbContext(new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={arquivoBanco}").Options))
                db.Database.Migrate();
            var store = new ConfiguracaoBackupStore(Path.Combine(pasta, "backup.json"));
            store.Salvar(new ConfiguracaoBackup { Pasta = Path.Combine(pasta, "bk"), IntervaloHoras = 24, Manter = 5 });
            var gestor = new GestorBackup(new BackupService(arquivoBanco), store, () => _agora);

            var antes = await CriarAsync(gestor);
            Assert.True(antes.TemBackup);
            Assert.True(antes.BackupAlerta);
            Assert.Equal("Nenhum backup feito ainda", antes.BackupTexto);

            await gestor.FazerAsync(MotivoBackup.Manual);
            var depois = await CriarAsync(gestor);
            Assert.False(depois.BackupAlerta);
            Assert.Equal("Backup em dia", depois.BackupTexto);
            Assert.Contains("19/10/2026 14:30", depois.BackupDetalhe);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(pasta, true); } catch (IOException) { }
        }
    }
}
