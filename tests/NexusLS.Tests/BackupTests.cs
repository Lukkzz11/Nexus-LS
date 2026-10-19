using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Backup;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class RegrasBackupTests
{
    [Fact]
    public void Nome_do_arquivo_tem_data_hora_e_motivo()
    {
        var nome = RegrasBackup.NomeArquivo(new DateTime(2026, 10, 19, 14, 32, 5), MotivoBackup.Automatico);
        Assert.Equal("nexus_2026-10-19_143205_auto.db", nome);
    }

    [Theory]
    [InlineData(MotivoBackup.Manual)]
    [InlineData(MotivoBackup.Automatico)]
    [InlineData(MotivoBackup.PreAtualizacao)]
    [InlineData(MotivoBackup.PreRestauracao)]
    public void Nome_gerado_e_lido_de_volta(MotivoBackup motivo)
    {
        var quando = new DateTime(2026, 1, 2, 3, 4, 5);
        Assert.True(RegrasBackup.TentarLerNome(RegrasBackup.NomeArquivo(quando, motivo), out var data, out var m));
        Assert.Equal(quando, data);
        Assert.Equal(motivo, m);
    }

    [Theory]
    [InlineData("nexus.db")]
    [InlineData("foto.db")]
    [InlineData("nexus_2026-13-40_999999_auto.db")]
    [InlineData("nexus_2026-10-19_143205_outro.db")]
    public void Arquivo_fora_do_padrao_e_ignorado(string nome) =>
        Assert.False(RegrasBackup.TentarLerNome(nome, out _, out _));

    [Fact]
    public void Nome_repetido_no_mesmo_segundo_com_sufixo_numerico_ainda_e_lido() =>
        Assert.True(RegrasBackup.TentarLerNome("nexus_2026-10-19_143205_manual_2.db", out _, out var m)
                    && m == MotivoBackup.Manual);

    [Fact]
    public void Devido_respeita_intervalo_desligado_e_nunca_feito()
    {
        var agora = new DateTime(2026, 10, 19, 12, 0, 0);
        Assert.False(RegrasBackup.Devido(null, agora, 0));                       // desligado
        Assert.True(RegrasBackup.Devido(null, agora, 24));                       // nunca fez
        Assert.False(RegrasBackup.Devido(agora.AddHours(-23), agora, 24));       // ainda cedo
        Assert.True(RegrasBackup.Devido(agora.AddHours(-24), agora, 24));        // na hora
    }

    [Fact]
    public void Retencao_apaga_so_os_comuns_mais_antigos_e_poupa_os_de_seguranca()
    {
        var baseData = new DateTime(2026, 10, 1);
        var lista = new List<InfoBackup>();
        for (var i = 0; i < 5; i++) lista.Add(new($"a{i}.db", baseData.AddDays(i), MotivoBackup.Automatico, 1));
        lista.Add(new("seg.db", baseData.AddDays(-30), MotivoBackup.PreRestauracao, 1));
        lista.Add(new("atu.db", baseData.AddDays(-20), MotivoBackup.PreAtualizacao, 1));

        var apagar = RegrasBackup.SelecionarParaApagar(lista, 3);

        Assert.Equal(new[] { "a1.db", "a0.db" }, apagar.Select(b => b.Caminho));
    }

    [Fact]
    public void MesmoDisco_compara_a_raiz()
    {
        Assert.True(RegrasBackup.MesmoDisco(@"C:\a\x.db", @"C:\b"));
        Assert.False(RegrasBackup.MesmoDisco(@"C:\a\x.db", @"D:\b"));
    }

    [Fact]
    public void Configuracao_valida_limites()
    {
        Assert.Throws<RegraDeNegocioException>(() => new ConfiguracaoBackup { Pasta = " " }.Validar());
        Assert.Throws<RegraDeNegocioException>(() => new ConfiguracaoBackup { Manter = 2 }.Validar());
        Assert.Throws<RegraDeNegocioException>(() => new ConfiguracaoBackup { IntervaloHoras = -1 }.Validar());
        new ConfiguracaoBackup { IntervaloHoras = 0 }.Validar(); // desligar é permitido
    }
}

