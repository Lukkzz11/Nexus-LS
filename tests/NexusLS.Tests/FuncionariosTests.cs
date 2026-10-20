using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Roda sozinha (sem outros testes ao lado): um dos testes muda o operador atual, que é compartilhado pelo programa todo.</summary>
[CollectionDefinition("OperadorAtual", DisableParallelization = true)]
public class ColecaoOperadorAtual { }

[Collection("OperadorAtual")]
public sealed class FuncionariosTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private DateTime _agora = new(2026, 10, 20, 10, 0, 0);
    private readonly FuncionarioService _servico;

    public FuncionariosTests() => _servico = new FuncionarioService(_banco.Fabrica, () => _agora);

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private async Task<(Funcionario admin, string codigo)> AdminAsync() =>
        await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");

    // ---------- permissões ----------

    [Fact]
    public void Perfis_prontos_tem_as_permissoes_esperadas()
    {
        Assert.Equal(Permissoes.Todas, Permissoes.DoPerfil(PerfilFuncionario.Administrador));
        var gerente = new Funcionario { Perfil = PerfilFuncionario.Gerente };
        Assert.True(gerente.Pode(Permissao.Financeiro));
        Assert.True(gerente.Pode(Permissao.CancelarVendas));
        Assert.False(gerente.Pode(Permissao.Configuracoes));
        Assert.False(gerente.Pode(Permissao.Funcionarios));

        var caixa = new Funcionario { Perfil = PerfilFuncionario.OperadorDeCaixa };
        Assert.True(caixa.Pode(Permissao.Vender | Permissao.Caixa));
        Assert.False(caixa.Pode(Permissao.CancelarVendas));
        Assert.False(caixa.Pode(Permissao.Estoque));

        var vendedor = new Funcionario { Perfil = PerfilFuncionario.Vendedor };
        Assert.True(vendedor.Pode(Permissao.Vender));
        Assert.False(vendedor.Pode(Permissao.Caixa));
    }

    [Fact]
    public void Personalizado_usa_so_o_que_foi_marcado()
    {
        var f = new Funcionario { Perfil = PerfilFuncionario.Personalizado, PermissoesPersonalizadas = Permissao.Vender | Permissao.Estoque };

        Assert.True(f.Pode(Permissao.Estoque));
        Assert.False(f.Pode(Permissao.Compras));
        Assert.False(f.Pode(Permissao.Vender | Permissao.Compras)); // exige todas as pedidas
    }

    // ---------- primeiro administrador e login ----------

    [Fact]
    public async Task Sem_funcionarios_nao_exige_login_e_o_primeiro_administrador_vira_obrigatorio()
    {
        Assert.False(await _servico.ExigeLoginAsync());

        var (admin, codigo) = await AdminAsync();

        Assert.True(await _servico.ExigeLoginAsync());
        Assert.Equal(PerfilFuncionario.Administrador, admin.Perfil);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", codigo);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CriarPrimeiroAdministradorAsync("Outro", "5678"));
    }

    [Fact]
    public async Task O_pin_nunca_fica_guardado_em_texto_e_autentica_com_o_pin_certo()
    {
        var (admin, _) = await AdminAsync();

        await using var db = _banco.CriarContexto();
        var guardado = await db.Funcionarios.SingleAsync();
        Assert.DoesNotContain("1234", guardado.PinHash);
        Assert.NotEqual(guardado.PinHash, (await _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor), "1234")).PinHash); // sal diferente

        Assert.Equal(admin.Id, (await _servico.AutenticarAsync(admin.Id, "1234")).Id);
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "9999"));
        Assert.Equal("PIN incorreto.", erro.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("12a4")]
    public async Task Pin_invalido_e_recusado(string pin)
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CriarPrimeiroAdministradorAsync("Maria", pin));
    }

    [Fact]
    public async Task Cinco_erros_seguidos_bloqueiam_por_30_segundos_e_depois_libera()
    {
        var (admin, _) = await AdminAsync();
        for (var i = 0; i < FuncionarioService.MaxTentativas; i++)
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "0000"));

        var bloqueado = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "1234"));
        Assert.Contains("Aguarde", bloqueado.Message); // nem o PIN certo entra durante o bloqueio

        _agora = _agora.AddSeconds(31);
        Assert.Equal(admin.Id, (await _servico.AutenticarAsync(admin.Id, "1234")).Id);
    }

    [Fact]
    public async Task Acertar_zera_a_contagem_de_erros()
    {
        var (admin, _) = await AdminAsync();
        for (var i = 0; i < FuncionarioService.MaxTentativas - 1; i++)
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "0000"));
        await _servico.AutenticarAsync(admin.Id, "1234");

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "0000"));
        await _servico.AutenticarAsync(admin.Id, "1234"); // não foi bloqueado
    }

    [Fact]
    public async Task Funcionario_desativado_nao_entra()
    {
        var (admin, _) = await AdminAsync();
        var ana = await _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor), "4321");
        await _servico.DesativarAsync(ana.Id);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(ana.Id, "4321"));
        Assert.Equal(new[] { "Dona Maria" }, (await _servico.ListarAsync()).Select(f => f.Nome));
        Assert.Equal(2, (await _servico.ListarAsync(incluirInativos: true)).Count);
    }

    // ---------- administração ----------

    [Fact]
    public async Task Nunca_fica_sem_administrador_ativo()
    {
        var (admin, _) = await AdminAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.DesativarAsync(admin.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.EditarAsync(admin.Id, new DadosFuncionario("Dona Maria", PerfilFuncionario.Gerente)));

        var outro = await _servico.CadastrarAsync(new DadosFuncionario("Rui", PerfilFuncionario.Administrador), "5555");
        await _servico.DesativarAsync(admin.Id);                    // agora pode: o Rui continua
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.DesativarAsync(outro.Id));
    }

    [Fact]
    public async Task Nome_repetido_entre_ativos_e_recusado_e_editar_atualiza_as_permissoes()
    {
        await AdminAsync();
        var ana = await _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor), "4321");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarAsync(new DadosFuncionario("ana", PerfilFuncionario.Vendedor), "1111"));

        await _servico.EditarAsync(ana.Id, new DadosFuncionario("Ana Paula", PerfilFuncionario.Personalizado, Permissao.Vender | Permissao.Compras, "Estoquista"));

        var salvo = (await _servico.ListarAsync()).Single(f => f.Id == ana.Id);
        Assert.Equal(("Ana Paula", "Estoquista"), (salvo.Nome, salvo.Cargo));
        Assert.True(salvo.Pode(Permissao.Compras));
        Assert.False(salvo.Pode(Permissao.Financeiro));
    }

    [Fact]
    public async Task Definir_pin_troca_a_senha_e_libera_um_bloqueio()
    {
        var (admin, _) = await AdminAsync();
        for (var i = 0; i < FuncionarioService.MaxTentativas; i++)
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "0000"));

        await _servico.DefinirPinAsync(admin.Id, "7777");

        await _servico.AutenticarAsync(admin.Id, "7777");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "1234"));
    }

    // ---------- recuperação ----------

    [Fact]
    public async Task Codigo_de_recuperacao_redefine_o_pin_e_so_serve_uma_vez()
    {
        var (admin, codigo) = await AdminAsync();

        var (func, novoCodigo) = await _servico.RedefinirPinComCodigoAsync(codigo.ToLower().Replace("-", " "), "8888"); // tolera minúsculas e separadores

        Assert.Equal(admin.Id, func.Id);
        await _servico.AutenticarAsync(admin.Id, "8888");
        Assert.NotEqual(codigo, novoCodigo);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.RedefinirPinComCodigoAsync(codigo, "9999")); // o antigo caducou
        await _servico.RedefinirPinComCodigoAsync(novoCodigo, "9999");
    }

    [Fact]
    public async Task Codigo_errado_e_recusado()
    {
        await AdminAsync();

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.RedefinirPinComCodigoAsync("AAAA-BBBB-CCCC", "8888"));
        Assert.Contains("inválido", erro.Message);
    }

    // ---------- operador nas vendas ----------

    [Fact]
    public async Task Venda_e_caixa_gravam_o_nome_de_quem_fez_o_login()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 5);

        OperadorAtual.Nome = "Ana Paula";
        var venda = await new VendaService(_banco.Fabrica).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));
        var turno = await new CaixaService(_banco.Fabrica).AbrirAsync(1000);

        Assert.Equal("Ana Paula", venda.Operador);
        Assert.Equal("Ana Paula", turno.Operador);
    }
}
