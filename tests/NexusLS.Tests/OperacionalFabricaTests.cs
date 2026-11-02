using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Operacional;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Fábrica (matéria-prima, ordens de produção e perdas) no envio operacional.</summary>
public sealed class OperacionalFabricaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid-ana");
    private readonly DateTime _agora = new(2026, 11, 1, 9, 0, 0);

    public void Dispose() => _banco.Dispose();

    private ProducaoService Servico() => new(_banco.Fabrica, new ProdutoService(_banco.Fabrica), () => _agora);

    private async Task<DocumentoOperacional> Documento(string entidade, Guid guid)
    {
        var r = await new MontadorOperacional(_banco.Fabrica).MontarAsync(entidade, new[] { guid });
        Assert.Empty(r.Ausentes);
        return Assert.Single(r.Documentos);
    }

    private List<(string Entidade, Guid Guid)> Fila()
    {
        using var db = _banco.CriarContexto();
        return db.FilaSincronizacao.AsNoTracking().ToList().Select(f => (f.Entidade, f.EntidadeGuid)).ToList();
    }

    [Fact]
    public async Task Materia_ordem_e_perda_entram_na_fila_e_viram_documentos_ligados_pelo_Guid()
    {
        var s = Servico();
        var materia = await s.CadastrarMateriaAsync(new DadosMateriaPrima("Malha PV", "Tecido", "metros", 50, 1890), 30);
        var ordem = await s.CriarOrdemAsync(new DadosOrdemProducao("Camiseta Surf", new Dictionary<string, int> { ["P"] = 10, ["M"] = 20 }, "CS-01", CustoEstimadoPecaCentavos: 2500));
        await s.RegistrarPerdaAsync(new DadosPerda(MotivoPerda.DefeitoNoTecido, 2.5m, MateriaPrimaId: materia.Id, OrdemProducaoId: ordem.Id));

        var fila = Fila();
        Assert.Contains(fila, f => f.Entidade == NomesEntidade.MateriaPrima && f.Guid == materia.Guid);
        Assert.Contains(fila, f => f.Entidade == NomesEntidade.OrdemProducao && f.Guid == ordem.Guid);
        var perdaGuid = fila.Single(f => f.Entidade == NomesEntidade.PerdaProducao).Guid;

        var dMateria = await Documento(NomesEntidade.MateriaPrima, materia.Guid);
        Assert.Equal("materias_primas", dMateria.Colecao);
        Assert.Equal("Malha PV", dMateria.Campos["nome"]);
        Assert.Equal(27.5m, dMateria.Campos["estoqueAtual"]);          // 30 − 2,5 da perda

        var dOrdem = await Documento(NomesEntidade.OrdemProducao, ordem.Guid);
        Assert.Equal("ordens_producao", dOrdem.Colecao);
        Assert.Equal("Planejamento", dOrdem.Campos["status"]);
        Assert.Equal(2, ((IEnumerable<object?>)dOrdem.Campos["grade"]!).Count());
        Assert.Equal(DateTimeKind.Utc, ((DateTime)dOrdem.Campos["atualizadoEm"]!).Kind);

        var dPerda = await Documento(NomesEntidade.PerdaProducao, perdaGuid);
        Assert.Equal("perdas_producao", dPerda.Colecao);
        Assert.Equal(ordem.Guid, dPerda.Campos["ordemGuid"]);
        Assert.Equal(materia.Guid, dPerda.Campos["materiaGuid"]);
        Assert.Equal("DefeitoNoTecido", dPerda.Campos["motivo"]);
    }

    [Fact]
    public async Task Avancar_etapa_e_mexer_na_grade_reenfileiram_a_ordem()
    {
        var s = Servico();
        var ordem = await s.CriarOrdemAsync(new DadosOrdemProducao("Boné", new Dictionary<string, int> { ["U"] = 5 }));
        var antes = Fila().Single(f => f.Entidade == NomesEntidade.OrdemProducao);
        int Versao() { using var db = _banco.CriarContexto(); return db.FilaSincronizacao.Single(f => f.EntidadeGuid == ordem.Guid).Versao; }
        var v1 = Versao();

        await s.AvancarEtapaAsync(ordem.Id);

        Assert.Equal(antes.Guid, ordem.Guid);
        Assert.True(Versao() > v1);
        var d = await Documento(NomesEntidade.OrdemProducao, ordem.Guid);
        Assert.Equal("Corte", d.Campos["status"]);
    }
}
