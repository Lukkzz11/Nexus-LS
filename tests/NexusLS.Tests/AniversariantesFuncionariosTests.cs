using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

/// <summary>Aviso de aniversariantes dos funcionários ao abrir, e a exportação da lista para CSV.</summary>
public class AniversariantesFuncionariosTests
{
    private static Funcionario F(string nome, DateTime? nasc, bool ativo = true) => new() { Nome = nome, DataNascimento = nasc, Ativo = ativo };

    [Fact]
    public void Aniversario_de_hoje_e_dos_proximos_sete_dias_entram_e_o_oitavo_dia_nao()
    {
        var hoje = new DateTime(2026, 11, 18);
        var lista = AvisosAoAbrir.AniversariantesProximos(new[]
        {
            F("Hoje", new DateTime(1990, 11, 18)), F("Sete", new DateTime(1985, 11, 25)), F("Oito", new DateTime(1985, 11, 26)),
            F("Ontem", new DateTime(1985, 11, 17))
        }, hoje);
        Assert.Equal(new[] { "Hoje", "Sete" }, lista.Select(a => a.Nome));
        Assert.Equal(36, lista[0].Idade);
        Assert.Equal(0, lista[0].DiasAte);
        Assert.Equal(7, lista[1].DiasAte);
    }

    [Fact]
    public void Virada_do_ano_olha_para_janeiro()
    {
        var lista = AvisosAoAbrir.AniversariantesProximos(new[] { F("Ana", new DateTime(2000, 1, 3)), F("Beto", new DateTime(2000, 12, 31)) }, new DateTime(2026, 12, 30));
        Assert.Equal(new[] { "Beto", "Ana" }, lista.Select(a => a.Nome));
        Assert.Equal(new DateTime(2027, 1, 3), lista[1].Data);
        Assert.Equal(27, lista[1].Idade);
        Assert.Equal(4, lista[1].DiasAte);
    }

    [Fact]
    public void Nascido_em_29_de_fevereiro_comemora_em_28_nos_anos_nao_bissextos_e_em_29_nos_bissextos()
    {
        var f = F("Bissexto", new DateTime(2000, 2, 29));
        var naoBissexto = AvisosAoAbrir.AniversariantesProximos(new[] { f }, new DateTime(2027, 2, 25));
        Assert.Equal(new DateTime(2027, 2, 28), Assert.Single(naoBissexto).Data);
        var bissexto = AvisosAoAbrir.AniversariantesProximos(new[] { f }, new DateTime(2028, 2, 27));
        Assert.Equal(new DateTime(2028, 2, 29), Assert.Single(bissexto).Data);
        Assert.Equal(28, bissexto[0].Idade);
    }

    [Fact]
    public void Inativo_sem_data_e_nascimento_no_futuro_ficam_de_fora()
    {
        var hoje = new DateTime(2026, 11, 18);
        Assert.Empty(AvisosAoAbrir.AniversariantesProximos(new[]
        {
            F("Inativo", hoje, ativo: false), F("SemData", null), F("Futuro", new DateTime(2027, 11, 18))
        }, hoje));
        Assert.Empty(AvisosAoAbrir.Aniversariantes(Array.Empty<Funcionario>(), hoje));
    }

    [Fact]
    public void Texto_destaca_hoje_e_amanha_e_lista_o_resto_por_data()
    {
        var hoje = new DateTime(2026, 11, 18);
        var linhas = AvisosAoAbrir.Aniversariantes(new[]
        {
            F("Ana", new DateTime(1990, 11, 18)), F("Bia", new DateTime(1990, 11, 19)), F("Caio", new DateTime(1990, 11, 22))
        }, hoje);
        Assert.Contains("hoje", linhas[0]);
        Assert.Contains("Ana, hoje (36 anos)", linhas[1]);
        Assert.Contains("Bia, amanhã (36 anos)", linhas[2]);
        Assert.Contains("Caio, 22/11 (36 anos)", linhas[3]);

        var semana = AvisosAoAbrir.Aniversariantes(new[] { F("Caio", new DateTime(1990, 11, 22)) }, hoje);
        Assert.Equal("Aniversariantes da semana:", semana[0]);
    }

    [Fact]
    public void Configuracao_nova_avisa_aniversariantes_por_padrao_e_o_valor_sobrevive_ao_salvar()
    {
        Assert.True(new ConfiguracaoSeguranca().AvisarAniversariantes);
        var caminho = Path.Combine(Path.GetTempPath(), "nexus-aniv-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ConfiguracaoSegurancaStore(caminho);
            store.Salvar(new ConfiguracaoSeguranca { AvisarAniversariantes = false });
            Assert.False(store.Carregar().AvisarAniversariantes);
        }
        finally { File.Delete(caminho); File.Delete(caminho + ".tmp"); }
    }

    // ---------- CSV ----------

    private static Funcionario Ana() => new()
    {
        Id = 1, Nome = "Ana; \"A\"", Cargo = "Caixa", Perfil = PerfilFuncionario.Vendedor, Telefone = "11999990000", Email = "ana@x.com",
        Cpf = "52998224725", DataNascimento = new DateTime(1995, 3, 10), DataAdmissao = new DateTime(2026, 1, 5), SalarioCentavos = 180050,
        PinHash = "HASH-SECRETO", RecuperacaoHash = "REC-SECRETO", Foto = new byte[] { 1, 2 }, Observacao = "Folga às quartas"
    };

    [Fact]
    public void Csv_sem_salario_nao_tem_coluna_nem_valor_e_nunca_tem_pin_nem_hash()
    {
        var csv = ExportadorCsv.Funcionarios(new[] { Ana() }, incluirSalario: false);
        Assert.DoesNotContain("Salário", csv);
        Assert.DoesNotContain("1800,50", csv);
        Assert.DoesNotContain("HASH-SECRETO", csv);
        Assert.DoesNotContain("REC-SECRETO", csv);
        Assert.DoesNotContain("PIN", csv, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10/03/1995", csv);
        Assert.Contains("529.982.247-25", csv);
        Assert.Contains("\"Ana; \"\"A\"\"\"", csv);
        Assert.Contains("Folga às quartas", csv);
    }

    [Fact]
    public void Csv_com_salario_inclui_a_coluna_mas_continua_sem_hash()
    {
        var csv = ExportadorCsv.Funcionarios(new[] { Ana(), new Funcionario { Id = 2, Nome = "Zé", Ativo = false } }, incluirSalario: true);
        Assert.Contains(";Salário;", csv);
        Assert.Contains("1800,50", csv);
        Assert.Contains("Desativado", csv);
        Assert.DoesNotContain("HASH-SECRETO", csv);
        Assert.Equal(3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Arquivo_salvo_tem_bom_utf8()
    {
        var caminho = Path.Combine(Path.GetTempPath(), "nexus-func-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            ExportadorCsv.Salvar(caminho, ExportadorCsv.Funcionarios(new[] { Ana() }, false));
            var bytes = File.ReadAllBytes(caminho);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }
        finally { File.Delete(caminho); }
    }
}
