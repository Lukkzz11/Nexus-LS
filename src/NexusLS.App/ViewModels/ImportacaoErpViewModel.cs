using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Seção "Importar do ERP" da tela Catálogo online: configura o projeto do ERP, entra com o usuário dele e traz
/// produtos, clientes, turnos e vendas. Só LÊ o ERP, e só o que mudou desde a última importação.
/// </summary>
public partial class ImportacaoErpViewModel : ViewModelBase
{
    private readonly ConfiguracaoErpStore _config;
    private readonly ContaErp _conta;
    private readonly Func<ImportadorErp> _criarImportador;

    /// <summary>Abre a janela de entrada no ERP. Preenchida pela janela principal.</summary>
    public Func<SessaoUsuario?> PedirLogin { get; set; } = () => null;

    public ImportacaoErpViewModel(ConfiguracaoErpStore config, ContaErp conta, Func<ImportadorErp> criarImportador)
    {
        _config = config;
        _conta = conta;
        _criarImportador = criarImportador;
        Carregar();
    }

    [ObservableProperty] private string _erpProjectId = "";
    [ObservableProperty] private string _erpApiKey = "";
    [ObservableProperty] private string _erpEmpresaId = "lifesurf-principal";
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private string _resultado = "";
    [ObservableProperty] private bool _ocupado;

    public bool PodeEditar => !Ocupado;
    public bool PodeImportar => !Ocupado && _config.Carregar().EstaCompleta;
    partial void OnOcupadoChanged(bool value) { OnPropertyChanged(nameof(PodeEditar)); OnPropertyChanged(nameof(PodeImportar)); }

    private void Carregar()
    {
        var c = _config.Carregar();
        ErpProjectId = c.ProjectId;
        ErpApiKey = c.ApiKey;
        ErpEmpresaId = c.EmpresaId;
        AtualizarSituacao();
    }

    private void AtualizarSituacao()
    {
        var c = _config.Carregar();
        Situacao = !c.EstaCompleta
            ? "Ainda não configurado: informe o projeto Firebase do ERP, a chave da API e a empresa."
            : _conta.Email is { } email
                ? $"Projeto {c.ProjectId}, empresa {c.EmpresaId}. Conta do ERP: {email}."
                : $"Projeto {c.ProjectId}, empresa {c.EmpresaId}. Falta entrar com o usuário do ERP.";
        OnPropertyChanged(nameof(PodeImportar));
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(() =>
    {
        _config.Salvar(new ConfiguracaoErp { ProjectId = ErpProjectId, ApiKey = ErpApiKey, EmpresaId = ErpEmpresaId });
        Carregar();
        MostrarSucesso("Dados do ERP salvos. Clique em \"Importar do ERP\" para trazer as informações.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task ImportarAsync() => ExecutarAsync(async () =>
    {
        if (!_config.Carregar().EstaCompleta) throw new NexusLS.Core.Services.RegraDeNegocioException("Salve primeiro os dados do ERP.");
        Ocupado = true;
        try
        {
            if (!await _conta.RestaurarAsync() && PedirLogin() is null)
            {
                MostrarErro("Sem entrar no ERP não dá para importar. Clique em Importar de novo e informe o usuário do ERP.");
                return;
            }
            Resultado = "Lendo o ERP...";
            var r = await _criarImportador().ImportarAsync();
            Resultado = Resumir(r);
            MostrarSucesso(r.Novos + r.Atualizados == 0 ? "Nada mudou no ERP desde a última importação." : "Importação concluída.");
        }
        catch (FalhaDestinoException ex) { MostrarErro(ex.Message); }
        catch (FalhaLoginException ex) { MostrarErro(ex.Message); }
        finally { Ocupado = false; AtualizarSituacao(); }
    });

    [RelayCommand]
    private void SairDoErp()
    {
        _conta.Sair();
        AtualizarSituacao();
        MostrarSucesso("Você saiu da conta do ERP.");
    }

    internal static string Resumir(ResultadoImportacaoErp r)
    {
        var linhas = r.Colecoes.Select(c => $"{Nome(c.Colecao)}: {c.Novos} novos, {c.Atualizados} atualizados, {c.SemMudanca} sem mudança" +
                                            (c.Ignorados > 0 ? $", {c.Ignorados} ignorados" : ""));
        var texto = $"{r.Leituras} documentos lidos no ERP.\n" + string.Join("\n", linhas);
        if (r.Avisos.Count > 0) texto += "\nAvisos:\n" + string.Join("\n", r.Avisos.Take(8)) + (r.Avisos.Count > 8 ? $"\n(+{r.Avisos.Count - 8} avisos)" : "");
        return texto;
    }

    private static string Nome(string colecao) => colecao switch
    {
        "produtos" => "Produtos", "clientes" => "Clientes", "caixa_turnos" => "Turnos de caixa", "vendas" => "Vendas", _ => colecao
    };
}
