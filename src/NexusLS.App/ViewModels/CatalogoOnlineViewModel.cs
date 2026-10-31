using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha do histórico de sincronizações.</summary>
public record ItemLogSincronizacao(SincronizacaoLog Log)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public string Texto =>
        $"{Log.IniciadaEm.ToString("dd/MM/yyyy HH:mm", PtBr)}  ·  {Resultado(Log.Resultado)}  ·  {Log.Mensagem}";

    private static string Resultado(ResultadoSincronizacao r) => r switch
    {
        ResultadoSincronizacao.Sucesso => "Sucesso",
        ResultadoSincronizacao.Parcial => "Parcial",
        ResultadoSincronizacao.Falhou => "Falhou",
        _ => "Sem mudanças"
    };
}

/// <summary>
/// Tela "Catálogo online": configura o Firebase desta instalação e tem o botão "Atualizar Firestore".
/// O envio NUNCA é automático: o clique calcula o plano, mostra o que vai acontecer e só envia depois da confirmação.
/// </summary>
public partial class CatalogoOnlineViewModel : ViewModelBase
{
    private readonly ConfiguracaoFirebaseStore _config;
    private readonly SincronizadorCatalogo _sincronizador;
    private readonly SincronizacaoRepositorio _repo;
    private readonly IDestinoCatalogo _destino;
    private readonly ConfiguracaoService? _loja;

    private readonly NexusLS.Core.Sincronizacao.Operacional.ConfiguracaoOperacionalStore? _operacional;
    private PlanoSincronizacao? _planoPendente;
    private CancellationTokenSource? _cancelamento;

    public CatalogoOnlineViewModel(
        ConfiguracaoFirebaseStore config, SincronizadorCatalogo sincronizador, SincronizacaoRepositorio repo,
        IDestinoCatalogo destino, ConfiguracaoService? loja = null,
        NexusLS.Core.Sincronizacao.Operacional.ConfiguracaoOperacionalStore? operacional = null)
    {
        _config = config;
        _sincronizador = sincronizador;
        _repo = repo;
        _destino = destino;
        _loja = loja;
        _operacional = operacional;
        CarregarConfiguracao();
        CarregarOperacional();
        Carregamento = AtualizarHistoricoAsync();
    }

    /// <summary>A primeira carga do histórico (os testes esperam por ela).</summary>
    public Task Carregamento { get; }

    public ObservableCollection<ItemLogSincronizacao> Historico { get; } = new();
    public ObservableCollection<string> Problemas { get; } = new();

    [ObservableProperty] private string _projectId = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _email = "";

    /// <summary>Senha digitada agora. Em branco = manter a que já está salva (que nunca é mostrada).</summary>
    [ObservableProperty] private string _senhaNova = "";

    [ObservableProperty] private string _situacaoConfiguracao = "";
    [ObservableProperty] private bool _configuracaoCompleta;

    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private bool _aguardandoConfirmacao;
    [ObservableProperty] private string _textoConfirmacao = "";
    [ObservableProperty] private double _progressoPercentual;
    [ObservableProperty] private string _progressoTexto = "";
    [ObservableProperty] private string _resultadoTexto = "";
    [ObservableProperty] private bool _resultadoEhErro;

    public bool PodeAtualizar => ConfiguracaoCompleta && !Ocupado && !AguardandoConfirmacao;
    public bool PodeEditar => !Ocupado;

    partial void OnConfiguracaoCompletaChanged(bool value) => OnPropertyChanged(nameof(PodeAtualizar));
    partial void OnOcupadoChanged(bool value) { OnPropertyChanged(nameof(PodeAtualizar)); OnPropertyChanged(nameof(PodeEditar)); }
    partial void OnAguardandoConfirmacaoChanged(bool value) => OnPropertyChanged(nameof(PodeAtualizar));

