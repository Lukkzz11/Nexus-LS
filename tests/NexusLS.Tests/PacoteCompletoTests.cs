using Microsoft.Data.Sqlite;
using NexusLS.Core.Backup;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class PacoteCompletoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nx-pacote-" + Guid.NewGuid().ToString("N"));

    public PacoteCompletoTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_pasta, true); } catch { }
    }

    private string CriarInstalacao(string nome, string marca)
    {
        var raiz = Path.Combine(_pasta, nome);
        Directory.CreateDirectory(Path.Combine(raiz, "anexos", "gaveta"));
        Directory.CreateDirectory(Path.Combine(raiz, "logs"));
        Directory.CreateDirectory(Path.Combine(raiz, "seguranca"));
        var db = Path.Combine(raiz, "nexus.db");
        var cs = new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString();
        using (var con = new SqliteConnection(cs))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE Produtos(Id INTEGER); CREATE TABLE Vendas(Id INTEGER); CREATE TABLE __EFMigrationsHistory(Id INTEGER);" +
                              $"CREATE TABLE Marca(Texto TEXT); INSERT INTO Marca VALUES('{marca}')";
            cmd.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(raiz, "firebase.json"), "chave-" + marca);
        File.WriteAllText(Path.Combine(raiz, "seguranca", "config.json"), "seg-" + marca);
        File.WriteAllText(Path.Combine(raiz, "anexos", "gaveta", "nota.pdf"), "pdf-" + marca);
        File.WriteAllText(Path.Combine(raiz, "logs", "log.txt"), "lixo");
        return db;
    }

    private static string LerMarca(string db)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = db, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var con = new SqliteConnection(cs);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Texto FROM Marca";
        return (string)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Cria_e_restaura_em_outra_instalacao_com_tudo()
    {
        var origem = CriarInstalacao("origem", "A");
        var arquivo = Path.Combine(_pasta, "saida", PacoteCompleto.NomeSugerido(DateTime.Now));
        new PacoteCompleto(origem).Criar(arquivo, "senha-forte");

        var novo = CriarInstalacao("novo", "B"); // PC formatado, já com instalação nova
        var seguranca = new PacoteCompleto(novo).Restaurar(arquivo, "senha-forte");

        var raiz = Path.GetDirectoryName(novo)!;
        Assert.Equal("A", LerMarca(novo));
        Assert.Equal("chave-A", File.ReadAllText(Path.Combine(raiz, "firebase.json")));
        Assert.Equal("seg-A", File.ReadAllText(Path.Combine(raiz, "seguranca", "config.json")));
        Assert.Equal("pdf-A", File.ReadAllText(Path.Combine(raiz, "anexos", "gaveta", "nota.pdf")));
        Assert.NotNull(seguranca);
        Assert.Equal("B", LerMarca(Path.Combine(seguranca!, "nexus.db"))); // o que havia antes foi guardado
    }

    [Fact]
    public void Nao_leva_logs_nem_o_proprio_pacote()
    {
        var origem = CriarInstalacao("origem", "A");
        var arquivo = Path.Combine(Path.GetDirectoryName(origem)!, PacoteCompleto.NomeSugerido(DateTime.Now));
        new PacoteCompleto(origem).Criar(arquivo, "senha-forte");
        var novo = CriarInstalacao("novo", "B");
        File.Delete(Path.Combine(Path.GetDirectoryName(novo)!, "logs", "log.txt"));
        new PacoteCompleto(novo).Restaurar(arquivo, "senha-forte");
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(novo)!, "logs", "log.txt")));
    }

    [Fact]
    public void Senha_errada_nao_restaura_e_nada_muda()
    {
        var origem = CriarInstalacao("origem", "A");
        var arquivo = Path.Combine(_pasta, "p" + PacoteCompleto.Extensao);
        new PacoteCompleto(origem).Criar(arquivo, "senha-forte");
        var novo = CriarInstalacao("novo", "B");
        var ex = Assert.Throws<RegraDeNegocioException>(() => new PacoteCompleto(novo).Restaurar(arquivo, "outra-senha"));
        Assert.Contains("Senha incorreta", ex.Message);
        Assert.Equal("B", LerMarca(novo));
    }

    [Fact]
    public void Arquivo_cortado_e_recusado()
    {
        var origem = CriarInstalacao("origem", "A");
        var arquivo = Path.Combine(_pasta, "p" + PacoteCompleto.Extensao);
        new PacoteCompleto(origem).Criar(arquivo, "senha-forte");
        var bytes = File.ReadAllBytes(arquivo);
        File.WriteAllBytes(arquivo, bytes[..(bytes.Length - 30)]);
        Assert.Throws<RegraDeNegocioException>(() => PacoteCompleto.LerResumo(arquivo, "senha-forte"));
    }

    [Fact]
    public void Senha_curta_e_recusada()
    {
        var origem = CriarInstalacao("origem", "A");
        Assert.Throws<RegraDeNegocioException>(() => new PacoteCompleto(origem).Criar(Path.Combine(_pasta, "x.nexusbackup"), "123"));
    }

    [Fact]
    public void Arquivo_comum_nao_e_aceito()
    {
        var lixo = Path.Combine(_pasta, "lixo.nexusbackup");
        File.WriteAllText(lixo, "isto nao e um backup, so um texto longo o bastante");
        Assert.Throws<RegraDeNegocioException>(() => PacoteCompleto.LerResumo(lixo, "senha-forte"));
    }

    [Fact]
    public void Arquivo_grande_com_varios_blocos_volta_igual()
    {
        var origem = CriarInstalacao("origem", "A");
        var grande = new byte[2_500_000];
        new Random(1).NextBytes(grande);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(origem)!, "anexos", "gaveta", "grande.bin"), grande);
        var arquivo = Path.Combine(_pasta, "g" + PacoteCompleto.Extensao);
        new PacoteCompleto(origem).Criar(arquivo, "senha-forte");
        var novo = CriarInstalacao("novo", "B");
        new PacoteCompleto(novo).Restaurar(arquivo, "senha-forte");
        Assert.Equal(grande, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(novo)!, "anexos", "gaveta", "grande.bin")));
    }
}
