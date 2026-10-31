using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Operacional;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Caixa e financeiro no envio operacional: documentos, referências por Guid e fila.</summary>
public sealed class OperacionalCaixaFinanceiroTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid-ana");
    private readonly DestinoOperacionalFalso _destino = new();
    private readonly DateTime _agora = new(2026, 10, 30, 9, 0, 0);

    public void Dispose() => _banco.Dispose();

    private MontadorOperacional Montador => new(_banco.Fabrica);

    private ProcessadorFilaOperacional Processador() => new(_banco.Fabrica, Montador, _destino, () => _agora);

    private async Task<DocumentoOperacional> Documento(string entidade, Guid guid)
    {
        var r = await Montador.MontarAsync(entidade, new[] { guid });
        Assert.Empty(r.Ausentes);
        return Assert.Single(r.Documentos);
    }

    [Fact]
    public async Task Turno_movimento_e_fechamento_apontam_para_o_turno_pelo_Guid()
    {
        TurnoCaixa turno; MovimentoCaixa mov; FechamentoCaixa fech;
        await using (var db = _banco.CriarContexto())
        {
            turno = new TurnoCaixa { DataAbertura = _agora, TrocoInicialCentavos = 10000, Operador = "ana" };
            db.TurnosCaixa.Add(turno);
            await db.SaveChangesAsync();
            mov = new MovimentoCaixa { TurnoId = turno.Id, DataHora = _agora, Tipo = TipoMovimentoCaixa.Sangria, ValorCentavos = 5000, Descricao = "banco" };
            fech = new FechamentoCaixa { TurnoId = turno.Id, Versao = 1, DataHora = _agora, Operador = "ana", EsperadoDinheiroCentavos = 15000, ConferidoDinheiroCentavos = 15000 };
            db.MovimentosCaixa.Add(mov);
            db.FechamentosCaixa.Add(fech);
            await db.SaveChangesAsync();
        }

        var dTurno = await Documento(NomesEntidade.TurnoCaixa, turno.Guid);
        Assert.Equal("turnos", dTurno.Colecao);
        Assert.Equal(10000L, dTurno.Campos["trocoInicialCentavos"]);
        Assert.Null(dTurno.Campos["fechadoEm"]);

        var dMov = await Documento(NomesEntidade.MovimentoCaixa, mov.Guid);
        Assert.Equal("movimentosCaixa", dMov.Colecao);
        Assert.Equal(turno.Guid, dMov.Campos["turnoGuid"]);
        Assert.Equal("Sangria", dMov.Campos["tipo"]);

        var dFech = await Documento(NomesEntidade.FechamentoCaixa, fech.Guid);
        Assert.Equal(turno.Guid, dFech.Campos["turnoGuid"]);
        Assert.Equal(1, dFech.Campos["versao"]);
    }

    [Fact]
    public async Task Lancamento_leva_vencimento_como_data_e_referencias_por_Guid()
    {
        ContaFinanceira conta; Lancamento lanc;
        await using (var db = _banco.CriarContexto())
        {
            conta = new ContaFinanceira { Nome = "Banco X", Tipo = TipoContaFin.Banco, Ativa = true, CriadaEm = _agora };
            db.ContasFinanceiras.Add(conta);
            await db.SaveChangesAsync();
            lanc = new Lancamento
            {
                Tipo = TipoLancamento.Despesa, Descricao = "Aluguel", ValorCentavos = 150000, Vencimento = new DateTime(2026, 11, 5),
                ContaId = conta.Id, Parcela = 1, TotalParcelas = 1, CriadoEm = _agora
            };
            db.Lancamentos.Add(lanc);
            await db.SaveChangesAsync();
        }

        var d = await Documento(NomesEntidade.Lancamento, lanc.Guid);

        Assert.Equal("lancamentos", d.Colecao);
        Assert.Equal("2026-11-05", d.Campos["vencimento"]);       // data de calendário não sofre mudança de fuso
        Assert.Equal(conta.Guid, d.Campos["contaGuid"]);
        Assert.Null(d.Campos["compraGuid"]);
        Assert.Equal("Despesa", d.Campos["tipo"]);
        Assert.Equal(false, d.Campos["cancelado"]);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)d.Campos["criadoEm"]!).Kind);
    }

    [Fact]
    public async Task Transferencia_e_cheque_e_pagamento_resolvem_as_referencias()
    {
        ContaFinanceira a, b; TransferenciaConta transf; Cliente cliente; PagamentoCliente pag; ChequeRecebido cheque;
        await using (var db = _banco.CriarContexto())
        {
            a = new ContaFinanceira { Nome = "Gaveta", Tipo = TipoContaFin.Caixa, Ativa = true, CriadaEm = _agora };
            b = new ContaFinanceira { Nome = "Banco", Tipo = TipoContaFin.Banco, Ativa = true, CriadaEm = _agora };
            cliente = new Cliente { Nome = "Ana" };
            db.ContasFinanceiras.AddRange(a, b);
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            transf = new TransferenciaConta { ContaOrigemId = a.Id, ContaDestinoId = b.Id, ValorCentavos = 20000, Data = new DateTime(2026, 10, 30), CriadaEm = _agora };
            pag = new PagamentoCliente { ClienteId = cliente.Id, DataHora = _agora, ValorCentavos = 3000, Forma = FormaPagamento.Cheque };
            db.Transferencias.Add(transf);
            db.PagamentosCliente.Add(pag);
            await db.SaveChangesAsync();
            cheque = new ChequeRecebido { ValorCentavos = 3000, BomPara = new DateTime(2026, 12, 1), Situacao = SituacaoCheque.Pendente, PagamentoClienteId = pag.Id, CriadoEm = _agora };
            db.ChequesRecebidos.Add(cheque);
            await db.SaveChangesAsync();
        }

        var dTransf = await Documento(NomesEntidade.TransferenciaConta, transf.Guid);
        Assert.Equal(a.Guid, dTransf.Campos["contaOrigemGuid"]);
        Assert.Equal(b.Guid, dTransf.Campos["contaDestinoGuid"]);
        Assert.Equal("2026-10-30", dTransf.Campos["data"]);

        var dPag = await Documento(NomesEntidade.PagamentoCliente, pag.Guid);
        Assert.Equal(cliente.Guid, dPag.Campos["clienteGuid"]);
        Assert.Equal("Cheque", dPag.Campos["forma"]);

        var dCheque = await Documento(NomesEntidade.ChequeRecebido, cheque.Guid);
        Assert.Equal(pag.Guid, dCheque.Campos["pagamentoClienteGuid"]);
        Assert.Null(dCheque.Campos["vendaGuid"]);
        Assert.Equal("2026-12-01", dCheque.Campos["bomPara"]);
        Assert.Equal("Pendente", dCheque.Campos["situacao"]);
    }

    [Fact]
    public async Task Todos_os_documentos_levam_guid_atualizadoEm_origem_e_versao_do_formato()
    {
        TurnoCaixa turno;
        await using (var db = _banco.CriarContexto())
        {
            turno = new TurnoCaixa { DataAbertura = _agora };
            db.TurnosCaixa.Add(turno);
            await db.SaveChangesAsync();
        }

        var d = await Documento(NomesEntidade.TurnoCaixa, turno.Guid);

        Assert.Equal(turno.Guid, d.Campos["guid"]);
        Assert.Equal("nexus-ls", d.Campos["origem"]);
        Assert.Equal(MontadorOperacional.VersaoDoFormato, d.Campos["versaoDoFormato"]);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)d.Campos["atualizadoEm"]!).Kind);
    }

    [Fact]
    public async Task Processador_envia_cada_entidade_para_a_sua_colecao_e_fecha_o_turno_atualiza_o_mesmo_documento()
    {
        TurnoCaixa turno;
        await using (var db = _banco.CriarContexto())
        {
            turno = new TurnoCaixa { DataAbertura = _agora, TrocoInicialCentavos = 5000 };
            db.TurnosCaixa.Add(turno);
            await db.SaveChangesAsync();
            db.MovimentosCaixa.Add(new MovimentoCaixa { TurnoId = turno.Id, DataHora = _agora, Tipo = TipoMovimentoCaixa.Reforco, ValorCentavos = 1000 });
            db.Lancamentos.Add(new Lancamento { Tipo = TipoLancamento.Receita, Descricao = "Serviço", ValorCentavos = 8000, Vencimento = _agora, CriadoEm = _agora });
            await db.SaveChangesAsync();
        }
        var proc = Processador();

        var r = await proc.ProcessarAsync();

        Assert.Equal(3, r.Enviados);
        Assert.Equal(new[] { "lancamentos", "movimentosCaixa", "turnos" }, _destino.Todos.Select(d => d.Colecao).OrderBy(c => c).ToArray());

        await using (var db = _banco.CriarContexto()) // fecha o turno
        {
            var t = await db.TurnosCaixa.SingleAsync();
            t.DataFechamento = _agora.AddHours(8);
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, (await proc.ProcessarAsync()).Enviados); // só o turno mudou
        var reenviado = _destino.Todos.Last();
        Assert.Equal(turno.Guid, reenviado.Guid);
        Assert.NotNull(reenviado.Campos["fechadoEm"]);
    }

    [Fact]
    public async Task Dados_antigos_de_caixa_e_financeiro_entram_na_fila_uma_vez_so()
    {
        await using (var db = _banco.CriarContexto())
        {
            db.ContasFinanceiras.Add(new ContaFinanceira { Nome = "Gaveta", Tipo = TipoContaFin.Caixa, Ativa = true, CriadaEm = _agora });
            db.TurnosCaixa.Add(new TurnoCaixa { DataAbertura = _agora });
            db.Lancamentos.Add(new Lancamento { Tipo = TipoLancamento.Despesa, Descricao = "Luz", ValorCentavos = 9000, Vencimento = _agora, CriadoEm = _agora });
            await db.SaveChangesAsync();
            db.FilaSincronizacao.RemoveRange(db.FilaSincronizacao);
            await db.SaveChangesAsync();
        }
        var proc = Processador();

        Assert.Equal(3, await proc.EnfileirarExistentesAsync());
        Assert.Equal(0, await proc.EnfileirarExistentesAsync());
        Assert.Equal(3, (await proc.ProcessarAsync()).Enviados);
    }

    [Fact]
    public async Task Referencia_para_registro_ausente_nao_derruba_o_envio()
    {
        Lancamento lanc;
        await using (var db = _banco.CriarContexto())
        {
            lanc = new Lancamento { Tipo = TipoLancamento.Despesa, Descricao = "Órfão", ValorCentavos = 100, Vencimento = _agora, ContaId = 999, CriadoEm = _agora };
            db.Lancamentos.Add(lanc);
            await db.SaveChangesAsync();
        }

        var d = await Documento(NomesEntidade.Lancamento, lanc.Guid);

        Assert.Null(d.Campos["contaGuid"]); // sem a conta no banco, o campo simplesmente não vai
    }
}
