using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Um vendedor na lista de percentuais. Vazio = usa o percentual padrão.</summary>
public partial class VendedorComissao : ObservableObject
{
    public VendedorComissao(string nome, string percentualTexto)
    {
        Nome = nome;
        PercentualTexto = percentualTexto;
    }

    public string Nome { get; }
    [ObservableProperty] private string _percentualTexto;
}

public record OpcaoMesComissao(string Texto, DateTime Inicio, DateTime Fim);

/// <summary>Uma linha do fechamento, com os textos prontos.</summary>
public record LinhaComissaoTela(string Vendedor, string Vendas, string Vendido, string Devolvido, string Percentual, string Comissao);

/// <summary>Página "Comissões": regra de percentuais e fechamento do mês (vendas líquidas de devolução por vendedor).</summary>
public partial class ConfiguracoesViewModel
{
    private ComissaoService? _comissoes;
    private FuncionarioService? _funcionariosComissao;

    public bool TemComissoes => _comissoes is not null && _seguranca is not null;

    [ObservableProperty] private string _comissaoPadraoTexto = "0";
    [ObservableProperty] private bool _comissaoDescontaDevolucoes = true;
    [ObservableProperty] private string _mensagemComissao = "";
    [ObservableProperty] private bool _mensagemComissaoEhErro;
    [ObservableProperty] private OpcaoMesComissao? _mesSelecionado;
    [ObservableProperty] private string _resumoComissao = "";
    [ObservableProperty] private bool _temResultadoComissao;

    public ObservableCollection<VendedorComissao> VendedoresComissao { get; } = new();
    public ObservableCollection<OpcaoMesComissao> MesesComissao { get; } = new();
    public ObservableCollection<LinhaComissaoTela> ResultadoComissao { get; } = new();

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public void UsarComissoes(ComissaoService comissoes, FuncionarioService funcionarios)
    {
        _comissoes = comissoes;
        _funcionariosComissao = funcionarios;

        MesesComissao.Clear();
        var primeiro = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var i = 0; i < 12; i++)
        {
            var inicio = primeiro.AddMonths(-i);
            MesesComissao.Add(new OpcaoMesComissao(inicio.ToString("MMMM 'de' yyyy", Br), inicio, inicio.AddMonths(1).AddDays(-1)));
        }
        MesSelecionado = MesesComissao[0];

        if (_seguranca is not null)
        {
            var c = _seguranca.Carregar();
            ComissaoPadraoTexto = TextoBr.Numero(c.ComissaoPadraoPercent, "0.##");
            ComissaoDescontaDevolucoes = c.ComissaoDescontaDevolucoes;
        }
        OnPropertyChanged(nameof(TemComissoes));
        _ = CarregarVendedoresAsync();
    }

    private async Task CarregarVendedoresAsync()
    {
        try
        {
            var c = _seguranca?.Carregar();
            var nomes = (await _funcionariosComissao!.ListarAsync()).Select(f => f.Nome).ToList();
            if (c is not null) nomes.AddRange(c.ComissaoPorVendedor.Keys);   // quem já tinha percentual não some da lista
            VendedoresComissao.Clear();
            foreach (var nome in nomes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                VendedoresComissao.Add(new VendedorComissao(nome,
                    c is not null && c.ComissaoPorVendedor.TryGetValue(nome, out var p) ? TextoBr.Numero(p, "0.##") : ""));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MensagemComissaoEhErro = true;
            MensagemComissao = "Não foi possível carregar a lista de funcionários: " + ex.Message;
        }
    }

    /// <summary>Passa o que está na tela para a configuração (sem gravar). Usado ao salvar e ao calcular, para o cálculo mostrar o que você vê.</summary>
    private void AplicarComissao(NexusLS.Core.Seguranca.ConfiguracaoSeguranca c)
    {
        c.ComissaoPadraoPercent = LerPercentual(ComissaoPadraoTexto, "Comissão padrão");
        c.ComissaoDescontaDevolucoes = ComissaoDescontaDevolucoes;
        c.ComissaoPorVendedor = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in VendedoresComissao.Where(v => !string.IsNullOrWhiteSpace(v.PercentualTexto)))
            c.ComissaoPorVendedor[v.Nome] = LerPercentual(v.PercentualTexto, $"Comissão de {v.Nome}");
    }

    [RelayCommand]
    private void SalvarComissao() => SalvarRegras(
        AplicarComissao,
        (m, erro) => { MensagemComissaoEhErro = erro; MensagemComissao = m; },
        () => "Salvo. A comissão do mês já usa essa regra.");

    [RelayCommand]
    private async Task CalcularComissaoAsync()
    {
        if (_comissoes is null || _seguranca is null || MesSelecionado is null) return;
        try
        {
            var cfg = _seguranca.Carregar();
            AplicarComissao(cfg);   // calcula com o que está na tela, mesmo sem ter salvado
            var linhas = await _comissoes.CalcularAsync(MesSelecionado.Inicio, MesSelecionado.Fim, cfg);
            ResultadoComissao.Clear();
            foreach (var l in linhas)
                ResultadoComissao.Add(new LinhaComissaoTela(l.Vendedor, l.Vendas.ToString(), Dinheiro.Formatar(l.VendidoCentavos),
                    l.DevolvidoCentavos > 0 ? "- " + Dinheiro.Formatar(l.DevolvidoCentavos) : "—",
                    TextoBr.Numero(l.Percentual, "0.##") + "%", Dinheiro.Formatar(l.ComissaoCentavos)));
            TemResultadoComissao = true;
            ResumoComissao = linhas.Count == 0
                ? "Nenhuma venda nesse mês."
                : $"Total de comissões: {Dinheiro.Formatar(linhas.Sum(l => l.ComissaoCentavos))} sobre {Dinheiro.Formatar(linhas.Sum(l => l.BaseCentavos))} de vendas líquidas. " +
                  "A base é o valor dos itens menos o desconto, sem o acréscimo do cartão.";
            MensagemComissaoEhErro = false;
            MensagemComissao = "";
        }
        catch (RegraDeNegocioException ex)
        {
            MensagemComissaoEhErro = true;
            MensagemComissao = ex.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MensagemComissaoEhErro = true;
            MensagemComissao = "Não foi possível calcular: " + ex.Message;
        }
    }

    [RelayCommand]
    private void CopiarComissao()
    {
        if (ResultadoComissao.Count == 0) return;
        var linhas = new List<string> { $"Comissões - {MesSelecionado?.Texto}", "Vendedor\tVendas\tVendido\tDevolvido\t%\tComissão" };
        linhas.AddRange(ResultadoComissao.Select(l => $"{l.Vendedor}\t{l.Vendas}\t{l.Vendido}\t{l.Devolvido}\t{l.Percentual}\t{l.Comissao}"));
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, linhas));
            MensagemComissaoEhErro = false;
            MensagemComissao = "Copiado. Cole numa planilha.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MensagemComissaoEhErro = true;
            MensagemComissao = "A área de transferência está ocupada. Tente de novo.";
        }
    }
}
