using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class AgendaTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 11, 2);
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private AgendaService Servico() => new(_banco.Fabrica, () => Hoje.AddHours(8));

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    [Fact]
    public async Task Servico_cria_edita_conclui_remove_e_lista_o_dia_em_ordem()
    {
        var s = Servico();
        var tarde = await s.CriarAsync(new DadosCompromisso("Cobrar Ana", Hoje.AddHours(15)));
        await s.CriarAsync(new DadosCompromisso("Entrega", Hoje.AddHours(9), Hoje.AddHours(10)));
        await s.CriarAsync(new DadosCompromisso("Feriado local", Hoje.AddHours(11), DiaInteiro: true));
        await s.CriarAsync(new DadosCompromisso("Outro dia", Hoje.AddDays(1).AddHours(9)));

        var dia = await s.DoDiaAsync(Hoje);
        Assert.Equal(new[] { "Feriado local", "Entrega", "Cobrar Ana" }, dia.Select(c => c.Titulo).ToArray());
        Assert.Equal(Hoje, dia[0].Inicio);                 // dia inteiro vira 00:00

        await s.EditarAsync(tarde.Id, new DadosCompromisso("Cobrar Ana Paula", Hoje.AddHours(16)));
        await s.DefinirConcluidoAsync(tarde.Id, true);
        Assert.Equal("Cobrar Ana Paula", (await s.DoDiaAsync(Hoje)).Last().Titulo);
        Assert.DoesNotContain(await s.ProximosAsync(), c => c.Id == tarde.Id);      // concluído sai dos próximos
        Assert.Equal(new[] { Hoje, Hoje.AddDays(1) }, (await s.DiasComCompromissoAsync(2026, 11)).OrderBy(d => d).ToArray());

        await s.RemoverAsync(tarde.Id);
        Assert.Equal(2, (await s.DoDiaAsync(Hoje)).Count);
    }

    [Fact]
    public async Task Servico_recusa_titulo_vazio_fim_antes_do_inicio_e_id_inexistente()
    {
        var s = Servico();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.CriarAsync(new DadosCompromisso("  ", Hoje)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.CriarAsync(new DadosCompromisso("X", Hoje.AddHours(10), Hoje.AddHours(9))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.RemoverAsync(999));
    }

    [Fact]
    public async Task Tela_salva_edita_conclui_e_remove_com_confirmacao()
    {
        var vm = new AgendaViewModel(Servico(), () => Hoje);
        await Esperar(() => vm.TituloDoDia.Contains("(hoje)"));

        vm.Titulo = "Reunião com a malharia"; vm.Hora = "14:30"; vm.HoraFim = "15:30";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        var linha = Assert.Single(vm.Compromissos);
        Assert.Equal("14:30–15:30", linha.Quando);
        Assert.Equal("", vm.Titulo);                       // o formulário limpa

        vm.EditarCommand.Execute(linha);
        Assert.Equal("Editar compromisso", vm.FormularioTitulo);
        vm.Titulo = "Reunião com a Malharia Sul";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Equal("Reunião com a Malharia Sul", Assert.Single(vm.Compromissos).Titulo);

        await vm.AlternarConcluidoCommand.ExecuteAsync(vm.Compromissos[0]);
        Assert.True(vm.Compromissos[0].Concluido);

        vm.Confirmar = _ => false;
        await vm.RemoverCommand.ExecuteAsync(vm.Compromissos[0]);
        Assert.Single(vm.Compromissos);
        vm.Confirmar = _ => true;
        await vm.RemoverCommand.ExecuteAsync(vm.Compromissos[0]);
        Assert.Empty(vm.Compromissos);
    }

    [Fact]
    public async Task Hora_mal_escrita_avisa_e_mudar_de_dia_troca_a_lista()
    {
        var servico = Servico();
        await servico.CriarAsync(new DadosCompromisso("Amanhã cedo", Hoje.AddDays(1).AddHours(8)));
        var vm = new AgendaViewModel(servico, () => Hoje);
        await Esperar(() => vm.TituloDoDia.Contains("(hoje)"));
        Assert.Empty(vm.Compromissos);

        vm.Titulo = "Teste"; vm.Hora = "25:99";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Hora inválida", vm.Mensagem);

        vm.Dia = Hoje.AddDays(1);
        await Esperar(() => vm.Compromissos.Count == 1);
        Assert.Equal("Amanhã cedo", vm.Compromissos[0].Titulo);
        vm.HojeCommand.Execute(null);
        await Esperar(() => vm.Compromissos.Count == 0);
    }

    [Fact]
    public async Task As_telas_de_agenda_e_venda_cliente_abrem_com_os_estilos_do_programa()
    {
        var servico = Servico();
        await servico.CriarAsync(new DadosCompromisso("Entrega para a Ana", Hoje.AddHours(9), Hoje.AddHours(10), Descricao: "Levar a nota"));
        await servico.CriarAsync(new DadosCompromisso("Feriado", Hoje, DiaInteiro: true));
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") ?? System.IO.Path.GetTempPath();
        System.IO.Directory.CreateDirectory(pasta);

        var agenda = new AgendaViewModel(servico, () => Hoje);
        await Esperar(() => agenda.Compromissos.Count == 2 && agenda.Proximos.Count > 0);
        var png1 = System.IO.Path.Combine(pasta, "agenda.png");
        Capturador.SalvarAgenda(agenda, png1);
        Assert.True(new System.IO.FileInfo(png1).Length > 5000);

        var clientes = new ClienteService(_banco.Fabrica);
        await clientes.CadastrarAsync(new DadosCliente("Maria Atacado", Telefone: "81 99999-0000"));
        var escolha = new VendaClienteViewModel(clientes, _ => Task.CompletedTask);
        await Esperar(() => escolha.Clientes.Count == 1);
        var png2 = System.IO.Path.Combine(pasta, "venda-cliente.png");
        Capturador.SalvarVendaCliente(escolha, png2);
        Assert.True(new System.IO.FileInfo(png2).Length > 5000);

        if (Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") is null) { System.IO.File.Delete(png1); System.IO.File.Delete(png2); }
    }
}
