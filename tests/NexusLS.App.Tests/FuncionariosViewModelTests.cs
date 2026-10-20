using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Roda sozinha: mexe na sessão (quem está logado), que é compartilhada pelo programa todo.</summary>
[CollectionDefinition("Sessao", DisableParallelization = true)]
public class ColecaoSessao { }

[Collection("Sessao")]
public sealed class FuncionariosViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly FuncionarioService _servico;

    public FuncionariosViewModelTests()
    {
        Sessao.Sair();
        Sessao.DefinirLoginAtivo(false);
        _servico = new FuncionarioService(_banco.Fabrica);
    }

    public void Dispose()
    {
        Sessao.Sair();
        Sessao.DefinirLoginAtivo(false);
        _banco.Dispose();
    }

    private async Task<FuncionariosViewModel> CriarAsync()
    {
        var vm = new FuncionariosViewModel(_servico);
        await vm.Carregamento;
        return vm;
    }

    private async Task<(FuncionariosViewModel vm, Funcionario admin)> ComAdminAsync()
    {
        var (admin, _) = await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        Sessao.Entrar(admin);
        return (await CriarAsync(), admin);
    }

    // ---------- ativar o login ----------

    [Fact]
    public async Task Sem_ninguem_cadastrado_o_login_esta_desligado_e_a_tela_oferece_ativar()
    {
        var vm = await CriarAsync();

        Assert.True(vm.LoginDesligado);
        Assert.Equal("Administrador", vm.PerfilSelecionado.Nome);
    }

    [Fact]
    public async Task Ativar_cria_o_administrador_liga_o_login_loga_quem_ativou_e_mostra_o_codigo()
    {
        var vm = await CriarAsync();
        string? texto = null;
        vm.Avisar = (_, t) => texto = t;
        vm.Nome = "Dona Maria";
        vm.PinNovo = vm.PinConfirmacao = "1234";

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.True(vm.LoginAtivo);
        Assert.True(Sessao.LoginAtivo);
        Assert.Equal("Dona Maria", Sessao.Atual!.Nome);
        Assert.Matches("[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}", texto);
        Assert.Single(vm.Funcionarios);
    }

    [Fact]
    public async Task Pin_diferente_da_confirmacao_ou_curto_nao_ativa()
    {
        var vm = await CriarAsync();
        vm.Nome = "Maria";

        vm.PinNovo = "1234";
        vm.PinConfirmacao = "4321";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("não são iguais", vm.Mensagem);

        vm.PinNovo = vm.PinConfirmacao = "12";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Contains("4 a 8", vm.Mensagem);
        Assert.False(await _servico.ExigeLoginAsync());
    }

    // ---------- cadastro ----------

    [Fact]
    public async Task Cadastra_funcionario_com_perfil_e_as_caixinhas_seguem_o_perfil()
    {
        var (vm, _) = await ComAdminAsync();
        vm.NovoCommand.Execute(null);
        vm.Nome = "Ana";
        vm.PerfilSelecionado = vm.Perfis.Single(p => p.Perfil == PerfilFuncionario.OperadorDeCaixa);

        Assert.True(vm.PermissoesLista.Single(i => i.Permissao == Permissao.Caixa).Marcada);
        Assert.False(vm.PermissoesLista.Single(i => i.Permissao == Permissao.Financeiro).Marcada);
        Assert.False(vm.PerfilPersonalizado);

        vm.PinNovo = vm.PinConfirmacao = "4321";
        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Equal(2, vm.Funcionarios.Count);
        Assert.Equal(PerfilFuncionario.OperadorDeCaixa, vm.Funcionarios.Single(f => f.Nome == "Ana").Perfil);
    }

    [Fact]
    public async Task Perfil_personalizado_grava_so_as_caixinhas_marcadas()
    {
        var (vm, _) = await ComAdminAsync();
        vm.NovoCommand.Execute(null);
        vm.Nome = "Rui";
        vm.PerfilSelecionado = vm.Perfis.Single(p => p.Perfil == PerfilFuncionario.Personalizado);
        foreach (var i in vm.PermissoesLista) i.Marcada = i.Permissao is Permissao.Estoque or Permissao.Compras;
        vm.PinNovo = vm.PinConfirmacao = "5555";

        await vm.SalvarCommand.ExecuteAsync(null);

        var rui = vm.Funcionarios.Single(f => f.Nome == "Rui");
        Assert.True(rui.Pode(Permissao.Estoque | Permissao.Compras));
        Assert.False(rui.Pode(Permissao.Vender));
    }

    [Fact]
    public async Task Editar_sem_pin_mantem_o_pin_e_com_pin_troca()
    {
        var (vm, _) = await ComAdminAsync();
        vm.NovoCommand.Execute(null);
        vm.Nome = "Ana";
        vm.PerfilSelecionado = vm.Perfis.Single(p => p.Perfil == PerfilFuncionario.Vendedor);
        vm.PinNovo = vm.PinConfirmacao = "4321";
        await vm.SalvarCommand.ExecuteAsync(null);
        var ana = vm.Funcionarios.Single(f => f.Nome == "Ana");

        vm.Selecionado = ana;
        vm.Cargo = "Atendente";
        await vm.SalvarCommand.ExecuteAsync(null);
        await _servico.AutenticarAsync(ana.Id, "4321");               // PIN antigo continua

        vm.Selecionado = vm.Funcionarios.Single(f => f.Nome == "Ana");
        vm.PinNovo = vm.PinConfirmacao = "8765";
        await vm.SalvarCommand.ExecuteAsync(null);
        await _servico.AutenticarAsync(ana.Id, "8765");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(ana.Id, "4321"));
    }

    [Fact]
    public async Task Nao_deixa_desativar_o_proprio_usuario_nem_o_ultimo_administrador()
    {
        var (vm, admin) = await ComAdminAsync();
        vm.Selecionado = vm.Funcionarios.Single();

        await vm.AlternarAtivoCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("próprio usuário", vm.Mensagem);
        Assert.True((await _servico.ListarAsync()).Single().Ativo);
    }

    [Fact]
    public async Task Editar_o_proprio_perfil_vale_na_hora_para_a_sessao()
    {
        var (vm, admin) = await ComAdminAsync();
        vm.NovoCommand.Execute(null);
        vm.Nome = "Rui";
        vm.PerfilSelecionado = vm.Perfis.Single(p => p.Perfil == PerfilFuncionario.Administrador);
        vm.PinNovo = vm.PinConfirmacao = "5555";
        await vm.SalvarCommand.ExecuteAsync(null);              // agora há 2 administradores

        vm.Selecionado = vm.Funcionarios.Single(f => f.Id == admin.Id);
        vm.PerfilSelecionado = vm.Perfis.Single(p => p.Perfil == PerfilFuncionario.Vendedor);
        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(Sessao.Pode(Permissao.Financeiro));
        Assert.True(Sessao.Pode(Permissao.Vender));
    }

    [Fact]
    public async Task Novo_codigo_de_recuperacao_so_para_administrador()
    {
        var (vm, _) = await ComAdminAsync();
        string? aviso = null;
        vm.Avisar = (_, t) => aviso = t;
        vm.Selecionado = vm.Funcionarios.Single();

        Assert.True(vm.EhAdministradorEditado);
        await vm.NovoCodigoCommand.ExecuteAsync(null);

        Assert.Matches("[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}", aviso);
    }
}

