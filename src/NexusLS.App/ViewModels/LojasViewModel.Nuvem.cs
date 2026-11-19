using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Nuvem;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha do comparativo das lojas lidas da nuvem.</summary>
public sealed record LinhaComparativo(string Nome, string Codigo, string Faturamento, string Vendas, string TicketMedio, string Atualizacao, bool EstaLoja);

/// <summary>Uma loja na edição de preço: o administrador digita o preço de varejo (e o de atacado, opcional) daquele produto para a loja.</summary>
public sealed partial class LinhaPrecoLoja : ObservableObject
{
    public LinhaPrecoLoja(string lojaId, string nome) { LojaId = lojaId; Nome = nome; }
    public string LojaId { get; }
    public string Nome { get; }
    [ObservableProperty] private string _varejo = "";
    [ObservableProperty] private string _atacado = "";
    [ObservableProperty] private string _situacao = "";
}

/// <summary>Os textos e regras de "Receber preços da nuvem", usados pela aba Lojas e pelas Configurações.</summary>
public static class NuvemApp
{
    /// <summary>Baixa e aplica os preços desta loja. Precisa da permissão Estoque ou da autorização de um gerente (alçada).</summary>
    public static async Task<(string Texto, bool Erro)> ReceberPrecosAsync(ServicoNuvemLoja nuvem, Func<string, bool>? autorizar = null,
        Func<bool>? podeEstoque = null, Action<string>? progresso = null)
    {
        var pode = (podeEstoque ?? (() => Sessao.Pode(Permissao.Estoque)))();
        if (!pode && !(autorizar ?? Alcada.Solicitar)("Receber os preços da nuvem muda os preços de venda desta loja."))
            return ("Receber preços da nuvem precisa da permissão de Estoque ou da autorização de um gerente.", true);
        try
        {
            var r = await nuvem.ReceberPrecosAsync(progresso);
            var t = r.Aplicados + r.Removidos == 0
                ? "Os preços desta loja já estavam em dia."
                : $"Preços recebidos: {r.Aplicados} aplicado(s), {r.Removidos} voltaram ao preço do cadastro.";
            return (r.Ignorados > 0 ? t + $" {r.Ignorados} preço(s) inválido(s) foram ignorados." : t, false);
        }
        catch (FalhaNuvemException ex) { return (ex.Message + " Os preços de hoje continuam como estavam.", true); }
        catch (RegraDeNegocioException ex) { return (ex.Message, true); }
    }
}

/// <summary>
/// A parte "nuvem" da aba Lojas: botão Compartilhar com a nuvem (loja deste computador), comparativo das lojas lidas da nuvem e,
/// para o administrador, o preço por loja. Nada é enviado sem o clique. Usuário de loja só vê a própria loja.
/// </summary>
public partial class LojasViewModel
{
    private ServicoNuvemLoja? _nuvem;
    private Func<bool> _souAdmin = () => Sessao.EhAdministrador;
    private Func<bool> _podeCompartilhar = () => Sessao.Pode(Permissao.Financeiro);
    private Func<string, bool> _autorizar = m => Alcada.Solicitar(m);
    private ProdutoService? _produtos;
    private Func<string> _operador = () => Sessao.Atual?.Nome ?? "";
    private Produto? _produtoDoPreco;

    public ObservableCollection<LinhaComparativo> Comparativo { get; } = new();
    public ObservableCollection<LinhaPrecoLoja> PrecosPorLoja { get; } = new();

    [ObservableProperty] private bool _temNuvem;
    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private bool _podeCompartilharAgora;
    [ObservableProperty] private bool _ocupadoNuvem;
    [ObservableProperty] private string _empresaNuvem = "";
    [ObservableProperty] private string _progressoNuvem = "";
    [ObservableProperty] private string _ultimoEnvioTexto = "";
    [ObservableProperty] private string _avisoComparativo = "";
    [ObservableProperty] private bool _comparativoDoCache;
    [ObservableProperty] private bool _precisaConfirmarEnvio;
    [ObservableProperty] private string _buscaProdutoPreco = "";
    [ObservableProperty] private string _produtoDoPrecoNome = "";
    [ObservableProperty] private string _mensagemPreco = "";
    [ObservableProperty] private bool _mensagemPrecoEhErro;

    public bool TemComparativo => Comparativo.Count > 0;