    private void CarregarConfiguracao()
    {
        var c = _config.Carregar();
        ProjectId = c.ProjectId;
        ApiKey = c.ApiKey;
        Email = c.Email;
        SenhaNova = "";
        AtualizarSituacao(c);
    }

    // ---- Nuvem operacional (projeto B): vendas e demais registros. Os usuários entram neste projeto. ----

    public bool TemOperacional => _operacional is not null;

    [ObservableProperty] private string _operProjectId = "";
    [ObservableProperty] private string _operApiKey = "";
    [ObservableProperty] private string _operLojaId = "";
    [ObservableProperty] private string _situacaoOperacional = "";

    private void CarregarOperacional()
    {
        if (_operacional is null) return;
        var c = _operacional.Carregar();
        OperProjectId = c.ProjectId;
        OperApiKey = c.ApiKey;
        OperLojaId = c.LojaId;
        SituacaoOperacional = c.EstaCompleta
            ? $"Vendas enviadas ao projeto {c.ProjectId}, loja {c.LojaId}. Os usuários entram neste projeto."
            : "Ainda não configurado: sem isso as vendas ficam só neste computador (na fila, esperando).";
    }

    [RelayCommand]
    private Task SalvarOperacionalAsync() => ExecutarAsync(() =>
    {
        _operacional?.Salvar(new NexusLS.Core.Sincronizacao.Operacional.ConfiguracaoOperacional
            { ProjectId = OperProjectId, ApiKey = OperApiKey, LojaId = OperLojaId });
        CarregarOperacional();
        MostrarSucesso("Nuvem operacional salva. Entre com seu usuário (menu lateral) para enviar as vendas.");
        return Task.CompletedTask;
    });

    private void AtualizarSituacao(ConfiguracaoFirebase c)
    {
        ConfiguracaoCompleta = c.EstaCompleta;
        SituacaoConfiguracao = c.EstaCompleta
            ? $"Configurado para o projeto {c.ProjectId} (senha guardada com a proteção do Windows)."
            : "Ainda não configurado: informe o projeto, a chave da API, o e-mail e a senha do login da licença.";
    }

    private async Task AtualizarHistoricoAsync()
    {
        var logs = await _repo.UltimosLogsAsync(15);
        Historico.Clear();
        foreach (var l in logs) Historico.Add(new ItemLogSincronizacao(l));
    }

