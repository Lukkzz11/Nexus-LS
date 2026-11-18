using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class AvisosAoAbrirTests : IDisposable
{
    private static readonly DateTime Hoje = new(2026, 11, 18);
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static Lancamento Conta(int dias, long valor, bool paga = false, bool cancelada = false, TipoLancamento tipo = TipoLancamento.Despesa) =>
        new() { Descricao = "x", Tipo = tipo, ValorCentavos = valor, Vencimento = Hoje.AddDays(dias), PagoEm = paga ? Hoje : null, Cancelado = cancelada };

    [Fact]
    public void Separa_vencidas_das_que_vencem_em_breve_e_soma_os_valores()
    {
        var f = AvisosAoAbrir.Contas(new[] { Conta(-2, 10000), Conta(-1, 5000), Conta(0, 2000), Conta(3, 3000), Conta(4, 9999) }, 3, Hoje);
        Assert.Contains("2 vencida(s)", f);
        Assert.Contains("R$ 150,00", f);
        Assert.Contains("2 vencendo em até 3 dia(s)", f);
        Assert.Contains("R$ 50,00", f);   // 20 + 30; a de +4 dias fica de fora
    }

    [Fact]
    public void Paga_cancelada_e_receita_nao_entram_e_sem_nada_devolve_nulo()
    {
        var f = AvisosAoAbrir.Contas(new[] { Conta(-1, 100, paga: true), Conta(-1, 100, cancelada: true), Conta(-1, 100, tipo: TipoLancamento.Receita) }, 3, Hoje);
        Assert.Null(f);
    }

    [Fact]
    public void Com_zero_dias_avisa_so_das_de_hoje()
    {
        var f = AvisosAoAbrir.Contas(new[] { Conta(0, 100), Conta(1, 100) }, 0, Hoje);
        Assert.Contains("1 vencendo hoje", f);
    }

    [Fact]
    public void Backup_atrasado_so_depois_do_limite_e_sem_backup_sempre_avisa()
    {
        var agora = Hoje.AddHours(12);
        Assert.Null(AvisosAoAbrir.Backup(agora.AddDays(-2), agora, 3));
        Assert.Contains("há 3 dia(s)", AvisosAoAbrir.Backup(agora.AddDays(-3), agora, 3));
        Assert.Contains("Nenhum backup", AvisosAoAbrir.Backup(null, agora, 3));
    }

    [Fact]
    public async Task Consulta_traz_so_despesa_em_aberto_ate_o_limite()
    {
        using (var db = _banco.Fabrica.CreateDbContext())
        {
            db.Lancamentos.AddRange(Conta(-5, 100), Conta(2, 200), Conta(10, 300), Conta(1, 400, paga: true), Conta(1, 500, tipo: TipoLancamento.Receita));
            db.SaveChanges();
        }
        var l = await new FinanceiroService(_banco.Fabrica).ListarContasAPagarProximasAsync(3, Hoje);
        Assert.Equal(new long[] { 100, 200 }, l.Select(x => x.ValorCentavos).ToArray());
    }
}
