using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Consultor;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Os cartões do Consultor Nexus na tela Início: carregam em segundo plano, atualizam, dispensam e respeitam permissões.</summary>
public sealed class ConsultorViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-consultorvm-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _agora = new(2026, 11, 19, 14, 30, 0);
    private readonly List<ItemMenu> _aberturas = new();

    public ConsultorViewModelTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private sealed class Regra(string id, AreaConsultor area, GravidadeSugestao gravidade, DestinoConsultor destino = DestinoConsultor.Desempenho, bool dispara = true) : IRegraConsultor
    {
        public string Id => id;
        public AreaConsultor Area => area;
        public Sugestao? Avaliar(DadosConsultor dados) =>
            dispara ? new Sugestao(id, "Título " + id, "Texto " + id, gravidade, area, "Abrir " + id) { Destino = destino } : null;
    }

    private static IReadOnlyList<IRegraConsultor> RegrasDeTeste() =>
    [
        new Regra("contas", AreaConsultor.Financeiro, GravidadeSugestao.Urgente, DestinoConsultor.Financeiro),
        new Regra("estoque", AreaConsultor.Estoque, GravidadeSugestao.Atencao, DestinoConsultor.Estoque),
        new Regra("vendas", AreaConsultor.Vendas, GravidadeSugestao.Informacao)
    ];

    private ConsultorNexus Servico(IReadOnlyList<IRegraConsultor>? regras = null, IDbContextFactory<PdvDbContext>? fabrica = null) =>
        new(fabrica ?? _banco.Fabrica, new DispensasConsultorStore(Path.Combine(_pasta, "consultor.json")), () => _agora, regras ?? RegrasDeTeste());

    private async Task<ConsultorViewModel> CriarAsync(ConsultorNexus? servico = null, Func<AreaConsultor, bool>? podeVer = null)
    {
        var vm = new ConsultorViewModel(servico ?? Servico(), _aberturas.Add, podeVer ?? (_ => true), () => _agora);
        await vm.Carregamento;
        return vm;
    }

    [Fact]
    public async Task Carrega_os_cartoes_do_mais_grave_ao_menos_e_mostra_o_horario()
    {
        var vm = await CriarAsync();

        Assert.Equal(["contas", "estoque", "vendas"], vm.Cartoes.Select(c => c.Id));
        Assert.True(vm.JaCarregou);
        Assert.False(vm.Carregando);
        Assert.False(vm.Vazio);
        Assert.True(vm.TemCartoes);
        Assert.Equal("Atualizado às 14:30", vm.AtualizadoEmTexto);
        Assert.Equal("Urgente", vm.Cartoes[0].GravidadeTexto);
        Assert.Equal("Atenção", vm.Cartoes[1].GravidadeTexto);
        Assert.Equal("Dica", vm.Cartoes[2].GravidadeTexto);
        Assert.Equal("Financeiro", vm.Cartoes[0].AreaTexto);
    }

    [Fact]
    public async Task Cartao_tem_nome_acessivel_com_area_gravidade_e_titulo()
    {
        var vm = await CriarAsync();
        Assert.Equal("Sugestão de financeiro, urgente: Título contas", vm.Cartoes[0].NomeAcessivel);
        Assert.Equal("Dispensar a sugestão Título contas", vm.Cartoes[0].NomeDispensar);
    }

    /// <summary>Banco que trava até alguém liberar: prova que o construtor do ViewModel não espera o banco (a tela não congela).</summary>
    private sealed class FabricaQueEspera(IDbContextFactory<PdvDbContext> real, ManualResetEventSlim liberar) : IDbContextFactory<PdvDbContext>
    {
        public PdvDbContext CreateDbContext() { liberar.Wait(); return real.CreateDbContext(); }
    }

    [Fact]
    public async Task Carregar_nao_trava_quem_chamou_a_leitura_roda_em_segundo_plano()
    {
        using var liberar = new ManualResetEventSlim(false);
        var vm = new ConsultorViewModel(Servico(fabrica: new FabricaQueEspera(_banco.Fabrica, liberar)), _aberturas.Add, _ => true, () => _agora);

        // O construtor já voltou, mesmo com o banco preso: a tela segue viva, mostrando "calculando".
        Assert.True(vm.Carregando);
        Assert.False(vm.Carregamento.IsCompleted);
        Assert.False(vm.Vazio);
        Assert.Equal("Calculando...", vm.TextoBotaoAtualizar);

        liberar.Set();
        await vm.Carregamento.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(vm.Carregando);
        Assert.Equal(3, vm.Cartoes.Count);
        Assert.Equal("Atualizar", vm.TextoBotaoAtualizar);
    }

    [Fact]
    public async Task Sem_nenhuma_sugestao_mostra_estado_vazio_amigavel()
    {
        var servico = Servico([new Regra("x", AreaConsultor.Vendas, GravidadeSugestao.Atencao, dispara: false)]);
        var vm = await CriarAsync(servico);

        Assert.Empty(vm.Cartoes);
        Assert.True(vm.Vazio);
        Assert.False(vm.TemCartoes);
        Assert.Contains("Tudo tranquilo", vm.TextoVazio);
        Assert.False(vm.TemDispensados);
    }

    [Fact]
    public async Task Antes_de_terminar_de_carregar_nao_mostra_o_estado_vazio()
    {
        using var liberar = new ManualResetEventSlim(false);
        var vm = new ConsultorViewModel(Servico(fabrica: new FabricaQueEspera(_banco.Fabrica, liberar)), _aberturas.Add, _ => true, () => _agora);
        Assert.False(vm.Vazio);
        liberar.Set();
        await vm.Carregamento.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Dispensar_tira_o_cartao_guarda_a_escolha_e_um_novo_Inicio_nao_mostra()
    {
        var vm = await CriarAsync();
        vm.DispensarCommand.Execute(vm.Cartoes.Single(c => c.Id == "estoque"));

        Assert.Equal(["contas", "vendas"], vm.Cartoes.Select(c => c.Id));
        Assert.True(vm.TemDispensados);
        Assert.True(File.Exists(Path.Combine(_pasta, "consultor.json")));

        var outro = await CriarAsync(Servico());   // "reabrir o programa": outro serviço, mesmo arquivo
        Assert.Equal(["contas", "vendas"], outro.Cartoes.Select(c => c.Id));
        Assert.True(outro.TemDispensados);
    }

    [Fact]
    public async Task Dispensar_o_ultimo_cartao_leva_ao_estado_vazio()
    {
        var vm = await CriarAsync(Servico([new Regra("so", AreaConsultor.Vendas, GravidadeSugestao.Informacao)]));
        vm.DispensarCommand.Execute(vm.Cartoes.Single());
        Assert.True(vm.Vazio);
        Assert.NotEmpty(vm.TextoVazio);
    }

    [Fact]
    public async Task Mostrar_os_dispensados_traz_os_cartoes_de_volta()
    {
        var vm = await CriarAsync();
        vm.DispensarCommand.Execute(vm.Cartoes.Single(c => c.Id == "contas"));
        vm.DispensarCommand.Execute(vm.Cartoes.Single(c => c.Id == "vendas"));
        Assert.Single(vm.Cartoes);

        await vm.MostrarDispensadosCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Cartoes.Count);
        Assert.False(vm.TemDispensados);
    }

    [Fact]
    public async Task Dispensar_com_parametro_nulo_nao_faz_nada()
    {
        var vm = await CriarAsync();
        vm.DispensarCommand.Execute(null);
        Assert.Equal(3, vm.Cartoes.Count);
    }

    [Fact]
    public async Task Atualizar_le_o_banco_de_novo_mesmo_com_cache()
    {
        var servico = Servico();
        var vm = await CriarAsync(servico);
        Assert.Equal(1, servico.Leituras);

        await vm.AtualizarCommand.ExecuteAsync(null);
        Assert.Equal(2, servico.Leituras);
        Assert.False(vm.Carregando);
    }

    [Fact]
    public async Task Sem_permissao_do_financeiro_nao_mostra_cartao_financeiro()
    {
        var vm = await CriarAsync(podeVer: area => area != AreaConsultor.Financeiro);
        Assert.DoesNotContain(vm.Cartoes, c => c.AreaTexto == "Financeiro");
        Assert.Equal(["estoque", "vendas"], vm.Cartoes.Select(c => c.Id));
    }

    [Fact]
    public async Task Sem_acesso_a_nenhum_assunto_explica_em_vez_de_mostrar_cartoes()
    {
        var vm = await CriarAsync(podeVer: _ => false);
        Assert.Empty(vm.Cartoes);
        Assert.True(vm.Vazio);
        Assert.Contains("acesso", vm.TextoVazio);
    }

    [Theory]
    [InlineData(DestinoConsultor.Estoque, ItemMenu.EstoqueDaLoja)]
    [InlineData(DestinoConsultor.EstoqueParado, ItemMenu.RelatorioProdutos)]
    [InlineData(DestinoConsultor.Desempenho, ItemMenu.Desempenho)]
    [InlineData(DestinoConsultor.Clientes, ItemMenu.Clientes)]
    [InlineData(DestinoConsultor.Financeiro, ItemMenu.Financeiro)]
    [InlineData(DestinoConsultor.RelatorioFinanceiro, ItemMenu.RelatorioFinanceiro)]
    public async Task Botao_do_cartao_abre_a_tela_certa(DestinoConsultor destino, ItemMenu esperado)
    {
        var vm = await CriarAsync(Servico([new Regra("r", AreaConsultor.Vendas, GravidadeSugestao.Atencao, destino)]));
        Assert.True(vm.Cartoes.Single().TemAcao);
        vm.AbrirCommand.Execute(vm.Cartoes.Single());
        Assert.Equal([esperado], _aberturas);
    }

    [Fact]
    public async Task Cartao_sem_destino_nao_tem_botao_e_nao_abre_nada()
    {
        var vm = await CriarAsync(Servico([new Regra("r", AreaConsultor.Vendas, GravidadeSugestao.Informacao, DestinoConsultor.Nenhum)]));
        Assert.False(vm.Cartoes.Single().TemAcao);
        vm.AbrirCommand.Execute(vm.Cartoes.Single());
        Assert.Empty(_aberturas);
    }

    private sealed class FabricaQuebrada : IDbContextFactory<PdvDbContext>
    {
        public PdvDbContext CreateDbContext() => throw new InvalidOperationException("banco indisponível");
    }

    [Fact]
    public async Task Erro_ao_calcular_vira_mensagem_amigavel_e_nao_derruba_a_tela()
    {
        var vm = await CriarAsync(Servico(fabrica: new FabricaQuebrada()));

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Não foi possível calcular", vm.Mensagem);
        Assert.DoesNotContain("indisponível", vm.Mensagem);   // o detalhe técnico não vai para o usuário
        Assert.False(vm.Carregando);
        Assert.Empty(vm.Cartoes);
    }

    [Fact]
    public async Task Inicio_hospeda_o_consultor_e_sem_consultor_nao_mostra_o_painel()
    {
        var consultor = await CriarAsync();
        var inicio = new InicioViewModel(new InicioService(_banco.Fabrica, () => _agora), null, _ => { }, () => _agora, consultor: consultor);
        await inicio.Carregamento;
        Assert.True(inicio.TemConsultor);
        Assert.Same(consultor, inicio.Consultor);

        var sem = new InicioViewModel(new InicioService(_banco.Fabrica, () => _agora), null, _ => { }, () => _agora);
        await sem.Carregamento;
        Assert.False(sem.TemConsultor);
    }

    [Fact]
    public void Tela_usa_cores_do_tema_com_DynamicResource_e_nomes_para_leitor_de_tela()
    {
        var xaml = File.ReadAllText(CaminhoDaView("InicioView.xaml"));
        var inicio = xaml.IndexOf("x:Name=\"PainelConsultor\"", StringComparison.Ordinal);
        var fim = xaml.IndexOf("x:Name=\"AreaPrincipal\"", StringComparison.Ordinal);
        Assert.True(inicio > 0 && fim > inicio);
        var bloco = xaml[inicio..fim];

        Assert.DoesNotContain("StaticResource Cor", bloco);
        Assert.Contains("DynamicResource CorTexto", bloco);
        Assert.Contains("AutomationProperties.Name=\"Atualizar sugestões do Consultor\"", bloco);
        Assert.Contains("AutomationProperties.Name=\"{Binding NomeDispensar}\"", bloco);
        Assert.Contains("AutomationProperties.Name=\"{Binding NomeAcessivel}\"", bloco);
    }

    private static string CaminhoDaView(string arquivo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusLS.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "NexusLS.App", "Views", arquivo);
    }
}
