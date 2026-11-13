using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Backup;
using NexusLS.Core.Data;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoIntervaloBackup(string Texto, int Horas);

/// <summary>Uma linha da lista de backups.</summary>
public record ItemBackup(InfoBackup Info)
{
    public string Texto =>
        $"{Info.Data:dd/MM/yyyy HH:mm}  ·  {Descricao(Info.Motivo)}  ·  {TamanhoTexto(Info.Bytes)}";

    private static string Descricao(MotivoBackup m) => m switch
    {
        MotivoBackup.Manual => "manual",
        MotivoBackup.Automatico => "automático",
        MotivoBackup.PreAtualizacao => "antes de atualizar",
        MotivoBackup.PreLimpeza => "antes de apagar dados",
        MotivoBackup.PreImportacao => "antes de importar",
        _ => "antes de restaurar"
    };

    private static string TamanhoTexto(long bytes) =>
        bytes >= 1_048_576 ? $"{TextoBr.Numero(bytes / 1_048_576.0, "0.0")} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}

/// <summary>Parte "Backup" da tela de Configurações (preferências, backup manual, lista e restauração).</summary>
public partial class ConfiguracoesViewModel
{
    private readonly GestorBackup? _backup;
    private readonly NexusLS.App.Backup.IDialogosBackup? _dialogos;

    public IReadOnlyList<OpcaoIntervaloBackup> IntervalosBackup { get; } = new[]
    {
        new OpcaoIntervaloBackup("Desligado (só manual)", 0),
        new OpcaoIntervaloBackup("A cada hora", 1),
        new OpcaoIntervaloBackup("A cada 6 horas", 6),
        new OpcaoIntervaloBackup("A cada 12 horas", 12),
        new OpcaoIntervaloBackup("Todo dia", 24),
        new OpcaoIntervaloBackup("Toda semana", 168)
    };

    public ObservableCollection<ItemBackup> Backups { get; } = new();

    /// <summary>Só mostra a seção de backup quando o gestor foi entregue (nos testes antigos ele não existe).</summary>
    public bool TemBackup => _backup is not null;

    /// <summary>Bloco "Apagar dados". Nulo nos testes antigos; a tela só mostra quando existe e o usuário é administrador.</summary>
    public LimparDadosViewModel? LimparDados { get; }

    /// <summary>Versão do programa e botão "Verificar atualizações". Nulo nos testes antigos.</summary>
    public AtualizacaoViewModel? Atualizacao { get; }
    public bool TemAtualizacao => Atualizacao is not null;
    public bool TemLimparDados => LimparDados?.Visivel ?? false;

    [ObservableProperty] private string _pastaBackup = "";
    [ObservableProperty] private OpcaoIntervaloBackup _intervaloSelecionado = null!;
    [ObservableProperty] private string _manterTexto = "20";
    [ObservableProperty] private string _ultimoBackupTexto = "";
    [ObservableProperty] private string _avisoBackup = "";
    [ObservableProperty] private ItemBackup? _backupSelecionado;
    [ObservableProperty] private string _mensagemBackup = "";
    [ObservableProperty] private bool _mensagemBackupEhErro;

    private void CarregarBackup()
    {
        IntervaloSelecionado = IntervalosBackup[4];
        if (_backup is null) return;

        var c = _backup.Configuracao;
        PastaBackup = c.Pasta;
        ManterTexto = c.Manter.ToString();
        IntervaloSelecionado = IntervalosBackup.FirstOrDefault(i => i.Horas == c.IntervaloHoras)
                               ?? new OpcaoIntervaloBackup($"A cada {c.IntervaloHoras} horas", c.IntervaloHoras);
        AtualizarListaBackup();
    }

    partial void OnPastaBackupChanged(string value) => AtualizarAvisoDisco();

