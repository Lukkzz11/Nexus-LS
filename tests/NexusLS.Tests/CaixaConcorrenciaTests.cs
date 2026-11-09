using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// O caixa só pode ter um turno aberto. Dois cliques ao mesmo tempo (duas tarefas paralelas no mesmo processo)
/// precisam resultar em UM turno; o outro pedido recebe regra de negócio, não um segundo caixa.
/// </summary>
public class CaixaConcorrenciaTests
{
    private static readonly DateTime Inicio = new(2026, 11, 9, 8, 0, 0);

    [Fact]
    public async Task Duas_aberturas_em_paralelo_criam_um_unico_turno()
    {
        using var banco = new BancoDeTeste();
        var caixa = new CaixaService(banco.Fabrica, () => Inicio);

        // Cada tarefa devolve o turno ou o erro, para o Task.WhenAll não parar na primeira exceção.
        async Task<object> TentarAbrir()
        {
            try { return await caixa.AbrirAsync(0); }
            catch (Exception ex) { return ex; }
        }

        var resultados = await Task.WhenAll(TentarAbrir(), TentarAbrir());

        Assert.Single(resultados.OfType<TurnoCaixa>());
        Assert.IsType<RegraDeNegocioException>(resultados.Single(r => r is not TurnoCaixa));

        await using var db = banco.CriarContexto();
        Assert.Equal(1, await db.TurnosCaixa.CountAsync(t => t.DataFechamento == null));
    }

    [Fact]
    public async Task Fechar_reabrir_e_refazer_a_conferencia_mantem_o_historico()
    {
        using var banco = new BancoDeTeste();
        var agora = Inicio;
        var caixa = new CaixaService(banco.Fabrica, () => agora);

        var primeiro = await caixa.AbrirAsync(1000);
        var v1 = await caixa.FecharAsync(primeiro.Id, new ConferenciaCaixa(1000, 0, 0));
        Assert.Equal(1, v1.Versao);
        Assert.Null(await caixa.ObterAbertoAsync());

        agora = agora.AddHours(1);
        var segundo = await caixa.AbrirAsync(0);
        Assert.NotEqual(primeiro.Id, segundo.Id);

        // O turno já fechado não reabre; refazer a conferência cria a versão 2.
        var v2 = await caixa.FecharAsync(primeiro.Id, new ConferenciaCaixa(1000, 0, 0, "Conferência revista"));
        Assert.Equal(2, v2.Versao);
        Assert.Equal(segundo.Id, (await caixa.ObterAbertoAsync())?.Id); // o turno novo continua aberto

        var historico = await caixa.HistoricoAsync();
        var doPrimeiro = historico.Where(f => f.TurnoId == primeiro.Id).ToList();
        Assert.Equal(2, doPrimeiro.Count);
        Assert.Equal(1, doPrimeiro.Single(f => f.Versao == 1).Versao);
        Assert.Equal(2, doPrimeiro.Single(f => f.Versao == 2).Versao);
    }
}