    /// <summary>Liga a tela à nuvem das lojas. Sem isto a aba continua como era (só a loja deste computador).</summary>
    public void UsarNuvem(ServicoNuvemLoja nuvem, ProdutoService? produtos = null, Func<bool>? ehAdministrador = null,
        Func<bool>? podeCompartilhar = null, Func<string, bool>? autorizar = null, Func<string>? operador = null)
    {
        _nuvem = nuvem;
        _produtos = produtos;
        if (ehAdministrador is not null) _souAdmin = ehAdministrador;
        if (podeCompartilhar is not null) _podeCompartilhar = podeCompartilhar;
        if (autorizar is not null) _autorizar = autorizar;
        if (operador is not null) _operador = operador;
        TemNuvem = true;
        AtualizarEstadoDaNuvem();
    }

    private void AtualizarEstadoDaNuvem()
    {
        if (_nuvem is null) return;
        var e = _nuvem.Estado;
        EmpresaNuvem = e.EmpresaId;
        EhAdministrador = _souAdmin();
        PodeCompartilharAgora = _podeCompartilhar();
        UltimoEnvioTexto = e.UltimoEnvioUtc is DateTime u ? $"Último envio: {u.ToLocalTime():dd/MM/yyyy HH:mm}" : "Esta loja ainda não foi enviada à nuvem.";
    }

    private void MensagemDaNuvem(string texto, bool erro) { if (erro) MostrarErro(texto); else MostrarSucesso(texto); }

    [RelayCommand]
    private void SalvarEmpresaNuvem()
    {
        if (_nuvem is null) return;
        try
        {
            var id = CaminhosNuvem.ValidarId(EmpresaNuvem, "empresa");
            _nuvem.SalvarEmpresa(id);
            AtualizarEstadoDaNuvem();
            MensagemDaNuvem("Código da empresa salvo.", false);
        }
        catch (RegraDeNegocioException ex) { MensagemDaNuvem(ex.Message, true); }
    }

    [RelayCommand] private Task CompartilharAsync() => EnviarAsync(false);

    [RelayCommand] private Task CompartilharMesmoAssimAsync() => EnviarAsync(true);

    private async Task EnviarAsync(bool ignorarConflito)
    {
        if (_nuvem is null || OcupadoNuvem) return;
        if (!_podeCompartilhar())
        {
            MensagemDaNuvem("Você não tem permissão para compartilhar os dados da loja (precisa de Financeiro).", true);
            return;
        }
        OcupadoNuvem = true;
        PrecisaConfirmarEnvio = false;
        ProgressoNuvem = "Preparando o envio...";
        try
        {
            var r = await _nuvem.CompartilharAsync(ignorarConflito, t => ProgressoNuvem = t);
            MensagemDaNuvem(r.Resumo, false);
        }
        catch (LojaIdDuplicadoException ex) { PrecisaConfirmarEnvio = true; MensagemDaNuvem(ex.Message, true); }
        catch (FalhaNuvemException ex) { MensagemDaNuvem(ex.Message + " Nada foi perdido: é só clicar de novo.", true); }
        catch (RegraDeNegocioException ex) { MensagemDaNuvem(ex.Message, true); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { MensagemDaNuvem("Não foi possível enviar: " + ex.Message, true); }
        finally
        {
            OcupadoNuvem = false;
            ProgressoNuvem = "";
            AtualizarEstadoDaNuvem();
        }
    }

    [RelayCommand]
    private async Task LerDaNuvemAsync()
    {
        if (_nuvem is null || OcupadoNuvem) return;
        OcupadoNuvem = true;
        ProgressoNuvem = "Lendo as lojas da nuvem...";
        try
        {
            var periodo = PeriodoEscolhido();
            var r = await _nuvem.LerLojasAsync(periodo.Inicio, periodo.Fim);
            Comparativo.Clear();
            foreach (var l in r.Lojas)
                Comparativo.Add(new LinhaComparativo(l.Nome, l.LojaId, Reais(l.FaturamentoCentavos), l.Vendas.ToString(PtBr), Reais(l.TicketMedioCentavos),
                    l.AtualizadoEmUtc == DateTime.MinValue ? "—" : l.AtualizadoEmUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), l.EstaLoja));
            ComparativoDoCache = r.DoCache;
            AvisoComparativo = r.Aviso ?? (r.Lojas.Count == 0 ? "Nenhuma loja enviou dados ainda." : $"Dados da nuvem lidos agora ({TextoDoPeriodo(periodo)}).");
            OnPropertyChanged(nameof(TemComparativo));
            MensagemDaNuvem("", false);
        }
        catch (RegraDeNegocioException ex) { MensagemDaNuvem(ex.Message, true); }
        finally
        {
            OcupadoNuvem = false;
            ProgressoNuvem = "";
        }
    }