    private void AtualizarAvisoDisco() =>
        AvisoBackup = !string.IsNullOrWhiteSpace(PastaBackup) && RegrasBackup.MesmoDisco(CaminhoBanco.Padrao(), PastaBackup)
            ? "Esta pasta fica no mesmo disco do banco: se o disco falhar, o backup se perde junto. Prefira um pen drive, HD externo ou pasta sincronizada na nuvem."
            : "";

    private void AtualizarListaBackup()
    {
        if (_backup is null) return;
        var c = _backup.Configuracao;
        Backups.Clear();
        foreach (var b in _backup.Servico.Listar(c.Pasta)) Backups.Add(new ItemBackup(b));
        UltimoBackupTexto = c.UltimoBackup is { } u ? $"Último backup: {u:dd/MM/yyyy HH:mm}" : "Ainda não foi feito nenhum backup.";
    }

    private void MostrarBackup(string texto, bool erro)
    {
        MensagemBackupEhErro = erro;
        MensagemBackup = texto;
    }

    /// <summary>Roda uma ação do backup mostrando qualquer erro na própria seção (nunca em silêncio).</summary>
    private async Task ExecutarBackupAsync(Func<Task> acao)
    {
        try { await acao(); }
        catch (RegraDeNegocioException ex) { MostrarBackup(ex.Message, true); }
        catch (Exception ex) { MostrarBackup("Erro inesperado: " + ex.Message, true); }
    }

    private ConfiguracaoBackup LerPreferenciasBackup()
    {
        if (!int.TryParse(ManterTexto.Trim(), out var manter))
            throw new RegraDeNegocioException("Informe quantos backups guardar (um número inteiro).");
        return new ConfiguracaoBackup { Pasta = PastaBackup.Trim(), IntervaloHoras = IntervaloSelecionado.Horas, Manter = manter };
    }

    [RelayCommand]
    private void EscolherPastaBackup()
    {
        var escolhida = _dialogos?.EscolherPasta(PastaBackup);
        if (!string.IsNullOrWhiteSpace(escolhida)) PastaBackup = escolhida;
    }

    [RelayCommand]
    private Task SalvarBackupAsync() => ExecutarBackupAsync(() =>
    {
        _backup!.SalvarConfiguracao(LerPreferenciasBackup());
        AtualizarListaBackup();
        MostrarBackup("Preferências de backup salvas.", false);
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task FazerBackupAgoraAsync() => ExecutarBackupAsync(async () =>
    {
        _backup!.SalvarConfiguracao(LerPreferenciasBackup()); // usa a pasta que está na tela
        var info = await _backup.FazerAsync(MotivoBackup.Manual);
        AtualizarListaBackup();
        MostrarBackup($"Backup feito: {info.Nome}", false);
    });

    [RelayCommand]
    private Task RestaurarAsync() => ExecutarBackupAsync(async () =>
    {
        var arquivo = BackupSelecionado?.Info.Caminho
                      ?? _dialogos?.EscolherArquivoBackup(_backup!.Configuracao.Pasta);
        if (string.IsNullOrWhiteSpace(arquivo)) return;

        var (ok, detalhe) = BackupService.Conferir(arquivo);
        if (!ok) throw new RegraDeNegocioException("Esse backup não pode ser restaurado: " + detalhe);

        var pergunta = "Restaurar este backup vai trocar TODOS os dados atuais (produtos, vendas, clientes, caixa) " +
                       "pelos dados dele.\n\nAntes disso o programa guarda uma cópia dos dados de agora, para você poder voltar atrás." +
                       "\n\nO programa será reiniciado. Continuar?";
        if (_dialogos is null || !_dialogos.Confirmar(pergunta)) return;

        var seguranca = await _backup!.Servico.RestaurarAsync(arquivo, _backup.Configuracao.Pasta);
        MostrarBackup("Backup restaurado." + (seguranca is null ? "" : $" Cópia dos dados anteriores: {seguranca.Nome}"), false);
        _dialogos.Reiniciar();
    });
}
