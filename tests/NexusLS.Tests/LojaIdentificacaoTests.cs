using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

/// <summary>Identificação da loja (um computador = uma loja): id estável e nome, guardados no JSON de segurança.</summary>
public sealed class LojaIdentificacaoTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_loja_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() { try { File.Delete(_arquivo); } catch (IOException) { } }

    [Fact]
    public void Primeira_leitura_gera_o_id_e_o_nome_padrao_e_grava()
    {
        var c = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.False(string.IsNullOrWhiteSpace(c.LojaId));
        Assert.Equal("Loja 1", c.LojaNome);
        Assert.True(File.Exists(_arquivo));
    }

    [Fact]
    public void O_id_continua_o_mesmo_entre_leituras_e_depois_de_salvar_outras_coisas()
    {
        var store = new ConfiguracaoSegurancaStore(_arquivo);
        var primeiro = store.Carregar();
        primeiro.MinutosInatividade = 5;
        store.Salvar(primeiro);
        var segundo = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.Equal(primeiro.LojaId, segundo.LojaId);
        Assert.Equal(5, segundo.MinutosInatividade);
    }

    [Fact]
    public void Arquivo_antigo_sem_loja_ganha_id_sem_perder_o_resto()
    {
        File.WriteAllText(_arquivo, "{\"MinutosInatividade\": 9}");
        var c = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.Equal(9, c.MinutosInatividade);
        Assert.False(string.IsNullOrWhiteSpace(c.LojaId));
        Assert.Equal(c.LojaId, new ConfiguracaoSegurancaStore(_arquivo).Carregar().LojaId);
    }

    [Fact]
    public void Arquivo_estragado_nao_e_sobrescrito()
    {
        File.WriteAllText(_arquivo, "{lixo");
        var c = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.False(string.IsNullOrWhiteSpace(c.LojaId));
        Assert.Equal("{lixo", File.ReadAllText(_arquivo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_vazio_e_recusado(string nome)
    {
        var c = new ConfiguracaoSeguranca { LojaNome = nome };
        Assert.Throws<RegraDeNegocioException>(() => c.Validar());
    }

    [Fact]
    public void Nome_muito_longo_e_id_invalido_sao_recusados()
    {
        Assert.Throws<RegraDeNegocioException>(() => new ConfiguracaoSeguranca { LojaNome = new string('a', 61) }.Validar());
        Assert.Throws<RegraDeNegocioException>(() => new ConfiguracaoSeguranca { LojaId = "x y/z" }.Validar());
        new ConfiguracaoSeguranca { LojaId = ConfiguracaoSeguranca.NovoLojaId(), LojaNome = "Loja 2" }.Validar();
    }

    [Fact]
    public void Renomear_a_loja_nao_troca_o_id()
    {
        var store = new ConfiguracaoSegurancaStore(_arquivo);
        var c = store.Carregar();
        var id = c.LojaId;
        c.LojaNome = "  Loja 2  ";
        store.Salvar(c);
        var lida = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.Equal("Loja 2", lida.LojaNome);
        Assert.Equal(id, lida.LojaId);
    }
}
