using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Páginas "Segurança", "Caixa" e os descontos de "Vendas": regras guardadas no arquivo de segurança deste computador.</summary>
public partial class ConfiguracoesViewModel
{
    private ConfiguracaoSegurancaStore? _seguranca;

    public bool TemSeguranca => _seguranca is not null;

    // Segurança
    [ObservableProperty] private string _minutosInatividadeTexto = "0";
    [ObservableProperty] private bool _esconderGestaoSemAcesso;
    [ObservableProperty] private string _mensagemSeguranca = "";
    [ObservableProperty] private bool _mensagemSegurancaEhErro;

    // Identificação desta loja (um computador = uma loja)
    [ObservableProperty] private string _lojaNomeTexto = ConfiguracaoSeguranca.LojaNomePadrao;
    [ObservableProperty] private string _lojaIdTexto = "";
    [ObservableProperty] private string _mensagemLojaId = "";
    [ObservableProperty] private bool _mensagemLojaIdEhErro;

    // Caixa
    [ObservableProperty] private string _sangriaMaximaTexto = "";
    [ObservableProperty] private bool _pinGerenteParaCancelar;
    [ObservableProperty] private string _mensagemCaixaRegras = "";
    [ObservableProperty] private bool _mensagemCaixaRegrasEhErro;

    // Descontos (página Vendas)
    [ObservableProperty] private string _descontoMaximoTexto = "0";
    [ObservableProperty] private string _mensagemDescontoRegras = "";
    [ObservableProperty] private bool _mensagemDescontoRegrasEhErro;

    // Crediário
    [ObservableProperty] private string _multaAtrasoTexto = "0";
    [ObservableProperty] private string _jurosMesTexto = "0";
    [ObservableProperty] private string _limiteCreditoPadraoTexto = "";
    [ObservableProperty] private string _mensagemCrediario = "";
    [ObservableProperty] private bool _mensagemCrediarioEhErro;

    public string LimiteDePinTexto =
        $"Depois de {FuncionarioService.MaxTentativas} PINs errados seguidos, o funcionário fica bloqueado por {(int)FuncionarioService.TempoDeBloqueio.TotalSeconds} segundos (mesmo se o programa for fechado e aberto). Cada entrada e cada erro ficam registrados em Atividades.";

    public void UsarSeguranca(ConfiguracaoSegurancaStore store)
    {
        _seguranca = store;
        var c = store.Carregar();
        MinutosInatividadeTexto = c.MinutosInatividade.ToString();
        EsconderGestaoSemAcesso = c.EsconderGestaoSemAcesso;
        LojaNomeTexto = c.LojaNome;
        LojaIdTexto = c.LojaId;
        SangriaMaximaTexto = c.SangriaMaximaSemGerenteCentavos > 0 ? Dinheiro.Formatar(c.SangriaMaximaSemGerenteCentavos).Replace("R$", "").Trim() : "";
        PinGerenteParaCancelar = c.PinGerenteParaCancelar;
        DescontoMaximoTexto = c.DescontoMaximoSemGerentePercent.ToString();
        MultaAtrasoTexto = TextoBr.Numero(c.MultaAtrasoPercent, "0.##");
        JurosMesTexto = TextoBr.Numero(c.JurosMesPercent, "0.##");
        LimiteCreditoPadraoTexto = c.LimiteCreditoPadraoCentavos > 0 ? Dinheiro.FormatarParaEdicao(c.LimiteCreditoPadraoCentavos) : "";
        OnPropertyChanged(nameof(TemSeguranca));
    }

    /// <summary>Lê o arquivo, deixa o <paramref name="mudar"/> alterar só o que a página mexe e grava (as outras páginas não perdem o que já salvaram).</summary>
    private bool SalvarRegras(Action<ConfiguracaoSeguranca> mudar, Action<string, bool> mensagem, Func<string> sucesso)
    {
        if (_seguranca is null) return false;
        try
        {
            var c = _seguranca.Carregar();
            mudar(c);
            _seguranca.Salvar(c);
            Sessao.Notificar();   // o menu confere de novo se mostra o botão Gestão
            mensagem(sucesso(), false);
            return true;
        }
        catch (RegraDeNegocioException ex)
        {
            mensagem(ex.Message, true);
            return false;
        }
    }

