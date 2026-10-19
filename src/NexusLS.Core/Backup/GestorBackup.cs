namespace NexusLS.Core.Backup;

/// <summary>
/// Junta o <see cref="BackupService"/> com as preferências: faz o backup na pasta escolhida, aplica o limite de
/// arquivos e anota o horário do último backup. Também decide se já está na hora do backup automático.
/// </summary>
public class GestorBackup
{
    private readonly BackupService _servico;
    private readonly ConfiguracaoBackupStore _store;
    private readonly Func<DateTime> _agora;

    public GestorBackup(BackupService servico, ConfiguracaoBackupStore store, Func<DateTime>? agora = null)
    {
        _servico = servico;
        _store = store;
        _agora = agora ?? (() => DateTime.Now);
    }

    public BackupService Servico => _servico;

    public ConfiguracaoBackup Configuracao => _store.Carregar();

    public void SalvarConfiguracao(ConfiguracaoBackup nova)
    {
        // O horário do último backup pertence ao gestor, não ao formulário: não pode ser apagado ao salvar a tela.
        nova.UltimoBackup = _store.Carregar().UltimoBackup;
        _store.Salvar(nova);
    }

    /// <summary>Faz um backup agora. Manual e automático contam como "último backup"; os de segurança não.</summary>
    public async Task<InfoBackup> FazerAsync(MotivoBackup motivo)
    {
        var config = _store.Carregar();
        var momento = _agora();
        var info = await _servico.FazerAsync(config.Pasta, motivo, config.Manter, momento).ConfigureAwait(false);
        if (motivo is MotivoBackup.Manual or MotivoBackup.Automatico)
        {
            config.UltimoBackup = momento;
            _store.Salvar(config);
        }
        return info;
    }

    /// <summary>Está na hora do backup automático (e ele está ligado)?</summary>
    public bool EstaNaHora() =>
        RegrasBackup.Devido(_store.Carregar().UltimoBackup, _agora(), _store.Carregar().IntervaloHoras);

    /// <summary>Faz o backup automático se estiver na hora. Devolve o backup feito, ou null se não era a hora.</summary>
    public async Task<InfoBackup?> FazerSeEstiverNaHoraAsync() =>
        EstaNaHora() ? await FazerAsync(MotivoBackup.Automatico).ConfigureAwait(false) : null;
}
