using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Backup;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests.Gestao;

public class BackupAnexosTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-bkanexo-" + Guid.NewGuid().ToString("N"));
    private readonly string _banco;
    private readonly string _backups;
    private readonly string _anexos;

    public BackupAnexosTests()
    {
        Directory.CreateDirectory(_pasta);
        _banco = Path.Combine(_pasta, "nexus.db");
        _backups = Path.Combine(_pasta, "backups");
        _anexos = Path.Combine(_pasta, "anexos");
        using var db = Abrir();
        db.Database.Migrate();
    }

    private PdvDbContext Abrir() => new(new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={_banco}").Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private async Task<(AnexoLancamento A, byte[] Bytes)> NovoLancamentoComAnexo(string nome = "nota.pdf")
    {
        var fabrica = new FabricaTeste(Abrir);
        var anexos = new AnexoService(fabrica, _anexos);
        var lancs = new LancamentoEmpresaService(fabrica, anexos);
        var l = await lancs.CriarAsync(new DadosLancamentoEmpresa(new DateTime(2026, 11, 1), TipoMovimentoEmpresa.Saida, "Compras", "Nota " + nome, 1000), new DateTime(2026, 11, 2));
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 " + Guid.NewGuid());
        var origem = Path.Combine(_pasta, nome);
        File.WriteAllBytes(origem, bytes);
        return (await anexos.AdicionarAsync(l.Id, origem, new DateTime(2026, 11, 2)), bytes);
    }

    private sealed class FabricaTeste : IDbContextFactory<PdvDbContext>
    {
        private readonly Func<PdvDbContext> _f;
        public FabricaTeste(Func<PdvDbContext> f) => _f = f;
        public PdvDbContext CreateDbContext() => _f();
    }

    [Fact]
    public async Task Backup_inclui_a_pasta_de_anexos_e_a_restauracao_devolve_os_arquivos()
    {
        var (anexo, bytes) = await NovoLancamentoComAnexo();
        var servico = new BackupService(_banco); // pasta anexos padrão: ao lado do banco

        var info = servico.Fazer(_backups, MotivoBackup.Manual);
        var zip = BackupService.CaminhoAnexosDoBackup(info.Caminho);
        Assert.True(File.Exists(zip));
        Assert.Equal((true, "ok"), BackupService.Conferir(info.Caminho));
        using (var z = ZipFile.OpenRead(zip)) Assert.Contains(z.Entries, e => e.FullName == anexo.CaminhoRelativo);

        // Desastre: apaga os anexos e muda o banco.
        Directory.Delete(_anexos, true);
        await using (var db = Abrir())
        {
            db.AnexosLancamento.RemoveRange(db.AnexosLancamento);
            db.LancamentosEmpresa.RemoveRange(db.LancamentosEmpresa);
            await db.SaveChangesAsync();
        }
        SqliteConnection.ClearAllPools();

        servico.Restaurar(info.Caminho, _backups);

        await using var depois = Abrir();
        var restaurado = await depois.AnexosLancamento.SingleAsync();
        Assert.Equal(anexo.Sha256, restaurado.Sha256);
        var arquivo = new AnexoService(new FabricaTeste(Abrir), _anexos).CaminhoCompleto(restaurado.CaminhoRelativo);
        Assert.Equal(bytes, File.ReadAllBytes(arquivo));
        Assert.False(Directory.Exists(_anexos + ".restaurando"));
        Assert.False(Directory.Exists(_anexos + ".antes"));
        // A restauração também guardou um backup de segurança, com os anexos que existiam na hora.
        Assert.Contains(servico.Listar(_backups), b => b.Motivo == MotivoBackup.PreRestauracao);
    }

    [Fact]
    public async Task Restaurar_troca_a_pasta_inteira_sem_sobras_do_estado_atual()
    {
        var (a1, _) = await NovoLancamentoComAnexo("um.pdf");
        var servico = new BackupService(_banco);
        var info = servico.Fazer(_backups, MotivoBackup.Manual);

        var (a2, _) = await NovoLancamentoComAnexo("dois.pdf"); // depois do backup
        servico.Restaurar(info.Caminho, _backups);

        Assert.True(File.Exists(Path.Combine(_anexos, a1.CaminhoRelativo.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(_anexos, a2.CaminhoRelativo.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void Sem_anexos_nao_gera_zip_e_backup_antigo_nao_mexe_na_pasta()
    {
        var servico = new BackupService(_banco);
        var info = servico.Fazer(_backups, MotivoBackup.Manual);
        Assert.False(File.Exists(BackupService.CaminhoAnexosDoBackup(info.Caminho)));

        Directory.CreateDirectory(_anexos);
        File.WriteAllText(Path.Combine(_anexos, "solto.txt"), "x");
        servico.Restaurar(info.Caminho, _backups);
        Assert.True(File.Exists(Path.Combine(_anexos, "solto.txt")));
    }

    [Fact]
    public async Task Retencao_apaga_tambem_o_zip_de_anexos_e_listar_ignora_o_zip()
    {
        await NovoLancamentoComAnexo();
        var servico = new BackupService(_banco);
        var antigo = servico.Fazer(_backups, MotivoBackup.Automatico, agora: new DateTime(2026, 10, 1, 8, 0, 0));
        servico.Fazer(_backups, MotivoBackup.Automatico, agora: new DateTime(2026, 10, 2, 8, 0, 0));
        Assert.Equal(2, servico.Listar(_backups).Count);

        Assert.Equal(1, servico.AplicarRetencao(_backups, 1));
        Assert.False(File.Exists(antigo.Caminho));
        Assert.False(File.Exists(BackupService.CaminhoAnexosDoBackup(antigo.Caminho)));
        Assert.Equal(1, Directory.GetFiles(_backups, "*" + BackupService.SufixoAnexos).Length);
    }

    [Fact]
    public async Task Zip_com_caminho_fora_da_pasta_e_recusado_e_nada_muda()
    {
        await NovoLancamentoComAnexo();
        var servico = new BackupService(_banco);
        var info = servico.Fazer(_backups, MotivoBackup.Manual);
        var zip = BackupService.CaminhoAnexosDoBackup(info.Caminho);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            var e = z.CreateEntry("../fora.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("malicioso");
        }
        var antes = Directory.GetFiles(_anexos, "*", SearchOption.AllDirectories).Length;

        var ex = Assert.Throws<RegraDeNegocioException>(() => servico.Restaurar(info.Caminho, _backups));

        Assert.Contains("banco atual foi mantido", ex.Message);
        Assert.False(File.Exists(Path.Combine(_pasta, "fora.txt")));
        Assert.Equal(antes, Directory.GetFiles(_anexos, "*", SearchOption.AllDirectories).Length);
        Assert.False(Directory.Exists(_anexos + ".restaurando"));
    }
}
