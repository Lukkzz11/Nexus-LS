using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class SecretariaTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 11, 10);
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private SecretariaService Secretaria() => new(_banco.Fabrica, () => Hoje.AddHours(8));
    private FinanceiroService Financeiro() => new(_banco.Fabrica, () => Hoje.AddHours(8));

    private async Task SemearAsync()
    {
        var f = Financeiro();
        await f.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Energia", 38000, Hoje.AddDays(-3)));
        await f.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel", 250000, Hoje.AddDays(2)));
        await f.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Uniformes da escola", 90000, Hoje.AddDays(5), Categoria: "Serviços"));
        await f.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Longe", 100, Hoje.AddDays(40)));
        var agenda = new AgendaService(_banco.Fabrica, () => Hoje);
        await agenda.CriarAsync(new DadosCompromisso("Buscar tecido", Hoje.AddHours(14)));
        await using var db = _banco.Fabrica.CreateDbContext();
        db.Clientes.Add(new Cliente { Nome = "Ana Paula", Telefone = "11999990000", DataNascimento = new DateTime(1990, 11, 12), Ativo = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Lista_traz_urgentes_primeiro_e_so_o_que_a_pessoa_pode_ver()
    {
        await SemearAsync();

        var tudo = await Secretaria().TarefasAsync(financeiro: true, clientes: true, "Loja X");
        Assert.True(tudo[0].Urgente);
        Assert.Contains("Energia", tudo[0].Titulo);
        Assert.Contains(tudo, t => t.Titulo.Contains("Aluguel"));
        Assert.DoesNotContain(tudo, t => t.Titulo.Contains("Longe"));
        var niver = Assert.Single(tudo, t => t.Assunto == AssuntoTarefa.Aniversario);
        Assert.Equal("11999990000", niver.Telefone);
        Assert.Contains("Ana!", niver.Mensagem);

        var balcao = await Secretaria().TarefasAsync(financeiro: false, clientes: false);
        Assert.Equal(AssuntoTarefa.Compromisso, Assert.Single(balcao).Assunto);
    }

    [Fact]
    public async Task Calendario_soma_o_saldo_dia_a_dia_contando_os_atrasados_a_partir_de_hoje()
    {
        await SemearAsync();

        var dias = await Secretaria().MesAsync(2026, 11, saldoDeHojeCentavos: 100000);

        Assert.Equal(30, dias.Count);
        Assert.True(dias[6].Atrasado);                                      // dia 7: energia
        Assert.Null(dias[6].SaldoPrevistoCentavos);                         // passado não tem previsão
        Assert.Equal(100000 - 38000, dias[9].SaldoPrevistoCentavos);        // hoje já desconta o atrasado
        Assert.Equal(100000 - 38000 - 250000, dias[11].SaldoPrevistoCentavos);
        Assert.Equal(100000 - 38000 - 250000 + 90000, dias[14].SaldoPrevistoCentavos);
    }

    private SecretariaViewModel Tela(bool financeiro = true) => new(Secretaria(), Financeiro(), () => Task.FromResult(100000L),
        () => Task.FromResult("Loja X"), financeiro, verClientes: true, () => Hoje);

    [Fact]
    public async Task Marcar_repetindo_cria_um_por_mes_com_o_mesmo_valor()
    {
        var vm = Tela();
        await vm.Carregamento;
        Assert.Equal("", vm.AlertaDoMes);
        vm.Descricao = "Internet";
        vm.Valor = "99,90";
        vm.RepetirMeses = "3";

        await vm.MarcarCommand.ExecuteAsync(null);

        var lancamentos = await Financeiro().ListarAsync(new FiltroLancamentos(Hoje, Hoje.AddMonths(6)));
        Assert.Equal(new[] { 9990L, 9990L, 9990L }, lancamentos.Select(l => l.ValorCentavos).ToArray());
        Assert.Equal(new[] { Hoje, Hoje.AddMonths(1), Hoje.AddMonths(2) }, lancamentos.Select(l => l.Vencimento).ToArray());
        Assert.Single(vm.ContasDoDia);
        Assert.Equal("", vm.AlertaDoMes);                    // 1000,00 em caixa cobrem a internet
        Assert.Equal("", vm.Descricao);
    }

    [Fact]
    public async Task Valor_invalido_nao_grava_e_sem_financeiro_nao_ha_calendario()
    {
        var vm = Tela();
        await vm.Carregamento;
        vm.Descricao = "X";
        vm.Valor = "abc";
        await vm.MarcarCommand.ExecuteAsync(null);
        Assert.Empty(await Financeiro().ListarAsync(new FiltroLancamentos(Hoje, Hoje.AddMonths(1))));

        var balcao = Tela(financeiro: false);
        await balcao.Carregamento;
        Assert.Empty(balcao.Dias);
    }

    /// <summary>Só gera a imagem quando NEXUSLS_PRINTS aponta uma pasta (conferência visual).</summary>
    [Fact]
    public async Task Gera_a_captura_da_tela()
    {
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(pasta)) return;
        Directory.CreateDirectory(pasta);
        await SemearAsync();
        var vm = Tela();
        await vm.Carregamento;

        Capturador.Rodar(() =>
        {
            var tela = new NexusLS.App.Views.SecretariaView { DataContext = vm, Margin = new Thickness(20) };
            var janela = new Window { Content = tela, Width = 1150, Height = 760, Left = -3000, Top = 0, ShowInTaskbar = false };
            janela.SetResourceReference(Window.BackgroundProperty, "CorFundo");
            janela.Show();
            janela.UpdateLayout();
            tela.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var alvo = (FrameworkElement)janela.Content;
            var bitmap = new RenderTargetBitmap((int)janela.ActualWidth, (int)janela.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(janela);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var arquivo = File.Create(Path.Combine(pasta, "secretaria.png"))) png.Save(arquivo);
            janela.Close();
        });
    }
}