    [RelayCommand]
    private void SalvarSeguranca() => SalvarRegras(
        c =>
        {
            if (!int.TryParse(MinutosInatividadeTexto.Trim(), out var minutos))
                throw new RegraDeNegocioException("Digite os minutos só com números (0 desliga o bloqueio).");
            c.MinutosInatividade = minutos;
            c.EsconderGestaoSemAcesso = EsconderGestaoSemAcesso;
        },
        (m, erro) => { MensagemSegurancaEhErro = erro; MensagemSeguranca = m; },
        () => int.Parse(MinutosInatividadeTexto.Trim()) == 0
            ? "Salvo. O bloqueio por inatividade está desligado."
            : $"Salvo. O programa pede o PIN depois de {MinutosInatividadeTexto.Trim()} minuto(s) parado.");

    [RelayCommand]
    private void SalvarLojaId() => SalvarRegras(
        c => c.LojaNome = (LojaNomeTexto ?? "").Trim(),
        (m, erro) => { MensagemLojaIdEhErro = erro; MensagemLojaId = m; },
        () => $"Salvo. Este computador agora é \"{LojaNomeTexto.Trim()}\".");

    [RelayCommand]
    private void SalvarCaixaRegras() => SalvarRegras(
        c =>
        {
            long valor = 0;
            if (!string.IsNullOrWhiteSpace(SangriaMaximaTexto) && !Dinheiro.TentarConverter(SangriaMaximaTexto, out valor))
                throw new RegraDeNegocioException("Valor da sangria inválido. Digite algo como 300,00 (em branco = sem limite).");
            c.SangriaMaximaSemGerenteCentavos = valor;
            c.PinGerenteParaCancelar = PinGerenteParaCancelar;
        },
        (m, erro) => { MensagemCaixaRegrasEhErro = erro; MensagemCaixaRegras = m; },
        () => "Salvo. As regras valem já na próxima operação.");

    private static decimal LerPercentual(string texto, string nome)
    {
        var t = string.IsNullOrWhiteSpace(texto) ? "0" : texto.Trim().Replace("%", "");
        if (!decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), out var v))
            throw new RegraDeNegocioException($"{nome}: digite só números, como 2 ou 1,5.");
        return v;
    }

    [RelayCommand]
    private void SalvarCrediario() => SalvarRegras(
        c =>
        {
            c.MultaAtrasoPercent = LerPercentual(MultaAtrasoTexto, "Multa por atraso");
            c.JurosMesPercent = LerPercentual(JurosMesTexto, "Juros ao mês");
            long limite = 0;
            if (!string.IsNullOrWhiteSpace(LimiteCreditoPadraoTexto) && !Dinheiro.TentarConverter(LimiteCreditoPadraoTexto, out limite))
                throw new RegraDeNegocioException("Limite de crédito padrão inválido. Digite algo como 500,00 (em branco = sem limite).");
            c.LimiteCreditoPadraoCentavos = limite;
        },
        (m, erro) => { MensagemCrediarioEhErro = erro; MensagemCrediario = m; },
        () => "Salvo. A multa e os juros aparecem na ficha de quem está com título vencido; o limite vale para clientes novos.");

    [RelayCommand]
    private void SalvarDescontoRegras() => SalvarRegras(
        c =>
        {
            if (!int.TryParse(DescontoMaximoTexto.Trim(), out var percentual))
                throw new RegraDeNegocioException("Digite o percentual só com números (0 = sem limite).");
            c.DescontoMaximoSemGerentePercent = percentual;
        },
        (m, erro) => { MensagemDescontoRegrasEhErro = erro; MensagemDescontoRegras = m; },
        () => int.Parse(DescontoMaximoTexto.Trim()) == 0
            ? "Salvo. Não há limite de desconto sem gerente."
            : $"Salvo. Desconto acima de {DescontoMaximoTexto.Trim()}% precisa da autorização de um gerente.");
}
