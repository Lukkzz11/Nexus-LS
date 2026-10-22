using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class MonitorSincronizacaoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true);
    private readonly DestinoFalso _destino = new();
    private readonly DateTime _agora = new(2026, 10, 22, 9, 0, 0);
    private bool _configurado = true;
    private bool _podeEnviar = true;

    public void Dispose() => _banco.Dispose();

    private MonitorSincronizacao Monitor() => new(
        new ProcessadorFila(_banco.Fabrica, new SincronizacaoRepositorio(_banco.Fabrica, () => _agora), _destino, () => _agora),
        () => _configurado, () => _podeEnviar);

    private Task Cadastrar(string nome, string codigo) =>
        new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto(nome, codigo, 5000, 2000, 1), 5);

    [Fact]
    public async Task Sem_configuracao_nao_tenta_enviar_e_a_fila_acumula()
    {
        _configurado = false;
        _podeEnviar = false;
        await Cadastrar("A", "1");

        var e = await Monitor().CicloAsync();

        Assert.Equal(NivelSincronizacao.NaoConfigurado, e.Nivel);
        Assert.Equal(0, _destino.Chamadas);
        Assert.Equal(1, e.Fila.Pendentes);
    }

    [Fact]
    public async Task Configurado_mas_sem_login_avisa_e_nao_envia()
    {
        _podeEnviar = false;
        await Cadastrar("A", "1");

        var e = await Monitor().CicloAsync();

        Assert.Equal(NivelSincronizacao.SemLogin, e.Nivel);
        Assert.Contains("1 aguardando", e.Texto);
        Assert.Equal(0, _destino.Chamadas);
    }

    [Fact]
    public async Task Com_login_envia_e_mostra_em_dia()
    {
        await Cadastrar("A", "1");
        var e = await Monitor().CicloAsync("ana");
        Assert.Equal(NivelSincronizacao.EmDia, e.Nivel);
        Assert.Equal(1, e.Fila.Enviados);
    }

    [Fact]
    public async Task Sem_conexao_mostra_quantos_aguardam_e_volta_ao_normal_depois()
    {
        await Cadastrar("A", "1");
        await Cadastrar("B", "2");
        _destino.FalhaNaVerificacao = new FalhaDestinoException("Sem conexão com a internet.");
        var monitor = Monitor();

        var e1 = await monitor.CicloAsync();
        Assert.Equal(NivelSincronizacao.SemConexao, e1.Nivel);
        Assert.Contains("2 aguardando", e1.Texto);

        _destino.FalhaNaVerificacao = null;
        // a espera do reagendamento ainda não passou: segue como sem conexão, sem nova tentativa
        var e2 = await monitor.CicloAsync();
        Assert.Equal(NivelSincronizacao.SemConexao, e2.Nivel);
    }

    [Fact]
    public async Task Produto_recusado_pelo_servidor_chama_atencao()
    {
        await Cadastrar("A", "1");
        _destino.Falha = (_, _) => new FalhaDestinoException("recusado", GravidadeFalha.DadosRecusados);

        var e = await Monitor().CicloAsync();

        Assert.Equal(NivelSincronizacao.Atencao, e.Nivel);
        Assert.Contains("1 com erro", e.Texto);
    }

    [Fact]
    public async Task Recuperar_ao_abrir_devolve_itens_presos_a_fila()
    {
        await Cadastrar("A", "1");
        await using (var db = _banco.CriarContexto())
        {
            var f = db.FilaSincronizacao.First();
            f.Estado = EstadoFila.Enviando;
            await db.SaveChangesAsync();
        }
        var monitor = Monitor();
        Assert.Equal(1, await monitor.RecuperarAoAbrirAsync());
        Assert.Equal(NivelSincronizacao.EmDia, (await monitor.CicloAsync()).Nivel);
    }
}
