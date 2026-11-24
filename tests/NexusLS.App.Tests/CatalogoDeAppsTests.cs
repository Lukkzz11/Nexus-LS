using System.IO;
using System.IO.Compression;
using NexusLS.Core.Atualizacao;

namespace NexusLS.App.Tests;

public class CatalogoDeAppsTests
{
    private const string Programa = """{"id":"order-separator","nome":"Order Separator","versao":"1.0.0","pacote":"OrderSeparator.zip","exe":"Order Separator.exe"}""";

    private static IReadOnlyList<AppDoCatalogo> Ler(params string[] apps) =>
        CatalogoDeApps.Ler("{\"apps\":[" + string.Join(",", apps) + "]}");

    [Fact]
    public void Le_programa_e_sistema_web()
    {
        var apps = Ler(Programa, """{"id":"erp-web","nome":"ERP Web","cor":"#112233","url":"https://exemplo.com/erp"}""");

        Assert.Equal(2, apps.Count);
        Assert.False(apps[0].EhWeb);
        Assert.Equal(new VersaoPrograma(1, 0, 0), apps[0].Versao);
        Assert.True(apps[1].EhWeb);
        Assert.Equal("#112233", apps[1].Cor);
    }

    [Theory]
    [InlineData("""{"id":"..","nome":"x","url":"https://a.com"}""")]
    [InlineData("""{"id":"alinhavo","nome":"x","url":"https://a.com"}""")]
    [InlineData("""{"id":"abc","nome":"","url":"https://a.com"}""")]
    [InlineData("""{"id":"abc","nome":"x","url":"http://a.com"}""")]
    [InlineData("""{"id":"abc","nome":"x","url":"file:///c:/windows/notepad.exe"}""")]
    [InlineData("""{"id":"abc","nome":"x","versao":"1.0.0","pacote":"a.zip","exe":"..\\a.exe"}""")]
    [InlineData("""{"id":"abc","nome":"x","versao":"1.0.0","pacote":"a.zip","exe":"a.bat"}""")]
    [InlineData("""{"id":"abc","nome":"x","versao":"?","pacote":"a.zip","exe":"a.exe"}""")]
    public void Entrada_incompleta_ou_perigosa_e_ignorada_sem_derrubar_as_outras(string ruim)
    {
        var apps = Ler(ruim, Programa);
        Assert.Equal("order-separator", Assert.Single(apps).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("não é json")]
    [InlineData("[]")]
    [InlineData("""{"apps":[]}""")]
    public void Catalogo_vazio_ou_invalido_vira_lista_vazia(string? json) => Assert.Empty(CatalogoDeApps.Ler(json));

    [Fact]
    public void Id_repetido_entra_uma_vez_so() => Assert.Single(Ler(Programa, Programa));

    [Fact]
    public void Instala_zip_de_um_app_do_catalogo_e_cobra_o_exe_certo()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "catalogo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var zip = Path.Combine(pasta, "a.zip");
            using (var arquivo = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(arquivo.CreateEntry("Meu App.exe").Open())) w.Write("exe");

            var destino = Path.Combine(pasta, "meu-app");
            InstalacaoAlinhavo.Instalar(zip, destino, "Meu App", "Meu App.exe");
            Assert.True(File.Exists(Path.Combine(destino, "Meu App.exe")));

            var erro = Assert.Throws<RegraDeAtualizacaoException>(() => InstalacaoAlinhavo.Instalar(zip, destino, "Meu App", "outro.exe"));
            Assert.Contains("Meu App", erro.Message);
            Assert.True(File.Exists(Path.Combine(destino, "Meu App.exe")));
        }
        finally { Directory.Delete(pasta, true); }
    }
}
