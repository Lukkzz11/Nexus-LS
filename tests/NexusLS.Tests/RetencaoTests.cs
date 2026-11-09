using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class RetencaoTests
{
    private static readonly DateTime Agora = new(2026, 11, 9, 12, 0, 0);

    [Fact]
    public async Task Apaga_so_enviado_antigo_e_o_excesso_de_logs()
    {
        using var banco = new BancoDeTeste();

        using (var db = banco.CriarContexto())
        {
            db.FilaSincronizacao.AddRange(
                Item(EstadoFila.Enviado, Agora.AddDays(-100)),   // some
                Item(EstadoFila.Pendente, Agora.AddDays(-100)),  // fica: ainda não confirmado
                Item(EstadoFila.Enviando, Agora.AddDays(-100)),  // fica
                Item(EstadoFila.Falhou, Agora.AddDays(-100)),    // fica
                Item(EstadoFila.Enviado, Agora.AddDays(-10)));   // fica: recente

            for (var i = 0; i < 5; i++)
                db.SincronizacaoLogs.Add(new SincronizacaoLog
                {
                    IniciadaEm = Agora.AddDays(-i),
                    Resultado = ResultadoSincronizacao.Sucesso
                });

            await db.SaveChangesAsync();
        }

        var retencao = new RetencaoService(banco.Fabrica);
        var resultado = await retencao.AplicarAsync(diasFila: 90, maxLogs: 3, agora: Agora);

        Assert.Equal(1, resultado.FilaApagada);
        Assert.Equal(2, resultado.LogsApagados);

        await using var conferencia = banco.CriarContexto();
        Assert.Equal(4, await conferencia.FilaSincronizacao.CountAsync());
        Assert.Equal(1, await conferencia.FilaSincronizacao.CountAsync(i => i.Estado == EstadoFila.Enviado));

        var datas = await conferencia.SincronizacaoLogs.AsNoTracking()
            .OrderByDescending(l => l.IniciadaEm).Select(l => l.IniciadaEm).ToListAsync();
        Assert.Equal(new[] { Agora, Agora.AddDays(-1), Agora.AddDays(-2) }, datas);
    }

    [Fact]
    public async Task Sem_nada_antigo_nao_apaga_nada()
    {
        using var banco = new BancoDeTeste();
        using (var db = banco.CriarContexto())
        {
            db.FilaSincronizacao.Add(Item(EstadoFila.Enviado, Agora.AddDays(-1)));
            db.SincronizacaoLogs.Add(new SincronizacaoLog { IniciadaEm = Agora });
            await db.SaveChangesAsync();
        }

        var resultado = await new RetencaoService(banco.Fabrica)
            .AplicarAsync(diasFila: 90, maxLogs: 10, agora: Agora);

        Assert.Equal(0, resultado.FilaApagada);
        Assert.Equal(0, resultado.LogsApagados);
    }

    private static ItemFilaSincronizacao Item(EstadoFila estado, DateTime atualizadoEm) => new()
    {
        Entidade = NomesEntidade.Produto,
        EntidadeGuid = Guid.NewGuid(),
        Estado = estado,
        CriadoEm = atualizadoEm,
        AtualizadoEm = atualizadoEm
    };
}
