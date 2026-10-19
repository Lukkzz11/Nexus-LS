using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.Backup;
using NexusLS.App.ViewModels;
using NexusLS.Core.Backup;
using NexusLS.Core.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Tests;

public sealed class BackupViewModelTests : IDisposable
{
    private sealed class DialogosFalsos : IDialogosBackup
    {
        public string? Pasta;
        public string? Arquivo;
        public bool Resposta = true;
        public int Reinicios;
        public string? UltimaPergunta;
        public string? EscolherPasta(string? pastaAtual) => Pasta;
        public string? EscolherArquivoBackup(string? pastaInicial) => Arquivo;
        public bool Confirmar(string texto) { UltimaPergunta = texto; return Resposta; }
        public void Reiniciar() => Reinicios++;
    }

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-vmbkp-" + Guid.NewGuid().ToString("N"));
    private readonly string _banco;
    private readonly GestorBackup _gestor;
    private readonly DialogosFalsos _dialogos = new();
    private readonly ConfiguracoesViewModel _vm;

    public BackupViewModelTests()
    {
        Directory.CreateDirectory(_pasta);
        _banco = Path.Combine(_pasta, "nexus.db");
        var opcoes = new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={_banco}").Options;
        using (var db = new PdvDbContext(opcoes))
        {
            db.Database.Migrate();
            db.Produtos.Add(new Produto { Nome = "Camiseta", PrecoVendaCentavos = 1000 });
            db.SaveChanges();
        }

        var store = new ConfiguracaoBackupStore(Path.Combine(_pasta, "backup.json"));
        store.Salvar(new ConfiguracaoBackup { Pasta = Path.Combine(_pasta, "bk"), IntervaloHoras = 24, Manter = 5 });
        _gestor = new GestorBackup(new BackupService(_banco), store);

        var fabrica = new FabricaContexto(opcoes);
        var config = new ConfiguracaoService(fabrica);
        _vm = new ConfiguracoesViewModel(config, new CupomService(fabrica, config, new ImpressoraDeMentira()), _gestor, _dialogos);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    [Fact]
    public void A_secao_aparece_com_as_preferencias_gravadas()
    {
        Assert.True(_vm.TemBackup);
        Assert.Equal(Path.Combine(_pasta, "bk"), _vm.PastaBackup);
        Assert.Equal("5", _vm.ManterTexto);
        Assert.Equal(24, _vm.IntervaloSelecionado.Horas);
        Assert.Contains("Ainda não foi feito", _vm.UltimoBackupTexto);
    }

    [Fact]
    public void Sem_gestor_a_secao_some_e_a_tela_continua_funcionando()
    {
        var fabrica = new FabricaContexto(new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={_banco}").Options);
        var config = new ConfiguracaoService(fabrica);
        var vm = new ConfiguracoesViewModel(config, new CupomService(fabrica, config, new ImpressoraDeMentira()));

        Assert.False(vm.TemBackup);
    }

    [Fact]
    public async Task Fazer_backup_agora_grava_o_arquivo_atualiza_a_lista_e_o_ultimo_backup()
    {
        await _vm.FazerBackupAgoraCommand.ExecuteAsync(null);

        Assert.False(_vm.MensagemBackupEhErro);
        Assert.Contains("Backup feito: nexus_", _vm.MensagemBackup);
        Assert.Single(_vm.Backups);
        Assert.Contains("Último backup:", _vm.UltimoBackupTexto);
        Assert.Single(Directory.GetFiles(_gestor.Configuracao.Pasta, "nexus_*_manual.db"));
    }

    [Fact]
    public async Task Pasta_vazia_mostra_o_erro_na_propria_secao()
    {
        _vm.PastaBackup = "  ";

        await _vm.FazerBackupAgoraCommand.ExecuteAsync(null);

        Assert.True(_vm.MensagemBackupEhErro);
        Assert.Contains("pasta", _vm.MensagemBackup);
    }

    [Fact]
    public async Task Numero_de_backups_invalido_mostra_erro_e_nao_salva()
    {
        _vm.ManterTexto = "abc";
        await _vm.SalvarBackupCommand.ExecuteAsync(null);
        Assert.True(_vm.MensagemBackupEhErro);

        _vm.ManterTexto = "1";
        await _vm.SalvarBackupCommand.ExecuteAsync(null);
        Assert.True(_vm.MensagemBackupEhErro);
        Assert.Equal(5, _gestor.Configuracao.Manter);
    }

    [Fact]
    public async Task Salvar_preferencias_grava_pasta_intervalo_e_limite()
    {
        _dialogos.Pasta = Path.Combine(_pasta, "outra");
        _vm.EscolherPastaBackupCommand.Execute(null);
        _vm.IntervaloSelecionado = _vm.IntervalosBackup.First(i => i.Horas == 6);
        _vm.ManterTexto = "10";

        await _vm.SalvarBackupCommand.ExecuteAsync(null);

        var c = _gestor.Configuracao;
        Assert.Equal(Path.Combine(_pasta, "outra"), c.Pasta);
        Assert.Equal(6, c.IntervaloHoras);
        Assert.Equal(10, c.Manter);
    }

    [Fact]
    public void Pasta_no_mesmo_disco_do_banco_mostra_o_aviso()
    {
        _vm.PastaBackup = Path.Combine(Path.GetPathRoot(CaminhoBanco.Padrao())!, "Backups");
        Assert.Contains("mesmo disco", _vm.AvisoBackup);
    }

    [Fact]
    public async Task Restaurar_o_selecionado_pede_confirmacao_troca_os_dados_e_reinicia()
    {
        await _vm.FazerBackupAgoraCommand.ExecuteAsync(null);
        var opcoes = new DbContextOptionsBuilder<PdvDbContext>().UseSqlite($"Data Source={_banco}").Options;
        using (var db = new PdvDbContext(opcoes))
        {
            db.Produtos.Add(new Produto { Nome = "Depois", PrecoVendaCentavos = 1 });
            db.SaveChanges();
        }
        _vm.BackupSelecionado = _vm.Backups[0];

        await _vm.RestaurarCommand.ExecuteAsync(null);

        Assert.Contains("TODOS os dados", _dialogos.UltimaPergunta);
        Assert.Equal(1, _dialogos.Reinicios);
        using var depois = new PdvDbContext(opcoes);
        Assert.Equal(new[] { "Camiseta" }, depois.Produtos.Select(p => p.Nome).ToList());
    }

    [Fact]
    public async Task Restaurar_dizendo_nao_nao_muda_nada_e_nao_reinicia()
    {
        await _vm.FazerBackupAgoraCommand.ExecuteAsync(null);
        _vm.BackupSelecionado = _vm.Backups[0];
        _dialogos.Resposta = false;

        await _vm.RestaurarCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogos.Reinicios);
        Assert.Single(_vm.Backups);
    }

    [Fact]
    public async Task Restaurar_arquivo_estragado_mostra_erro_e_nao_pergunta_nem_reinicia()
    {
        var ruim = Path.Combine(_pasta, "ruim.db");
        File.WriteAllText(ruim, "não sou um banco");
        _dialogos.Arquivo = ruim;
        _vm.BackupSelecionado = null;

        await _vm.RestaurarCommand.ExecuteAsync(null);

        Assert.True(_vm.MensagemBackupEhErro);
        Assert.Contains("não pode ser restaurado", _vm.MensagemBackup);
        Assert.Null(_dialogos.UltimaPergunta);
        Assert.Equal(0, _dialogos.Reinicios);
    }
}
