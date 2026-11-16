using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Data;

namespace NexusLS.App.Tests;

/// <summary>Usa o gerenciador estático de etiquetas: roda isolado para não trocar o banco de outro teste.</summary>
[Collection("GerenciadorEtiquetas")]
public class EtiquetasProdutoBancoTests : IDisposable
{
    private readonly string _caminho = Path.Combine(Path.GetTempPath(), $"nexus_etq_{Guid.NewGuid():N}.db");
    private readonly BancoDadosService? _bancoAnterior;

    public EtiquetasProdutoBancoTests()
    {
        _bancoAnterior = GerenciadorEtiquetasProdutoService.Banco;

        var opcoes = new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={_caminho}").Options;
        using (var db = new PdvDbContext(opcoes)) db.Database.EnsureCreated();

        GerenciadorEtiquetasProdutoService.Banco = new BancoDadosService(_caminho);
        GerenciadorEtiquetasProdutoService.Invalidar();
    }

    public void Dispose()
    {
        GerenciadorEtiquetasProdutoService.Banco = _bancoAnterior;
        GerenciadorEtiquetasProdutoService.Invalidar();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _caminho, _caminho + "-wal", _caminho + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void Etiqueta_do_produto_e_gravada_no_banco_e_lida_de_volta()
    {
        const int produto = 987001;
        var modelo = ModeloEtiquetaDesign.CriarPadrao50x30();
        modelo.Nome = "Meu modelo";

        Assert.False(GerenciadorEtiquetasProdutoService.TemEtiquetaPersonalizada(produto));

        Assert.True(GerenciadorEtiquetasProdutoService.SalvarEtiquetaProduto(produto, modelo));

        Assert.True(GerenciadorEtiquetasProdutoService.TemEtiquetaPersonalizada(produto));
        var (tem, nome, medidas) = GerenciadorEtiquetasProdutoService.ObterInfoEtiqueta(produto);
        Assert.True(tem);
        Assert.Equal("Meu modelo", nome);
        Assert.Equal("50 × 30 mm", medidas);

        var lido = GerenciadorEtiquetasProdutoService.CarregarEtiquetaProduto(produto);
        Assert.NotNull(lido);
        Assert.Equal("Meu modelo", lido!.Nome);
        Assert.Equal(modelo.Elementos.Count, lido.Elementos.Count);

        // Está de fato na tabela do banco (por isso entra no backup do Nexus).
        using var db = GerenciadorEtiquetasProdutoService.Banco!.CriarContexto();
        Assert.Single(db.EtiquetasProduto.Where(e => e.ProdutoId == produto));
    }

    [Fact]
    public void Salvar_de_novo_atualiza_a_mesma_linha_e_remover_apaga()
    {
        const int produto = 987002;
        var modelo = ModeloEtiquetaDesign.CriarPadrao50x30();

        modelo.Nome = "Primeira";
        Assert.True(GerenciadorEtiquetasProdutoService.SalvarEtiquetaProduto(produto, modelo));
        modelo.Nome = "Segunda";
        Assert.True(GerenciadorEtiquetasProdutoService.SalvarEtiquetaProduto(produto, modelo));

        using (var db = GerenciadorEtiquetasProdutoService.Banco!.CriarContexto())
            Assert.Equal(1, db.EtiquetasProduto.Count(e => e.ProdutoId == produto));
        Assert.Equal("Segunda", GerenciadorEtiquetasProdutoService.ObterInfoEtiqueta(produto).NomeModelo);

        Assert.True(GerenciadorEtiquetasProdutoService.RemoverEtiquetaProduto(produto));
        Assert.False(GerenciadorEtiquetasProdutoService.TemEtiquetaPersonalizada(produto));
        Assert.Null(GerenciadorEtiquetasProdutoService.CarregarEtiquetaProduto(produto));
    }
}