[Collection("Sessao")]
public sealed class PermissoesDoMenuTests : IDisposable
{
    public PermissoesDoMenuTests()
    {
        Sessao.Sair();
        Sessao.DefinirLoginAtivo(false);
    }

    public void Dispose()
    {
        Sessao.Sair();
        Sessao.DefinirLoginAtivo(false);
    }

    [Fact]
    public void Sem_login_ativo_tudo_e_permitido()
    {
        foreach (var p in Permissoes.Lista) Assert.True(Sessao.Pode(p));
    }

    [Fact]
    public void Com_login_ativo_e_ninguem_logado_nada_e_permitido()
    {
        Sessao.DefinirLoginAtivo(true);

        foreach (var p in Permissoes.Lista) Assert.False(Sessao.Pode(p));
    }

    [Fact]
    public void Entrar_aplica_as_permissoes_do_perfil_e_o_nome_vai_para_o_operador()
    {
        Sessao.Entrar(new Funcionario { Nome = "Ana", Perfil = PerfilFuncionario.Vendedor });

        Assert.True(Sessao.Pode(Permissao.Vender));
        Assert.False(Sessao.Pode(Permissao.Financeiro));
        Assert.Equal("Ana", OperadorAtual.Nome);

        Sessao.Sair();
        Assert.Null(OperadorAtual.Nome);
    }

    [Fact]
    public void Vendas_canceladas_so_para_quem_tem_a_permissao()
    {
        Sessao.Entrar(new Funcionario { Nome = "Ana", Perfil = PerfilFuncionario.OperadorDeCaixa });
        Assert.False(Sessao.Pode(Permissao.CancelarVendas));

        Sessao.Entrar(new Funcionario { Nome = "Rui", Perfil = PerfilFuncionario.Gerente });
        Assert.True(Sessao.Pode(Permissao.CancelarVendas));
    }
}