/// <summary>Backup e restauração com um banco de verdade em arquivo (numa pasta temporária).</summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-bkp-" + Guid.NewGuid().ToString("N"));
    private readonly string _banco;
    private readonly string _backups;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_pasta);
        _banco = Path.Combine(_pasta, "nexus.db");
        _backups = Path.Combine(_pasta, "backups");
        using var db = Abrir();
        db.Database.Migrate();
        db.Produtos.Add(new Produto { Nome = "Camiseta Original", PrecoVendaCentavos = 5000 });
        db.SaveChanges();
    }

    private PdvDbContext Abrir() =>
        new(new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={_banco}").Options);

    private List<string> Nomes()
    {
        using var db = Abrir();
        return db.Produtos.Select(p => p.Nome).OrderBy(n => n).ToList();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    [Fact]
    public void Backup_gera_arquivo_integro_com_os_dados_e_sem_sobra_de_temporario()
    {
        var info = new BackupService(_banco).Fazer(_backups, MotivoBackup.Manual);

        Assert.True(File.Exists(info.Caminho));
        Assert.Equal(MotivoBackup.Manual, info.Motivo);
        Assert.Equal((true, "ok"), BackupService.Conferir(info.Caminho));
        Assert.Empty(Directory.GetFiles(_backups, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_backups, "*-wal"));
    }

    [Fact]
    public void Backup_com_o_banco_aberto_e_com_dados_ainda_no_wal_sai_completo()
    {
        // Conexão aberta e uma venda recém-gravada: parte dos dados pode estar só no arquivo -wal.
        using var aberto = Abrir();
        aberto.Produtos.Add(new Produto { Nome = "Recém gravado", PrecoVendaCentavos = 100 });
        aberto.SaveChanges();

        var info = new BackupService(_banco).Fazer(_backups, MotivoBackup.Automatico);

        File.Copy(info.Caminho, _banco + ".copia");
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _banco + ".copia", Pooling = false }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Produtos";
        Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void Backup_do_mesmo_segundo_nao_sobrescreve_o_anterior()
    {
        var servico = new BackupService(_banco);
        var agora = new DateTime(2026, 10, 19, 10, 0, 0);

        var a = servico.Fazer(_backups, MotivoBackup.Manual, agora: agora);
        var b = servico.Fazer(_backups, MotivoBackup.Manual, agora: agora);

        Assert.NotEqual(a.Caminho, b.Caminho);
        Assert.Equal(2, servico.Listar(_backups).Count);
    }

    [Fact]
    public void Listar_mostra_so_arquivos_do_nexus_do_mais_novo_para_o_mais_antigo()
    {
        var servico = new BackupService(_banco);
        servico.Fazer(_backups, MotivoBackup.Manual, agora: new DateTime(2026, 10, 1, 8, 0, 0));
        servico.Fazer(_backups, MotivoBackup.Automatico, agora: new DateTime(2026, 10, 2, 8, 0, 0));
        File.WriteAllText(Path.Combine(_backups, "meu-arquivo.db"), "x");

        var lista = servico.Listar(_backups);

        Assert.Equal(2, lista.Count);
        Assert.Equal(MotivoBackup.Automatico, lista[0].Motivo);
    }

    [Fact]
    public void Retencao_mantem_so_o_limite_e_nao_apaga_arquivo_alheio()
    {
        var servico = new BackupService(_banco);
        for (var i = 0; i < 5; i++)
            servico.Fazer(_backups, MotivoBackup.Automatico, agora: new DateTime(2026, 10, 1 + i, 8, 0, 0));
        var alheio = Path.Combine(_backups, "contrato.db");
        File.WriteAllText(alheio, "x");

        var apagados = servico.AplicarRetencao(_backups, 3);

        Assert.Equal(2, apagados);
        Assert.Equal(3, servico.Listar(_backups).Count);
        Assert.True(File.Exists(alheio));
    }

    [Fact]
    public void Fazer_com_manter_ja_aplica_a_retencao()
    {
        var servico = new BackupService(_banco);
        for (var i = 0; i < 4; i++)
            servico.Fazer(_backups, MotivoBackup.Automatico, manter: 3, agora: new DateTime(2026, 10, 1 + i, 8, 0, 0));

        Assert.Equal(3, servico.Listar(_backups).Count);
    }

    [Fact]
    public void Banco_inexistente_da_erro_claro_e_nao_cria_arquivo()
    {
        var servico = new BackupService(Path.Combine(_pasta, "nao-existe.db"));

        var ex = Assert.Throws<RegraDeNegocioException>(() => servico.Fazer(_backups, MotivoBackup.Manual));

        Assert.Contains("Ainda não existe banco", ex.Message);
        Assert.False(Directory.Exists(_backups));
    }

    [Fact]
    public void Conferir_recusa_arquivo_que_nao_e_banco_ou_nao_e_do_nexus()
    {
        var lixo = Path.Combine(_pasta, "lixo.db");
        File.WriteAllText(lixo, "isto não é um banco sqlite, só texto solto suficiente para ter bytes");
        Assert.False(BackupService.Conferir(lixo).Ok);

        var outro = Path.Combine(_pasta, "outro.db");
        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = outro, Pooling = false }.ToString()))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE Coisas (Id INTEGER)";
            cmd.ExecuteNonQuery();
        }
        var (ok, detalhe) = BackupService.Conferir(outro);
        Assert.False(ok);
        Assert.Contains("Nexus LS", detalhe);

        Assert.False(BackupService.Conferir(Path.Combine(_pasta, "sumiu.db")).Ok);
    }

    [Fact]
    public void Restaurar_volta_os_dados_do_backup_e_guarda_uma_copia_de_seguranca()
    {
        var servico = new BackupService(_banco);
        var backup = servico.Fazer(_backups, MotivoBackup.Manual);

        using (var db = Abrir())
        {
            db.Produtos.Add(new Produto { Nome = "Depois do backup", PrecoVendaCentavos = 1 });
            db.SaveChanges();
        }
        Assert.Equal(2, Nomes().Count);

        var seguranca = servico.Restaurar(backup.Caminho, _backups);

        Assert.Equal(new[] { "Camiseta Original" }, Nomes());
        Assert.NotNull(seguranca);
        Assert.Equal(MotivoBackup.PreRestauracao, seguranca!.Motivo);
        // a cópia de segurança tem o que existia ANTES de restaurar, então dá para desfazer
        Assert.Equal((true, "ok"), BackupService.Conferir(seguranca.Caminho));
        Assert.False(File.Exists(_banco + ".antes"));
        Assert.False(File.Exists(_banco + ".restaurando"));
    }

    [Fact]
    public void Restaurar_arquivo_ruim_nao_mexe_no_banco_atual()
    {
        var lixo = Path.Combine(_pasta, "ruim.db");
        File.WriteAllText(lixo, "não é um banco");

        var ex = Assert.Throws<RegraDeNegocioException>(() => new BackupService(_banco).Restaurar(lixo, _backups));

        Assert.Contains("não pode ser restaurado", ex.Message);
        Assert.Equal(new[] { "Camiseta Original" }, Nomes());
        Assert.False(Directory.Exists(_backups)); // nem a cópia de segurança foi feita: nada aconteceu
    }

    [Fact]
    public void Restaurar_com_o_banco_travado_mantem_o_banco_atual()
    {
        var servico = new BackupService(_banco);
        var backup = servico.Fazer(_backups, MotivoBackup.Manual);

        // Segura o arquivo aberto e exclusivo: o Windows não deixa renomear nem abrir.
        SqliteConnection.ClearAllPools();
        using var preso = new FileStream(_banco, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<RegraDeNegocioException>(() => servico.Restaurar(backup.Caminho, _backups));
        preso.Dispose();

        Assert.Equal(new[] { "Camiseta Original" }, Nomes());
        Assert.False(File.Exists(_banco + ".restaurando"));
    }

    [Fact]
    public void Configuracao_e_gravada_e_lida_de_volta_e_arquivo_estragado_vira_padrao()
    {
        var caminho = Path.Combine(_pasta, "backup.json");
        var store = new ConfiguracaoBackupStore(caminho);
        store.Salvar(new ConfiguracaoBackup { Pasta = _backups, IntervaloHoras = 6, Manter = 10, UltimoBackup = new DateTime(2026, 10, 19, 9, 0, 0) });

        var lida = store.Carregar();
        Assert.Equal(_backups, lida.Pasta);
        Assert.Equal(6, lida.IntervaloHoras);
        Assert.Equal(10, lida.Manter);
        Assert.Equal(new DateTime(2026, 10, 19, 9, 0, 0), lida.UltimoBackup);
        Assert.False(File.Exists(caminho + ".tmp"));

        File.WriteAllText(caminho, "{ quebrado");
        Assert.Equal(24, store.Carregar().IntervaloHoras);
    }
}

