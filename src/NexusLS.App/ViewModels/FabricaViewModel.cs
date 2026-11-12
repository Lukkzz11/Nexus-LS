using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public sealed record LinhaOrdem(OrdemProducao Ordem)
{
    public string Codigo => Ordem.Codigo;
    public string Modelo => Ordem.Modelo;
    public string Etapa => FabricaViewModel.NomeEtapa(Ordem.Status);
    public int Total => Ordem.TotalPlanejado;
    public string Grade => FabricaViewModel.GradeTexto(Ordem.Grade);
    public string Previsao => Ordem.PrevisaoTermino?.ToString("dd/MM/yyyy") ?? "";
    public string CustoTotal => Dinheiro.Formatar(Ordem.CustoEstimadoTotalCentavos);
}

public sealed record LinhaMateria(MateriaPrima Materia)
{
    public string Nome => Materia.Nome;
    public string Tipo => Materia.Tipo;
    public string Estoque => $"{Materia.EstoqueAtual.ToString("0.###", TextoBr.Cultura)} {Materia.Unidade}";
    public string Minimo => Materia.EstoqueMinimo.ToString("0.###", TextoBr.Cultura);
    public string Custo => Dinheiro.Formatar(Materia.CustoUnitarioCentavos);
    public string Situacao => Materia.AbaixoDoMinimo ? "Abaixo do mínimo" : "";
}

public sealed record LinhaPerda(PerdaProducao Perda)
{
    public string Data => Perda.DataHora.ToString("dd/MM/yyyy HH:mm");
    public string Ordem => Perda.OrdemCodigo ?? "Geral";
    public string Motivo => FabricaViewModel.NomeMotivo(Perda.Motivo);
    public string Material => Perda.MaterialNome;
    public string Quantidade => $"{Perda.Quantidade.ToString("0.###", TextoBr.Cultura)} {Perda.Unidade}";
    public string Custo => Dinheiro.Formatar(Perda.CustoEstimadoCentavos);
}

public sealed record OpcaoMotivo(MotivoPerda Valor, string Texto);

