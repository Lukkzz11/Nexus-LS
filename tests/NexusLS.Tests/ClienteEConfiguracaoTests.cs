using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class ClienteServiceTests
{
    private static readonly DateTime Agora = new(2026, 10, 18, 9, 0, 0);

    [Fact]
    public async Task Cadastra_cliente_limpando_campos_vazios()
    {
        using var banco = new BancoDeTeste();
        var servico = new ClienteService(banco.Fabrica, () => Agora);

        var c = await servico.CadastrarAsync(new DadosCliente("  Maria Silva ", "  ", "(11) 99999-0000"));

        Assert.Equal("Maria Silva", c.Nome);
        Assert.Null(c.Documento);
        Assert.Equal("(11) 99999-0000", c.Telefone);
        Assert.Equal(Agora, c.DataCadastro);
        Assert.True(c.Ativo);
    }

    [Fact]
    public async Task Cliente_sem_nome_e_recusado()
    {
        using var banco = new BancoDeTeste();
        var servico = new ClienteService(banco.Fabrica);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CadastrarAsync(new DadosCliente(" ")));
    }

    [Fact]
    public async Task Edita_e_busca_por_nome_documento_ou_telefone()
    {
        using var banco = new BancoDeTeste();
        var servico = new ClienteService(banco.Fabrica);
        var maria = await servico.CadastrarAsync(new DadosCliente("Maria", "111.222.333-44", "1111"));
        await servico.CadastrarAsync(new DadosCliente("João", null, "2222"));

        await servico.EditarAsync(maria.Id, new DadosCliente("Maria Souza", "111.222.333-44", "1111"));

        Assert.Single(await servico.BuscarAsync("souza"));
        Assert.Single(await servico.BuscarAsync("222.333"));
        Assert.Single(await servico.BuscarAsync("2222"));
        Assert.Equal(2, (await servico.BuscarAsync("")).Count);
    }

    [Fact]
    public async Task Desativado_some_da_busca_mas_continua_no_banco()
    {
        using var banco = new BancoDeTeste();
        var servico = new ClienteService(banco.Fabrica);
        var c = await servico.CadastrarAsync(new DadosCliente("Pedro"));

        await servico.DesativarAsync(c.Id);

        Assert.Empty(await servico.BuscarAsync("Pedro"));
        Assert.Single(await servico.BuscarAsync("Pedro", incluirInativos: true));
        await servico.ReativarAsync(c.Id);
        Assert.Single(await servico.BuscarAsync("Pedro"));
    }
}

public class ConfiguracaoServiceTests
{
    [Fact]
    public async Task Primeira_leitura_cria_a_configuracao_padrao()
    {
        using var banco = new BancoDeTeste();
        var servico = new ConfiguracaoService(banco.Fabrica);

        var config = await servico.ObterAsync();

        Assert.Equal(1, config.Id);
        Assert.Equal("Nexus LS", config.NomeLoja);
        using var db = banco.CriarContexto();
        Assert.Single(db.ConfiguracoesLoja);
    }

    [Fact]
    public async Task Salva_e_le_os_dados_da_loja_sem_duplicar_a_linha()
    {
        using var banco = new BancoDeTeste();
        var servico = new ConfiguracaoService(banco.Fabrica);

        await servico.SalvarAsync(new DadosLoja(" Loja do Zé ", "12.345.678/0001-90", "Rua A, 10", "(11) 3333-4444", "Volte sempre!"));
        await servico.SalvarAsync(new DadosLoja("Loja do Zé", Endereco: "Rua B, 20"));

        var config = await servico.ObterAsync();
        Assert.Equal("Loja do Zé", config.NomeLoja);
        Assert.Equal("Rua B, 20", config.Endereco);
        Assert.Null(config.Documento); // o segundo salvamento substitui tudo
        using var db = banco.CriarContexto();
        Assert.Single(db.ConfiguracoesLoja);
    }

    [Fact]
    public async Task Nome_da_loja_e_obrigatorio()
    {
        using var banco = new BancoDeTeste();
        var servico = new ConfiguracaoService(banco.Fabrica);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.SalvarAsync(new DadosLoja("  ")));
    }
}
