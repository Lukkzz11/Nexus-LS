using System.IO;
using NexusLS.Setup;

namespace NexusLS.App.Tests;

public class SetupTests
{
    [Fact]
    public void Linha_de_comando_le_silencioso_empresa_e_pasta()
    {
        var op = OpcoesSetup.Ler(["/S", "--empresa=arena-sandplay", "--pasta=D:/Nexus"]);
        Assert.True(op.Silencioso);
        Assert.Equal("arena-sandplay", op.Empresa);
        Assert.Equal("D:/Nexus", op.Pasta);
        Assert.False(OpcoesSetup.Ler([]).Silencioso);
        Assert.EndsWith("Nexus", OpcoesSetup.Ler([]).Pasta);
    }

    [Fact]
    public void Loja_online_e_opcional_e_so_entra_com_com_loja()
    {
        var padrao = new Instalador(OpcoesSetup.Ler([])).ObterProgramasSelecionados();
        Assert.DoesNotContain("NexusLojaOnline.exe", padrao);
        Assert.Contains("NexusBarTender.exe", padrao);

        var comLoja = new Instalador(OpcoesSetup.Ler(["--com-loja"])).ObterProgramasSelecionados();
        Assert.Contains("NexusLojaOnline.exe", comLoja);
    }

    [Fact]
    public void Alinhavo_vem_marcado_ganha_atalho_e_sai_com_sem_alinhavo()
    {
        var padrao = new Instalador(OpcoesSetup.Ler([]));
        Assert.Contains(Instalador.ExeAlinhavo, padrao.ObterProgramasComAtalho());
        // o Alinhavo nao e baixado da nuvem: nao entra na lista de executaveis a baixar
        Assert.DoesNotContain(Instalador.ExeAlinhavo, padrao.ObterProgramasSelecionados());

        var sem = new Instalador(OpcoesSetup.Ler(["--sem-alinhavo"]));
        Assert.DoesNotContain(Instalador.ExeAlinhavo, sem.ObterProgramasComAtalho());
    }

    [Fact]
    public void Pacote_do_alinhavo_e_extraido_com_exe_manequim_e_versao()
    {
        var temp = Path.Combine(Path.GetTempPath(), "alinhavo-teste-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (nome, texto) in new[] { ("alinhavo.exe", "exe"), ("assets/manequim/m.glb", "glb"), ("versao.txt", "0.1.0\n") })
                {
                    using var w = new StreamWriter(zip.CreateEntry(nome).Open());
                    w.Write(texto);
                }
            }
            ms.Position = 0;
            Instalador.ExtrairPacoteAlinhavo(ms, temp);

            Assert.True(File.Exists(Path.Combine(temp, "alinhavo.exe")));
            Assert.True(File.Exists(Path.Combine(temp, "assets", "manequim", "m.glb")));
            Assert.Equal("0.1.0", File.ReadAllText(Path.Combine(temp, "versao.txt")).Trim());
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    [Fact]
    public void Pacote_do_alinhavo_sem_exe_e_recusado()
    {
        var temp = Path.Combine(Path.GetTempPath(), "alinhavo-teste-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using var w = new StreamWriter(zip.CreateEntry("versao.txt").Open());
                w.Write("0.1.0");
            }
            ms.Position = 0;
            Assert.Throws<InvalidDataException>(() => Instalador.ExtrairPacoteAlinhavo(ms, temp));
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    [Fact]
    public void Desinstalador_para_o_processo_e_remove_o_atalho_do_alinhavo()
    {
        var ps = Instalador.DesinstaladorPs1(@"C:\x");
        Assert.Contains("alinhavo", ps);
        Assert.Contains("'Alinhavo'", ps);
    }

    [Theory]
    [InlineData("lifesurf-principal", "lifesurf-principal")]
    [InlineData("a\",\"x\":\"b", null)]
    [InlineData("../x", null)]
    [InlineData("", null)]
    public void Id_da_empresa_so_aceita_caracteres_seguros(string entrada, string? esperado) =>
        Assert.Equal(esperado, OpcoesSetup.SomenteIdValido(entrada));

    [Fact]
    public void Hash_confere_com_o_formato_do_arquivo_sha256()
    {
        var h = new string('a', 64);
        Assert.True(Instalador.HashConfere(h + "  NexusLS.exe\n", h.ToUpperInvariant()));
        Assert.False(Instalador.HashConfere(h + "  NexusLS.exe", new string('b', 64)));
        Assert.False(Instalador.HashConfere("", h));
    }

    [Fact]
    public void Conexao_com_o_erp_gera_json_valido_com_a_empresa()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Instalador.ConteudoErpJson("arena-sandplay"));
        Assert.Equal("arena-sandplay", doc.RootElement.GetProperty("EmpresaId").GetString());
        Assert.Equal("pdv---lifesurf", doc.RootElement.GetProperty("ProjectId").GetString());
    }
}