    [RelayCommand]
    private Task SalvarConfiguracaoAsync() => ExecutarAsync(() =>
    {
        _config.Salvar(new ConfiguracaoFirebase { ProjectId = ProjectId, ApiKey = ApiKey, Email = Email }, SenhaNova);
        CarregarConfiguracao();
        MostrarSucesso("Configuração salva.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task TestarConexaoAsync() => ExecutarAsync(async () =>
    {
        Ocupado = true;
        try
        {
            await _destino.VerificarAsync();
            MostrarSucesso("Conexão ok: o login vale e há permissão para gravar o catálogo.");
        }
        catch (FalhaDestinoException ex)
        {
            MostrarErro(ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    });

    /// <summary>O botão "Atualizar Firestore": confere a conexão e mostra o plano para o usuário confirmar. Não envia nada ainda.</summary>
    [RelayCommand]
    private Task AtualizarFirestoreAsync() => ExecutarAsync(async () =>
    {
        ResultadoTexto = "";
        Problemas.Clear();
        Ocupado = true;
        try
        {
            ProgressoTexto = "Conferindo a conexão e calculando o que mudou...";
            var preparo = await _sincronizador.PrepararAsync(await NomeDoOperadorAsync());
            await AtualizarHistoricoAsync();

            if (preparo.Plano is null)
            {
                ProgressoTexto = "";
                MostrarResultado(preparo.Erro!, erro: true);
                return;
            }

            ListarProblemas(preparo.Plano);
            if (!preparo.Plano.TemQueEnviar)
            {
                // Nada a enviar: ainda assim vira uma linha no histórico ("sem mudanças").
                var resultado = await _sincronizador.ExecutarAsync(preparo.Plano, await NomeDoOperadorAsync());
                await AtualizarHistoricoAsync();
                ProgressoTexto = "";
                MostrarResultado(resultado.Log.Mensagem ?? "", erro: resultado.Log.Resultado == ResultadoSincronizacao.Falhou);
                return;
            }

            _planoPendente = preparo.Plano;
            TextoConfirmacao = DescreverPlano(preparo.Plano);
            ProgressoTexto = "";
            AguardandoConfirmacao = true;
        }
        finally
        {
            Ocupado = false;
        }
    });

    [RelayCommand]
    private Task ConfirmarEnvioAsync() => ExecutarAsync(async () =>
    {
        if (_planoPendente is null) return;
        var plano = _planoPendente;
        _planoPendente = null;
        AguardandoConfirmacao = false;
        Ocupado = true;
        _cancelamento = new CancellationTokenSource();
        try
        {
            ProgressoPercentual = 0;
            var progresso = new Progress<ProgressoSincronizacao>(p =>
            {
                ProgressoPercentual = p.Total == 0 ? 100 : 100.0 * p.Processados / p.Total;
                ProgressoTexto = $"{p.Etapa} {p.Processados} de {p.Total}";
            });
            var resultado = await _sincronizador.ExecutarAsync(plano, await NomeDoOperadorAsync(), progresso, _cancelamento.Token);
            await AtualizarHistoricoAsync();
            ProgressoTexto = "";
            ProgressoPercentual = 0;
            ListarProblemas(plano, resultado.FalhasPorProduto);
            MostrarResultado(resultado.Log.Mensagem ?? "", erro: resultado.Log.Resultado is ResultadoSincronizacao.Falhou or ResultadoSincronizacao.Parcial);
        }
        finally
        {
            _cancelamento.Dispose();
            _cancelamento = null;
            Ocupado = false;
        }
    });

    /// <summary>Desiste antes de enviar: nada foi gravado em lugar nenhum.</summary>
    [RelayCommand]
    private void CancelarPrevia()
    {
        _planoPendente = null;
        AguardandoConfirmacao = false;
        TextoConfirmacao = "";
        MostrarResultado("Nada foi enviado.", erro: false);
    }

    /// <summary>Interrompe um envio em andamento. O que já foi confirmado pelo servidor fica anotado.</summary>
    [RelayCommand]
    private void CancelarEnvio() => _cancelamento?.Cancel();

    private async Task<string?> NomeDoOperadorAsync() =>
        _loja is null ? null : (await _loja.ObterAsync()).NomeOperador;

    private void MostrarResultado(string texto, bool erro)
    {
        ResultadoEhErro = erro;
        ResultadoTexto = texto;
    }

    private void ListarProblemas(PlanoSincronizacao plano, IEnumerable<string>? falhas = null)
    {
        Problemas.Clear();
        foreach (var p in plano.Invalidos) Problemas.Add($"{p.Nome}: {p.Motivo} (não será enviado até corrigir)");
        if (falhas is not null) foreach (var f in falhas) Problemas.Add(f);
    }

    private static string DescreverPlano(PlanoSincronizacao plano)
    {
        var partes = new List<string>();
        if (plano.Novos.Count > 0) partes.Add($"{plano.Novos.Count} novo(s)");
        if (plano.Alterados.Count > 0) partes.Add($"{plano.Alterados.Count} alterado(s)");
        if (plano.Desativar.Count > 0) partes.Add($"{plano.Desativar.Count} para desativar");
        var texto = "Vai enviar " + string.Join(", ", partes) + ".";
        if (plano.Inalterados > 0) texto += $" {plano.Inalterados} já estão atualizados.";
        if (plano.Invalidos.Count > 0) texto += $" {plano.Invalidos.Count} com dados inválidos ficam de fora (veja a lista).";
        return texto + " Nada será apagado na nuvem.";
    }
}