    [RelayCommand]
    private async Task ReceberPrecosAsync()
    {
        if (_nuvem is null || OcupadoNuvem) return;
        OcupadoNuvem = true;
        try
        {
            var (texto, erro) = await NuvemApp.ReceberPrecosAsync(_nuvem, _autorizar, progresso: t => ProgressoNuvem = t);
            MensagemDaNuvem(texto, erro);
        }
        finally
        {
            OcupadoNuvem = false;
            ProgressoNuvem = "";
            AtualizarEstadoDaNuvem();
        }
    }

    // ---------------------------------------------------------------- preço por loja (administrador)
    [RelayCommand]
    private async Task BuscarProdutoDoPrecoAsync()
    {
        if (_nuvem is null || _produtos is null) return;
        MensagemPreco = "";
        if (!_souAdmin()) { MostrarMensagemPreco("Só o administrador define o preço por loja.", true); return; }
        try
        {
            var achados = await _produtos.BuscarAsync(BuscaProdutoPreco);
            if (achados.Count == 0) { MostrarMensagemPreco("Nenhum produto encontrado.", true); PrecosPorLoja.Clear(); _produtoDoPreco = null; ProdutoDoPrecoNome = ""; return; }
            _produtoDoPreco = achados[0];
            ProdutoDoPrecoNome = $"{_produtoDoPreco.Nome} (cadastro: {Reais(_produtoDoPreco.PrecoVendaCentavos)})";
            var lojas = Comparativo.Select(c => (c.Codigo, c.Nome)).ToList();
            if (lojas.Count == 0) lojas.Add((LojaId, LojaNome));
            var atuais = await _nuvem.LerPrecosDoProdutoAsync(_produtoDoPreco.Guid, lojas.Select(l => l.Codigo));
            PrecosPorLoja.Clear();
            foreach (var (codigo, nome) in lojas)
            {
                var linha = new LinhaPrecoLoja(codigo, nome);
                if (atuais.TryGetValue(codigo, out var p))
                {
                    linha.Varejo = Dinheiro.FormatarParaEdicao(p.PrecoVarejoCentavos);
                    linha.Atacado = p.PrecoAtacadoCentavos is long a ? Dinheiro.FormatarParaEdicao(a) : "";
                    linha.Situacao = "Preço definido";
                }
                else linha.Situacao = "Sem preço da loja (vale o do cadastro)";
                PrecosPorLoja.Add(linha);
            }
            MostrarMensagemPreco(achados.Count > 1 ? $"{achados.Count} produtos encontrados; mostrando o primeiro. Digite mais do nome para refinar." : "", false);
        }
        catch (FalhaNuvemException ex) { MostrarMensagemPreco(ex.Message, true); }
        catch (RegraDeNegocioException ex) { MostrarMensagemPreco(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SalvarPrecoDaLojaAsync(LinhaPrecoLoja? linha)
    {
        if (_nuvem is null || linha is null || _produtoDoPreco is null) return;
        try
        {
            if (!Dinheiro.TentarConverter(linha.Varejo, out var varejo)) throw new RegraDeNegocioException("Preço de varejo inválido. Digite algo como 59,90.");
            long? atacado = null;
            if (!string.IsNullOrWhiteSpace(linha.Atacado))
            {
                if (!Dinheiro.TentarConverter(linha.Atacado, out var a)) throw new RegraDeNegocioException("Preço de atacado inválido. Digite algo como 39,90 (ou deixe em branco).");
                atacado = a;
            }
            await _nuvem.DefinirPrecoAsync(_produtoDoPreco.Guid, linha.LojaId, varejo, atacado, _operador());
            linha.Situacao = "Salvo na nuvem. A loja recebe ao clicar em Receber da nuvem.";
            MostrarMensagemPreco($"Preço de {linha.Nome} salvo.", false);
        }
        catch (FalhaNuvemException ex) { MostrarMensagemPreco(ex.Message, true); }
        catch (RegraDeNegocioException ex) { MostrarMensagemPreco(ex.Message, true); }
    }

    private void MostrarMensagemPreco(string texto, bool erro) { MensagemPrecoEhErro = erro; MensagemPreco = texto; }
}
