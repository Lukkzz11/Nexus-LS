using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests.Gestao;

public class LancamentoEmpresaTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 19, 10, 0, 0);
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true);
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-lanc-" + Guid.NewGuid().ToString("N"));
    private readonly LancamentoEmpresaService _servico;
    private readonly AnexoService _anexos;

    public LancamentoEmpresaTests()
    {
        Directory.CreateDirectory(_pasta);
        _anexos = new AnexoService(_banco.Fabrica, Path.Combine(_pasta, "anexos"));
        _servico = new LancamentoEmpresaService(_banco.Fabrica, _anexos);
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private static DadosLancamentoEmpresa Dados(string desc = "Conta de luz", long valor = 15000, TipoMovimentoEmpresa tipo = TipoMovimentoEmpresa.Saida,
        string categoria = "Energia", DateTime? data = null) => new(data ?? Agora, tipo, categoria, desc, valor, FormaPagamento.Pix, "obs", "Ana");

    private string ArquivoPdf(string nome = "nota.pdf")
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, "%PDF-1.4 conteudo"u8.ToArray());
        return caminho;
    }

    [Fact]
    public async Task Cria_e_obtem_com_os_campos()
    {
        var l = await _servico.CriarAsync(Dados(), Agora);
        var lido = await _servico.ObterAsync(l.Id);
        Assert.NotNull(lido);
        Assert.Equal("Conta de luz", lido!.Descricao);
        Assert.Equal(15000, lido.ValorCentavos);
        Assert.Equal(TipoMovimentoEmpresa.Saida, lido.Tipo);
        Assert.Equal(FormaPagamento.Pix, lido.Forma);
        Assert.Equal(new DateTime(2026, 11, 19), lido.Data);
        Assert.NotEqual(Guid.Empty, lido.Guid);
    }

    [Theory]
    [InlineData("ab", 100, "Energia", "Descreva")]
    [InlineData("Conta", 0, "Energia", "valor")]
    [InlineData("Conta", -5, "Energia", "valor")]
    [InlineData("Conta", 100, "  ", "categoria")]
    public async Task Recusa_dados_invalidos(string desc, long valor, string categoria, string trecho)
    {
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CriarAsync(Dados(desc, valor, categoria: categoria), Agora));
        Assert.Contains(trecho, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recusa_data_no_futuro_forma_vale_troca_e_tipo_invalido()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CriarAsync(Dados(data: Agora.AddDays(5)), Agora));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _servico.CriarAsync(Dados() with { Forma = FormaPagamento.ValeTroca }, Agora));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _servico.CriarAsync(Dados() with { Tipo = (TipoMovimentoEmpresa)9 }, Agora));
    }

    [Fact]
    public async Task Edita_e_mantem_quem_criou()
    {
        var l = await _servico.CriarAsync(Dados(), Agora);
        await _servico.EditarAsync(l.Id, Dados("Conta de luz maio", 20000) with { Operador = "Outro" }, Agora.AddHours(1));
        var lido = (await _servico.ObterAsync(l.Id))!;
        Assert.Equal("Conta de luz maio", lido.Descricao);
        Assert.Equal(20000, lido.ValorCentavos);
        Assert.Equal("Ana", lido.Operador);
        Assert.Equal(Agora.AddHours(1), lido.AlteradoEm);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.EditarAsync(999, Dados(), Agora));
    }

    [Fact]
    public async Task Filtra_por_periodo_tipo_categoria_e_anexo()
    {
        var a = await _servico.CriarAsync(Dados("Luz", 100, categoria: "Energia", data: new DateTime(2026, 11, 1)), Agora);
        await _servico.CriarAsync(Dados("Venda avulsa", 500, TipoMovimentoEmpresa.Entrada, "Outros", new DateTime(2026, 11, 10)), Agora);
        await _servico.CriarAsync(Dados("Aluguel", 900, categoria: "Aluguel", data: new DateTime(2026, 10, 30)), Agora);
        await _anexos.AdicionarAsync(a.Id, ArquivoPdf(), Agora);

        Assert.Equal(3, (await _servico.ListarAsync()).Count);
        Assert.Equal(2, (await _servico.ListarAsync(new FiltroLancamentoEmpresa(De: new DateTime(2026, 11, 1), Ate: new DateTime(2026, 11, 30)))).Count);
        Assert.Single(await _servico.ListarAsync(new FiltroLancamentoEmpresa(Tipo: TipoMovimentoEmpresa.Entrada)));
        Assert.Single(await _servico.ListarAsync(new FiltroLancamentoEmpresa(Categoria: "ENERGIA")));
        Assert.Single(await _servico.ListarAsync(new FiltroLancamentoEmpresa(ComAnexo: true)));
        Assert.Equal(2, (await _servico.ListarAsync(new FiltroLancamentoEmpresa(ComAnexo: false))).Count);
        Assert.Single(await _servico.ListarAsync(new FiltroLancamentoEmpresa(Texto: "alugue")));
        // Limites do período são inclusivos (mesmo com hora na data final).
        Assert.Single(await _servico.ListarAsync(new FiltroLancamentoEmpresa(De: new DateTime(2026, 11, 10), Ate: new DateTime(2026, 11, 10, 23, 59, 0))));
        // Mais recentes primeiro.
        Assert.Equal("Venda avulsa", (await _servico.ListarAsync())[0].Descricao);
    }

    [Fact]
    public async Task Totais_somam_entradas_saidas_e_saldo()
    {
        await _servico.CriarAsync(Dados("Entrada", 1000, TipoMovimentoEmpresa.Entrada, "Outros"), Agora);
        await _servico.CriarAsync(Dados("Saída A", 300), Agora);
        await _servico.CriarAsync(Dados("Saída B", 200), Agora);
        var (e, s, saldo) = LancamentoEmpresaService.Totais(await _servico.ListarAsync());
        Assert.Equal((1000L, 500L, 500L), (e, s, saldo));
        Assert.Equal(new[] { "Energia", "Outros" }, await _servico.CategoriasAsync());
    }

    [Fact]
    public async Task Excluir_com_anexo_exige_confirmacao_e_depois_apaga_arquivo()
    {
        var l = await _servico.CriarAsync(Dados(), Agora);
        var anexo = await _anexos.AdicionarAsync(l.Id, ArquivoPdf(), Agora);
        var arquivo = _anexos.CaminhoCompleto(anexo.CaminhoRelativo);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.ExcluirAsync(l.Id));
        Assert.NotNull(await _servico.ObterAsync(l.Id));
        Assert.True(File.Exists(arquivo));

        await _servico.ExcluirAsync(l.Id, removerAnexos: true);
        Assert.Null(await _servico.ObterAsync(l.Id));
        Assert.False(File.Exists(arquivo));
        await using var db = _banco.CriarContexto();
        Assert.Empty(db.AnexosLancamento);
    }

    [Fact]
    public async Task Excluir_sem_anexo_funciona_direto()
    {
        var l = await _servico.CriarAsync(Dados(), Agora);
        await _servico.ExcluirAsync(l.Id);
        Assert.Null(await _servico.ObterAsync(l.Id));
    }

    [Fact]
    public async Task E_somente_local_nao_e_sincronizavel_nem_entra_na_fila()
    {
        Assert.False(typeof(IEntidadeSincronizavel).IsAssignableFrom(typeof(LancamentoEmpresa)));
        Assert.False(typeof(IEntidadeSincronizavel).IsAssignableFrom(typeof(AnexoLancamento)));
        Assert.DoesNotContain(typeof(LancamentoEmpresa).GetProperties(), p => p.Name.Contains("Erp", StringComparison.OrdinalIgnoreCase));

        var l = await _servico.CriarAsync(Dados(), Agora);
        await _anexos.AdicionarAsync(l.Id, ArquivoPdf(), Agora);
        await _servico.EditarAsync(l.Id, Dados("Outra descrição"), Agora);
        await using var db = _banco.CriarContexto();
        Assert.Empty(db.FilaSincronizacao);
    }
}
