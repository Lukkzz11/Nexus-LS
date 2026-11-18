using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
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

    /// <summary>Banco para a importação do arquivo do Life (prévia e gravação). Preenchido pela janela principal.</summary>
    public IDbContextFactory<PdvDbContext>? FabricaBanco { get; set; }

    /// <summary>Abre a escolha do arquivo do Life e devolve o caminho (nulo = cancelou). Preenchida pela janela principal.</summary>
    public Func<string?> EscolherArquivoLife { get; set; } = () => null;

    private ArquivoLife? _arquivoLife;

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
    partial void OnOcupadoChanged(bool value) { OnPropertyChanged(nameof(PodeEditar)); OnPropertyChanged(nameof(PodeImportar)); OnPropertyChanged(nameof(PodeEscolherArquivo)); }

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
            Resultado = "Fazendo uma cópia de segurança...";
            await NexusLS.Core.Backup.CopiaAntesDeImportar.ExecutarAsync();
            Resultado = "Lendo o ERP...";
            var r = await _criarImportador().ImportarAsync(default, new Progress<string>(m => Resultado = m));
            Resultado = Resumir(r);
            MostrarSucesso(r.Novos + r.Atualizados == 0 ? "Nada mudou no ERP desde a última importação." : "Importação concluída.");
        }
        catch (FalhaDestinoException ex) { MostrarErro(ex.Message); }
        catch (FalhaLoginException ex) { MostrarErro(ex.Message); }
        finally { Ocupado = false; AtualizarSituacao(); }
    });

    [ObservableProperty] private string _previaArquivo = "";
    [ObservableProperty] private bool _temPreviaArquivo;

    public bool PodeEscolherArquivo => !Ocupado && !TemPreviaArquivo;

    partial void OnTemPreviaArquivoChanged(bool value) => OnPropertyChanged(nameof(PodeEscolherArquivo));

    /// <summary>Escolhe o arquivo do Life, confere o formato e mostra o que seria importado, sem gravar nada.</summary>
    [RelayCommand]
    private Task ImportarArquivoLifeAsync() => ExecutarAsync(async () =>
    {
        if (FabricaBanco is null) throw new NexusLS.Core.Services.RegraDeNegocioException("A importação de arquivo não está disponível.");
        var caminho = EscolherArquivoLife();
        if (string.IsNullOrWhiteSpace(caminho)) return;
        CancelarArquivoLife();
        Ocupado = true;
        try
        {
            Resultado = "Lendo o arquivo...";
            var arquivo = await Task.Run(() => ArquivoLife.LerArquivo(caminho));
            if (arquivo.TotalDocumentos == 0)
                throw new NexusLS.Core.Services.RegraDeNegocioException("O arquivo não tem nenhum dado que o Nexus importe (produtos, clientes, vendas, etc.).");
            Resultado = "Conferindo o que mudaria (nada é gravado ainda)...";
            var previa = await Task.Run(() => ImportacaoArquivoLife.PreverAsync(arquivo, FabricaBanco));
            _arquivoLife = arquivo;
            PreviaArquivo = ResumirPrevia(arquivo, previa);
            TemPreviaArquivo = true;
            Resultado = "";
            MostrarSucesso("Confira o resumo abaixo e confirme para importar.");
        }
        catch (NexusLS.Core.Services.RegraDeNegocioException ex) { Resultado = ""; MostrarErro(ex.Message); }
        finally { Ocupado = false; }
    });

    [RelayCommand]
    private Task ConfirmarArquivoLifeAsync() => ExecutarAsync(async () =>
    {
        if (_arquivoLife is null || FabricaBanco is null) return;
        var arquivo = _arquivoLife;
        Ocupado = true;
        try
        {
            Resultado = "Fazendo uma cópia de segurança...";
            await NexusLS.Core.Backup.CopiaAntesDeImportar.ExecutarAsync();
            Resultado = "Importando...";
            var fabrica = FabricaBanco;
            var r = await Task.Run(() => ImportacaoArquivoLife.ImportarAsync(arquivo, fabrica, default, new Progress<string>(m => Resultado = m)));
            Resultado = Resumir(r);
            MostrarSucesso(r.Novos + r.Atualizados == 0 ? "Nada mudou: tudo do arquivo já estava importado." : "Importação do arquivo concluída.");
            _arquivoLife = null;
            TemPreviaArquivo = false;
            PreviaArquivo = "";
        }
        finally { Ocupado = false; }
    });

    [RelayCommand]
    private void CancelarArquivoLife()
    {
        _arquivoLife = null;
        TemPreviaArquivo = false;
        PreviaArquivo = "";
    }

    internal static string ResumirPrevia(ArquivoLife arquivo, ResultadoImportacaoErp previa)
    {
        var cab = $"Arquivo do Life (versão {arquivo.Versao})" + (arquivo.EmpresaId is null ? "" : $", empresa {arquivo.EmpresaId}")
                  + (arquivo.GeradoEm is { } g ? $", gerado em {g.ToLocalTime():dd/MM/yyyy HH:mm}" : "") + $": {arquivo.TotalDocumentos} documentos.";
        var linhas = previa.Colecoes.Where(c => c.Lidos > 0).Select(c =>
            $"{Nome(c.Colecao)}: {c.Lidos} no arquivo, {c.Novos} novos, {c.Atualizados} atualizados, {c.SemMudanca} sem mudança, {c.Ignorados} ignorados");
        var texto = cab + "\n" + string.Join("\n", linhas);
        if (arquivo.NaoImportadas.Count > 0)
            texto += "\nNão importados nesta versão: " + string.Join(", ", arquivo.NaoImportadas.Select(n => $"{n.Colecao} ({n.Quantidade})"));
        var avisos = arquivo.Avisos.Concat(previa.Avisos).ToList();
        if (avisos.Count > 0) texto += "\nAvisos:\n" + string.Join("\n", avisos.Take(8)) + (avisos.Count > 8 ? $"\n(+{avisos.Count - 8} avisos)" : "");
        return texto;
    }

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
        "produtos" => "Produtos", "clientes" => "Clientes", "caixa_turnos" => "Turnos de caixa", "vendas" => "Vendas", "contas_receber" => "Dívidas (títulos a prazo)", "pedidos" => "Pedidos", "transacoes_financeiras" => "Gastos e entradas", _ => colecao
    };
}