public sealed class GestorBackupTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-gb-" + Guid.NewGuid().ToString("N"));
    private DateTime _agora = new(2026, 10, 19, 10, 0, 0);
    private readonly GestorBackup _gestor;

    public GestorBackupTests()
    {
        Directory.CreateDirectory(_pasta);
        var banco = Path.Combine(_pasta, "nexus.db");
        using (var db = new PdvDbContext(new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={banco}").Options))
            db.Database.Migrate();
        var store = new ConfiguracaoBackupStore(Path.Combine(_pasta, "backup.json"));
        store.Salvar(new ConfiguracaoBackup { Pasta = Path.Combine(_pasta, "bk"), IntervaloHoras = 24, Manter = 3 });
        _gestor = new GestorBackup(new BackupService(banco), store, () => _agora);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Primeira_abertura_faz_backup_e_depois_so_quando_passa_o_intervalo()
    {
        Assert.NotNull(await _gestor.FazerSeEstiverNaHoraAsync());      // nunca fez
        Assert.Null(await _gestor.FazerSeEstiverNaHoraAsync());         // acabou de fazer

        _agora = _agora.AddHours(23);
        Assert.Null(await _gestor.FazerSeEstiverNaHoraAsync());

        _agora = _agora.AddHours(1);
        Assert.NotNull(await _gestor.FazerSeEstiverNaHoraAsync());
    }

    [Fact]
    public async Task Intervalo_zero_desliga_o_automatico_mas_o_manual_continua_valendo()
    {
        var c = _gestor.Configuracao;
        c.IntervaloHoras = 0;
        _gestor.SalvarConfiguracao(c);

        Assert.Null(await _gestor.FazerSeEstiverNaHoraAsync());
        Assert.NotNull(await _gestor.FazerAsync(MotivoBackup.Manual));
    }

    [Fact]
    public async Task Backup_de_seguranca_nao_conta_como_ultimo_backup()
    {
        await _gestor.FazerAsync(MotivoBackup.PreAtualizacao);

        Assert.Null(_gestor.Configuracao.UltimoBackup);
        Assert.True(_gestor.EstaNaHora());
    }

    [Fact]
    public async Task Salvar_a_tela_de_configuracao_nao_apaga_o_horario_do_ultimo_backup()
    {
        await _gestor.FazerAsync(MotivoBackup.Manual);
        var novo = new ConfiguracaoBackup { Pasta = _gestor.Configuracao.Pasta, IntervaloHoras = 12, Manter = 5 };

        _gestor.SalvarConfiguracao(novo);

        Assert.Equal(_agora, _gestor.Configuracao.UltimoBackup);
        Assert.Equal(12, _gestor.Configuracao.IntervaloHoras);
    }

    [Fact]
    public async Task Respeita_o_limite_de_arquivos_da_configuracao()
    {
        for (var i = 0; i < 5; i++)
        {
            _agora = _agora.AddMinutes(1);
            await _gestor.FazerAsync(MotivoBackup.Manual);
        }

        Assert.Equal(3, _gestor.Servico.Listar(_gestor.Configuracao.Pasta).Count);
    }
}

public class SituacaoDoBackupTests
{
    private static readonly DateTime Agora = new(2026, 10, 19, 12, 0, 0);

    [Fact]
    public void Nunca_feito_e_sempre_alerta_mesmo_com_automatico_desligado() =>
        Assert.Equal(SituacaoBackup.Nunca, RegrasBackup.Situacao(null, Agora, 0));

    [Fact]
    public void Automatico_desligado_com_backup_antigo_e_so_manual() =>
        Assert.Equal(SituacaoBackup.SoManual, RegrasBackup.Situacao(Agora.AddDays(-40), Agora, 0));

    [Fact]
    public void Ate_o_dobro_do_intervalo_esta_em_dia_e_depois_disso_atrasado()
    {
        Assert.Equal(SituacaoBackup.EmDia, RegrasBackup.Situacao(Agora.AddHours(-48), Agora, 24));
        Assert.Equal(SituacaoBackup.Atrasado, RegrasBackup.Situacao(Agora.AddHours(-48).AddMinutes(-1), Agora, 24));
    }
}

public class AbrirNoPdvTests
{
    [Fact]
    public async Task Por_padrao_abre_na_tela_inicial_e_a_escolha_e_gravada()
    {
        using var banco = new Infra.BancoDeTeste();
        var servico = new ConfiguracaoService(banco.Fabrica);

        Assert.False((await servico.ObterAsync()).AbrirNoPdv);

        await servico.SalvarAsync(new DadosLoja("Loja", AbrirNoPdv: true));
        Assert.True((await servico.ObterAsync()).AbrirNoPdv);

        await servico.SalvarAsync(new DadosLoja("Loja"));
        Assert.False((await servico.ObterAsync()).AbrirNoPdv);
    }
}
