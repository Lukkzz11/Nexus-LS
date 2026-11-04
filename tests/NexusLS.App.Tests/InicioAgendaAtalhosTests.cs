using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Início: saudação, agenda de hoje e atalhos conforme a permissão.</summary>
[Collection("Sessao")]
public sealed class InicioAgendaAtalhosTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose()
    {
        Sessao.Sair();
        Sessao.DefinirLoginAtivo(false);
        _banco.Dispose();
    }

    private async Task<InicioViewModel> CriarAsync(DateTime agora, bool comAgenda = true)
    {
        var agenda = new AgendaService(_banco.Fabrica, () => agora);
        var dia = agora.Date;
        await agenda.CriarAsync(new DadosCompromisso("Reunião", dia.AddHours(16)));
        await agenda.CriarAsync(new DadosCompromisso("Feriado", dia, DiaInteiro: true));
        var feito = await agenda.CriarAsync(new DadosCompromisso("Já feito", dia.AddHours(9)));
        await agenda.DefinirConcluidoAsync(feito.Id, true);
        await agenda.CriarAsync(new DadosCompromisso("Amanhã", dia.AddDays(1).AddHours(9)));

        var vm = new InicioViewModel(new InicioService(_banco.Fabrica, () => agora), null, _ => { }, () => agora, comAgenda ? agenda : null);
        await vm.Carregamento;
        return vm;
    }

    [Theory]
    [InlineData(8, "Bom dia")]
    [InlineData(14, "Boa tarde")]
    [InlineData(21, "Boa noite")]
    public async Task Saudacao_muda_com_a_hora(int hora, string esperado)
    {
        var vm = await CriarAsync(new DateTime(2026, 10, 19, hora, 0, 0));
        Assert.Equal(esperado, vm.Saudacao);
    }

    [Fact]
    public async Task Agenda_de_hoje_mostra_so_o_que_falta_fazer_em_ordem()
    {
        var vm = await CriarAsync(new DateTime(2026, 10, 19, 14, 30, 0));
        Assert.True(vm.TemAgenda);
        Assert.False(vm.AgendaVazia);
        Assert.Equal(2, vm.AgendaHoje.Count);
        Assert.StartsWith("Dia inteiro", vm.AgendaHoje[0]);
        Assert.Contains("16:00", vm.AgendaHoje[1]);
        Assert.DoesNotContain(vm.AgendaHoje, t => t.Contains("Já feito") || t.Contains("Amanhã"));
    }

    [Fact]
    public async Task Sem_agenda_o_cartao_some()
    {
        var vm = await CriarAsync(new DateTime(2026, 10, 19, 14, 30, 0), comAgenda: false);
        Assert.False(vm.TemAgenda);
        Assert.Empty(vm.AgendaHoje);
    }

    [Fact]
    public async Task Atalhos_so_mostram_o_que_a_pessoa_pode_abrir()
    {
        var servico = new FuncionarioService(_banco.Fabrica);
        var (admin, _) = await servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var caixa = await servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.OperadorDeCaixa), "4321");
        Sessao.DefinirLoginAtivo(true);

        Sessao.Entrar(admin);
        var tudo = (await CriarAsync(new DateTime(2026, 10, 19, 10, 0, 0))).Atalhos.Select(a => a.Nome).ToList();
        Assert.Contains("Configurações", tudo);
        Assert.Contains("PDV", tudo);

        Sessao.Entrar(caixa);
        var limitado = (await CriarAsync(new DateTime(2026, 10, 19, 10, 0, 0))).Atalhos.Select(a => a.Nome).ToList();
        Assert.Contains("PDV", limitado);
        Assert.Contains("Agenda", limitado);
        Assert.DoesNotContain("Configurações", limitado);
        Assert.DoesNotContain("Relatórios", limitado);
    }
}