/// <summary>
/// Fábrica: ordens de produção (etapas e transferência para a loja), matéria-prima e perdas. Os campos de grade são texto
/// no formato "P:10, M:20, G:5"; a conta e a gravação ficam no <see cref="ProducaoService"/>.
/// </summary>
public partial class FabricaViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly ProducaoService _servico;

    public FabricaViewModel(ProducaoService servico)
    {
        _servico = servico;
        _ = CarregarAsync();
    }

    public ObservableCollection<LinhaOrdem> Ordens { get; } = new();
    public ObservableCollection<LinhaMateria> Materias { get; } = new();
    public ObservableCollection<LinhaPerda> Perdas { get; } = new();
    public ObservableCollection<MateriaPrima> MateriasAtivas { get; } = new();
    public ObservableCollection<OrdemProducao> OrdensAbertas { get; } = new();

    public IReadOnlyList<OpcaoMotivo> Motivos { get; } =
        Enum.GetValues<MotivoPerda>().Select(m => new OpcaoMotivo(m, NomeMotivo(m))).ToList();

    public IReadOnlyList<string> TiposMateria { get; } =
        new[] { "Tecido / Malha", "Aviamento / Botão / Ilhós", "Zíper", "Linha de costura", "Etiqueta", "Elástico / Cós", "Embalagem" };

    [ObservableProperty] private string _resumoAlertas = "";

    // ---- ordens
    [ObservableProperty] private LinhaOrdem? _ordemSelecionada;
    [ObservableProperty] private string _opModelo = "";
    [ObservableProperty] private string _opReferencia = "";
    [ObservableProperty] private string _opCategoria = "";
    [ObservableProperty] private string _opGrade = "";
    [ObservableProperty] private string _opCustoPeca = "";
    [ObservableProperty] private string _opTecido = "";
    [ObservableProperty] private DateTime? _opPrevisao;
    [ObservableProperty] private string _opObservacoes = "";

    // ---- transferência
    [ObservableProperty] private string _trGrade = "";
    [ObservableProperty] private string _trNome = "";
    [ObservableProperty] private string _trPrecoVarejo = "";
    [ObservableProperty] private string _trPrecoAtacado = "";

    // ---- matéria-prima
    [ObservableProperty] private LinhaMateria? _materiaSelecionada;
    [ObservableProperty] private string _mpNome = "";
    [ObservableProperty] private string _mpTipo = "";
    [ObservableProperty] private string _mpUnidade = "metros";
    [ObservableProperty] private string _mpMinimo = "";
    [ObservableProperty] private string _mpCusto = "";
    [ObservableProperty] private string _mpFornecedor = "";
    [ObservableProperty] private string _mpLocal = "";
    [ObservableProperty] private string _mpEstoqueInicial = "";
    [ObservableProperty] private string _mpMovimento = "";

    // ---- perdas
    [ObservableProperty] private OpcaoMotivo? _pdMotivo;
    [ObservableProperty] private MateriaPrima? _pdMateria;
    [ObservableProperty] private string _pdMaterialLivre = "";
    [ObservableProperty] private OrdemProducao? _pdOrdem;
    [ObservableProperty] private string _pdQuantidade = "";
    [ObservableProperty] private string _pdCusto = "";
    [ObservableProperty] private string _pdObservacao = "";
    [ObservableProperty] private bool _pdBaixar = true;

    partial void OnOrdemSelecionadaChanged(LinhaOrdem? value)
    {
        TrGrade = value is null ? "" : GradeTexto(value.Ordem.Grade);
        TrNome = value?.Ordem.Modelo ?? "";
    }

    partial void OnMateriaSelecionadaChanged(LinhaMateria? value)
    {
        if (value is not { Materia: var m }) return;
        MpNome = m.Nome; MpTipo = m.Tipo; MpUnidade = m.Unidade; MpMinimo = m.EstoqueMinimo.ToString("0.###", PtBr);
        MpCusto = Dinheiro.FormatarParaEdicao(m.CustoUnitarioCentavos); MpFornecedor = m.Fornecedor ?? ""; MpLocal = m.Localizacao ?? "";
    }

    // ================= nomes e textos =================

    public static string NomeEtapa(StatusOrdemProducao s) => s switch
    {
        StatusOrdemProducao.Planejamento => "1. Planejamento",
        StatusOrdemProducao.Corte => "2. Corte",
        StatusOrdemProducao.Costura => "3. Costura e montagem",
        StatusOrdemProducao.Estampa => "4. Estamparia / silk",
        StatusOrdemProducao.Acabamento => "5. Revisão e acabamento",
        StatusOrdemProducao.Concluida => "6. Concluída (na loja)",
        _ => "Cancelada"
    };

    public static string NomeMotivo(MotivoPerda m) => m switch
    {
        MotivoPerda.FalhaNoCorte => "Falha no corte",
        MotivoPerda.DefeitoNoTecido => "Defeito no tecido",
        MotivoPerda.ErroNaCostura => "Erro na costura",
        MotivoPerda.FalhaNaEstampa => "Falha na estampa",
        MotivoPerda.SobraDeRolo => "Sobra de rolo",
        _ => "Outro"
    };

    public static string GradeTexto(IEnumerable<OrdemProducaoGrade> grade) =>
        string.Join(", ", grade.Select(g => $"{g.Tamanho}:{g.Quantidade}"));

    /// <summary>"P:10, M:20" (ou "P=10 M=20") vira tamanho → quantidade. Texto fora do formato é recusado com a mensagem certa.</summary>
    public static Dictionary<string, int> LerGrade(string? texto)
    {
        var grade = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(texto)) return grade;
        foreach (var parte in Regex.Split(texto.Trim(), @"[,;\n]+|\s{2,}").Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var m = Regex.Match(parte.Trim(), @"^(?<tam>.+?)\s*[:=]\s*(?<qtd>\d+)$");
            if (!m.Success) throw new RegraDeNegocioException($"Grade \"{parte.Trim()}\": use tamanho:quantidade, por exemplo P:10, M:20.");
            var tam = m.Groups["tam"].Value.Trim();
            grade[tam] = grade.GetValueOrDefault(tam) + int.Parse(m.Groups["qtd"].Value, CultureInfo.InvariantCulture);
        }
        return grade;
    }

    private static decimal Numero(string? texto, string campo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        var s = texto.Replace("R$", "").Replace(" ", "").Replace(" ", "");
        if (!s.Contains(',') && Regex.IsMatch(s, @"^\d+\.\d{1,3}$")) s = s.Replace('.', ',');
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, PtBr, out var v)
            ? v : throw new RegraDeNegocioException($"{campo}: \"{texto}\" não é um número válido.");
    }

    private static long Centavos(string? texto, string campo) =>
        (long)Math.Round(Numero(texto, campo) * 100m, MidpointRounding.AwayFromZero);

    // ================= carregar =================

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var selOrdem = OrdemSelecionada?.Ordem.Id;
        var selMateria = MateriaSelecionada?.Materia.Id;
        var ordens = await _servico.ListarOrdensAsync();
        var materias = await _servico.ListarMateriasAsync();
        var perdas = await _servico.ListarPerdasAsync();

        Ordens.Clear(); foreach (var o in ordens) Ordens.Add(new LinhaOrdem(o));
        Materias.Clear(); foreach (var m in materias) Materias.Add(new LinhaMateria(m));
        Perdas.Clear(); foreach (var p in perdas) Perdas.Add(new LinhaPerda(p));
        MateriasAtivas.Clear(); foreach (var m in materias) MateriasAtivas.Add(m);
        OrdensAbertas.Clear();
        foreach (var o in ordens.Where(o => o.Status is not (StatusOrdemProducao.Concluida or StatusOrdemProducao.Cancelada))) OrdensAbertas.Add(o);

        OrdemSelecionada = Ordens.FirstOrDefault(o => o.Ordem.Id == selOrdem);
        MateriaSelecionada = Materias.FirstOrDefault(m => m.Materia.Id == selMateria);
        PdMotivo ??= Motivos[0];

        var baixas = materias.Where(m => m.AbaixoDoMinimo).Select(m => m.Nome).ToList();
        ResumoAlertas = baixas.Count == 0 ? "" : $"Matéria-prima abaixo do mínimo: {string.Join(", ", baixas)}.";
    });

    // ================= ordens =================

    [RelayCommand]
    private Task CriarOrdemAsync() => ExecutarAsync(async () =>
    {
        var ordem = await _servico.CriarOrdemAsync(new DadosOrdemProducao(OpModelo, LerGrade(OpGrade), OpReferencia, OpCategoria, OpPrevisao,
            OpTecido, null, Centavos(OpCustoPeca, "Custo por peça"), OpObservacoes));
        OpModelo = OpReferencia = OpCategoria = OpGrade = OpCustoPeca = OpTecido = OpObservacoes = "";
        OpPrevisao = null;
        await CarregarAsync();
        MostrarSucesso($"Ordem {ordem.Codigo} criada.");
    });

    [RelayCommand]
    private Task AvancarEtapaAsync() => ExecutarAsync(async () =>
    {
        var ordem = Selecionada();
        var nova = await _servico.AvancarEtapaAsync(ordem.Id);
        await CarregarAsync();
        MostrarSucesso($"{nova.Codigo} agora está em: {NomeEtapa(nova.Status)}.");
    });

    [RelayCommand]
    private Task CancelarOrdemAsync() => ExecutarAsync(async () =>
    {
        var ordem = Selecionada();
        await _servico.CancelarOrdemAsync(ordem.Id);
        await CarregarAsync();
        MostrarSucesso($"{ordem.Codigo} cancelada.");
    });

    [RelayCommand]
    private Task TransferirParaLojaAsync() => ExecutarAsync(async () =>
    {
        var ordem = Selecionada();
        var r = await _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(LerGrade(TrGrade), Nome: TrNome,
            PrecoVendaCentavos: Centavos(TrPrecoVarejo, "Preço de varejo"), PrecoAtacadoCentavos: Centavos(TrPrecoAtacado, "Preço de atacado")));
        TrPrecoVarejo = TrPrecoAtacado = "";
        await CarregarAsync();
        MostrarSucesso($"{r.TotalPecas} peça(s) de \"{r.ProdutoNome}\" entraram no estoque da loja{(r.ProdutoNovo ? " (produto novo)" : "")}. {ordem.Codigo} concluída.");
    });

    private OrdemProducao Selecionada() =>
        OrdemSelecionada?.Ordem ?? throw new RegraDeNegocioException("Escolha uma ordem de produção na lista.");

    // ================= matéria-prima =================

    private DadosMateriaPrima LerMateria() =>
        new(MpNome, MpTipo, MpUnidade, Numero(MpMinimo, "Estoque mínimo"), Centavos(MpCusto, "Custo"), MpFornecedor, MpLocal);

    [RelayCommand]
    private Task CadastrarMateriaAsync() => ExecutarAsync(async () =>
    {
        var m = await _servico.CadastrarMateriaAsync(LerMateria(), Numero(MpEstoqueInicial, "Estoque inicial"));
        LimparMateria();
        await CarregarAsync();
        MostrarSucesso($"\"{m.Nome}\" cadastrada.");
    });

    [RelayCommand]
    private Task SalvarMateriaAsync() => ExecutarAsync(async () =>
    {
        var m = (MateriaSelecionada ?? throw new RegraDeNegocioException("Escolha uma matéria-prima na lista.")).Materia;
        await _servico.EditarMateriaAsync(m.Id, LerMateria());
        await CarregarAsync();
        MostrarSucesso("Matéria-prima atualizada.");
    });

    [RelayCommand] private Task EntradaMateriaAsync() => MoverEstoque(+1);
    [RelayCommand] private Task SaidaMateriaAsync() => MoverEstoque(-1);

    private Task MoverEstoque(int sinal) => ExecutarAsync(async () =>
    {
        var m = (MateriaSelecionada ?? throw new RegraDeNegocioException("Escolha uma matéria-prima na lista.")).Materia;
        var qtd = Numero(MpMovimento, "Quantidade");
        if (qtd <= 0) throw new RegraDeNegocioException("Informe a quantidade, maior que zero.");
        var r = await _servico.AjustarEstoqueMateriaAsync(m.Id, sinal * qtd);
        MpMovimento = "";
        await CarregarAsync();
        MostrarSucesso($"\"{r.Nome}\": agora {r.EstoqueAtual.ToString("0.###", PtBr)} {r.Unidade}.");
    });

    [RelayCommand]
    private Task DesativarMateriaAsync() => ExecutarAsync(async () =>
    {
        var m = (MateriaSelecionada ?? throw new RegraDeNegocioException("Escolha uma matéria-prima na lista.")).Materia;
        await _servico.DefinirMateriaAtivaAsync(m.Id, false);
        LimparMateria();
        await CarregarAsync();
        MostrarSucesso($"\"{m.Nome}\" desativada.");
    });

    private void LimparMateria()
    {
        MpNome = MpTipo = MpMinimo = MpCusto = MpFornecedor = MpLocal = MpEstoqueInicial = MpMovimento = "";
        MpUnidade = "metros";
    }

    // ================= perdas =================

    [RelayCommand]
    private Task RegistrarPerdaAsync() => ExecutarAsync(async () =>
    {
        var perda = await _servico.RegistrarPerdaAsync(new DadosPerda(PdMotivo?.Valor ?? MotivoPerda.Outro, Numero(PdQuantidade, "Quantidade"),
            PdMaterialLivre, PdMateria?.Id, PdOrdem?.Id, null,
            string.IsNullOrWhiteSpace(PdCusto) ? null : Centavos(PdCusto, "Custo estimado"), PdObservacao, PdBaixar));
        PdQuantidade = PdCusto = PdObservacao = PdMaterialLivre = "";
        PdMateria = null; PdOrdem = null;
        await CarregarAsync();
        MostrarSucesso($"Perda registrada: {perda.Quantidade.ToString("0.###", PtBr)} {perda.Unidade} de {perda.MaterialNome} ({Dinheiro.Formatar(perda.CustoEstimadoCentavos)}).");
    });
}
