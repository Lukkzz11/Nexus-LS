using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Gaveta;
using NexusLS.Core.Models;
using NexusLS.Core.Pdf;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha da lista de orçamentos.</summary>
public sealed record LinhaOrcamento(int Id, string Numero, string Cliente, string Total, string Data, string Situacao)
{
    public string DescricaoAcessivel => $"{Numero}, {Cliente}, {Total}, {Situacao}";
}

/// <summary>
/// Tela "Orçamentos": montar o preço, mandar o PDF por WhatsApp e, quando o cliente aprovar, virar pedido.
/// </summary>
public partial class OrcamentosViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly OrcamentoService _orcamentos;
    private readonly ProdutoService _produtos;
    private readonly ClienteService _clientes;
    private readonly ConfiguracaoService _configuracao;
    private readonly GavetaService _gaveta;
    private int _versaoBusca;
    private int? _editando;

    public OrcamentosViewModel(OrcamentoService orcamentos, ProdutoService produtos, ClienteService clientes,
        ConfiguracaoService configuracao, GavetaService gaveta)
    {
        _orcamentos = orcamentos;
        _produtos = produtos;
        _clientes = clientes;
        _configuracao = configuracao;
        _gaveta = gaveta;
        Carregamento = CarregarAsync();
    }

    public Task Carregamento { get; private set; }

    /// <summary>A tela define (abre a janela do WhatsApp com telefone, mensagem e PDF). Quem envia é a pessoa.</summary>
    public Action<string, string, string> AbrirWhatsApp { get; set; } = (_, _, _) => { };

    /// <summary>A tela define: abre o PDF no leitor do computador.</summary>
    public Action<string> AbrirArquivo { get; set; } = _ => { };

    public ObservableCollection<LinhaOrcamento> Lista { get; } = new();
    public ObservableCollection<string> DetalheItens { get; } = new();

    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _soAbertos;
    [ObservableProperty] private LinhaOrcamento? _selecionado;
    [ObservableProperty] private string _detalheTitulo = "";
    [ObservableProperty] private string _detalheCliente = "";
    [ObservableProperty] private string _detalheValores = "";
    [ObservableProperty] private string _detalheTotal = "";
    [ObservableProperty] private string _detalheObservacao = "";

    private Orcamento? _atual;

    public bool TemSelecao => _atual is not null;
    public bool EstaAberto => _atual?.Status == StatusOrcamento.Aberto;
    public bool PodeReabrir => _atual is not null && !EstaAberto;
    public bool ListaVazia => Lista.Count == 0;

    partial void OnBuscaChanged(string value) => Carregamento = CarregarAsync();
    partial void OnSoAbertosChanged(bool value) => Carregamento = CarregarAsync();
    partial void OnSelecionadoChanged(LinhaOrcamento? value) => Carregamento = MostrarDetalheAsync(value);

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var id = Selecionado?.Id;
        var versao = ++_versaoBusca;
        var lista = await _orcamentos.ListarAsync(Busca, SoAbertos);
        if (versao != _versaoBusca) return;
        Lista.Clear();
        foreach (var o in lista)
            Lista.Add(new LinhaOrcamento(o.Id, o.Numero, o.ClienteNome, Dinheiro.Formatar(o.TotalCentavos),
                o.CriadoEm.ToString("dd/MM", PtBr), OrcamentoService.Situacao(o, _orcamentos.Hoje)));
        OnPropertyChanged(nameof(ListaVazia));
        Selecionado = Lista.FirstOrDefault(l => l.Id == id);
    });

    private async Task MostrarDetalheAsync(LinhaOrcamento? linha)
    {
        Orcamento? o = null;
        if (linha is not null)
        {
            try { o = await _orcamentos.ObterAsync(linha.Id); }
            catch (RegraDeNegocioException) { }
            if (Selecionado?.Id != linha.Id) return; // a seleção mudou enquanto carregava
        }

        _atual = o;
        DetalheItens.Clear();
        if (o is null) DetalheTitulo = DetalheCliente = DetalheValores = DetalheTotal = DetalheObservacao = "";
        else
        {
            DetalheTitulo = $"{o.Numero} · {OrcamentoService.Situacao(o, _orcamentos.Hoje)}";
            DetalheCliente = o.ClienteNome + (string.IsNullOrWhiteSpace(o.ClienteTelefone) ? "" : $" · {o.ClienteTelefone}");
            DetalheValores = $"Válido até {o.ValidoAte.ToString("dd/MM/yyyy", PtBr)}"
                + (o.DescontoCentavos > 0 ? $" · desconto {Dinheiro.Formatar(o.DescontoCentavos)}" : "");
            DetalheTotal = "Total: " + Dinheiro.Formatar(o.TotalCentavos);
            DetalheObservacao = o.Observacao ?? "";
            foreach (var i in o.Itens)
                DetalheItens.Add($"{i.Quantidade} × {i.Nome}{(i.Tamanho == Tamanhos.SemTamanho ? "" : " (" + i.Tamanho + ")")}  —  {Dinheiro.Formatar(i.TotalCentavos)}");
        }
        OnPropertyChanged(nameof(TemSelecao));
        OnPropertyChanged(nameof(EstaAberto));
        OnPropertyChanged(nameof(PodeReabrir));
    }

    // ================= ações do orçamento escolhido =================

    /// <summary>Gera o PDF com os dados da loja e guarda na gaveta de documentos. Devolve o caminho.</summary>
    private async Task<(string Caminho, ConfiguracaoLoja Loja)> GuardarPdfAsync(Orcamento o)
    {
        var loja = await _configuracao.ObterAsync();
        var bytes = OrcamentoService.GerarPdf(o, loja);
        return (_gaveta.Salvar(CategoriaGaveta.Outros, OrcamentoService.NomeDoPdf(o), bytes), loja);
    }

    [RelayCommand]
    private Task EnviarWhatsAppAsync() => ExecutarAsync(async () =>
    {
        if (_atual is not { } o) return;
        var (caminho, loja) = await GuardarPdfAsync(o);
        var primeiroNome = o.ClienteNome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? o.ClienteNome;
        AbrirWhatsApp(o.ClienteTelefone ?? "",
            $"Olá, {primeiroNome}! Aqui é da {loja.NomeLoja}. Segue o orçamento {o.Numero}, no total de {Dinheiro.Formatar(o.TotalCentavos)}, "
            + $"válido até {o.ValidoAte.ToString("dd/MM", PtBr)}. Qualquer dúvida, é só chamar.", caminho);
        MostrarSucesso("PDF guardado na gaveta e preparado no WhatsApp.");
    });

    [RelayCommand]
    private Task VerPdfAsync() => ExecutarAsync(async () =>
    {
        if (_atual is not { } o) return;
        var (caminho, _) = await GuardarPdfAsync(o);
        AbrirArquivo(caminho);
        MostrarSucesso($"PDF salvo: {caminho}");
    });

    [RelayCommand]
    private Task VirarPedidoAsync() => ExecutarAsync(async () =>
    {
        if (_atual is not { } o) return;
        var pedido = await _orcamentos.VirarPedidoAsync(o.Id);
        await RecarregarMantendoAsync(o.Id);
        MostrarSucesso($"Pedido {pedido.Numero} criado. Acompanhe na tela Pedidos.");
    });

    [RelayCommand]
    private Task RecusarAsync() => ExecutarAsync(async () =>
    {
        if (_atual is not { } o) return;
        await _orcamentos.RecusarAsync(o.Id);
        await RecarregarMantendoAsync(o.Id);
        MostrarSucesso($"Orçamento {o.Numero} marcado como recusado.");
    });

    [RelayCommand]
    private Task ReabrirAsync() => ExecutarAsync(async () =>
    {
        if (_atual is not { } o) return;
        await _orcamentos.ReabrirAsync(o.Id);
        await RecarregarMantendoAsync(o.Id);
        MostrarSucesso($"Orçamento {o.Numero} em aberto de novo.");
    });

    private async Task RecarregarMantendoAsync(int id)
    {
        await CarregarAsync();
        var linha = Lista.FirstOrDefault(l => l.Id == id);
        Selecionado = linha;
        await MostrarDetalheAsync(linha);
    }

    // ================= montar / corrigir =================

    [ObservableProperty] private bool _formAberto;
    [ObservableProperty] private string _tituloForm = "";
    [ObservableProperty] private Cliente? _clienteCadastrado;
    [ObservableProperty] private string _clienteNome = "";
    [ObservableProperty] private string _clienteTelefone = "";
    [ObservableProperty] private string _validadeDias = "7";
    [ObservableProperty] private string _desconto = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _buscaProduto = "";
    [ObservableProperty] private Produto? _resultadoSelecionado;
    [ObservableProperty] private string _totalForm = "R$ 0,00";

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public ObservableCollection<Produto> Resultados { get; } = new();
    public ObservableCollection<ItemPedidoViewModel> ItensForm { get; } = new();
    public bool SemClienteCadastrado => ClienteCadastrado is null;

    partial void OnClienteCadastradoChanged(Cliente? value) => OnPropertyChanged(nameof(SemClienteCadastrado));
    partial void OnDescontoChanged(string value) => RecalcularForm();
    partial void OnBuscaProdutoChanged(string value) => _ = BuscarProdutosAsync();

    [RelayCommand]
    private Task NovoAsync() => ExecutarAsync(async () =>
    {
        await AbrirFormAsync("Novo orçamento");
        _editando = null;
    });

    [RelayCommand]
    private Task CorrigirAsync() => ExecutarAsync(async () =>
    {
        if (_atual is not { Status: StatusOrcamento.Aberto } o) return;
        await AbrirFormAsync($"Corrigir {o.Numero}");
        _editando = o.Id;
        ClienteCadastrado = Clientes.FirstOrDefault(c => c.Id == o.ClienteId);
        ClienteNome = o.ClienteNome;
        ClienteTelefone = o.ClienteTelefone ?? "";
        Observacao = o.Observacao ?? "";
        Desconto = o.DescontoCentavos > 0 ? Dinheiro.FormatarParaEdicao(o.DescontoCentavos) : "";
        foreach (var i in o.Itens)
        {
            // Produto que foi excluído depois: o item continua, como item digitado.
            var existe = i.ProdutoId is int pid && (await _produtos.ListarTamanhosAsync(pid)).Count > 0;
            var tamanhos = existe ? (await TamanhosDeAsync(i.ProdutoId!.Value)).ToList() : new List<string> { i.Tamanho };
            if (!tamanhos.Contains(i.Tamanho)) tamanhos.Add(i.Tamanho);
            var item = new ItemPedidoViewModel(existe ? i.ProdutoId : null, i.Nome, i.PrecoUnitarioCentavos, tamanhos, RecalcularForm);
            item.Tamanho = i.Tamanho;
            item.QuantidadeTexto = i.Quantidade.ToString();
            ItensForm.Add(item);
        }
        RecalcularForm();
    });

    private async Task AbrirFormAsync(string titulo)
    {
        LimparForm();
        Clientes.Clear();
        foreach (var c in await _clientes.BuscarAsync(null)) Clientes.Add(c);
        TituloForm = titulo;
        FormAberto = true;
        Mensagem = "";
    }

    [RelayCommand]
    private void FecharForm()
    {
        FormAberto = false;
        LimparForm();
    }

    private void LimparForm()
    {
        ItensForm.Clear();
        ClienteCadastrado = null;
        ClienteNome = ClienteTelefone = Observacao = Desconto = BuscaProduto = "";
        ValidadeDias = "7";
        Resultados.Clear();
        RecalcularForm();
    }

    private async Task<IReadOnlyList<string>> TamanhosDeAsync(int produtoId)
    {
        var reais = (await _produtos.ListarTamanhosAsync(produtoId)).Select(t => t.Tamanho).Where(t => t != Tamanhos.SemTamanho).ToList();
        return reais.Count > 0 ? reais : new List<string> { Tamanhos.SemTamanho };
    }

    private async Task BuscarProdutosAsync()
    {
        var texto = BuscaProduto;
        if (string.IsNullOrWhiteSpace(texto)) { Resultados.Clear(); return; }
        var lista = await _produtos.BuscarAsync(texto);
        if (texto != BuscaProduto) return;
        Resultados.Clear();
        foreach (var p in lista.Take(8)) Resultados.Add(p);
        ResultadoSelecionado = Resultados.FirstOrDefault();
    }

    [RelayCommand]
    private Task AdicionarProdutoAsync() => ExecutarAsync(async () =>
    {
        if (ResultadoSelecionado is null && !string.IsNullOrWhiteSpace(BuscaProduto))
            ResultadoSelecionado = (await _produtos.BuscarAsync(BuscaProduto)).FirstOrDefault();
        if (ResultadoSelecionado is not { } produto)
        {
            MostrarErro("Não achei esse produto. Use \"Item sem cadastro\" para um serviço ou encomenda.");
            return;
        }

        var opcoes = await TamanhosDeAsync(produto.Id);
        var existente = ItensForm.FirstOrDefault(i => i.ProdutoId == produto.Id && i.Tamanho == opcoes[0]);
        if (existente is not null) existente.QuantidadeTexto = (existente.Quantidade + 1).ToString();
        else ItensForm.Add(new ItemPedidoViewModel(produto.Id, produto.Nome, produto.PrecoVendaCentavos, opcoes, RecalcularForm));

        BuscaProduto = "";
        Resultados.Clear();
        Mensagem = "";
        RecalcularForm();
    });

    [RelayCommand]
    private void AdicionarItemLivre()
    {
        ItensForm.Add(new ItemPedidoViewModel(null, "", 0, new[] { Tamanhos.SemTamanho }, RecalcularForm));
        RecalcularForm();
    }

    [RelayCommand]
    private void RemoverItem(ItemPedidoViewModel? item)
    {
        if (item is null) return;
        ItensForm.Remove(item);
        RecalcularForm();
    }

    private void RecalcularForm()
    {
        var desconto = Dinheiro.TentarConverter(Desconto, out var d) ? d : 0;
        TotalForm = Dinheiro.Formatar(Math.Max(0, ItensForm.Sum(i => i.TotalCentavos) - desconto));
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var itens = new List<ItemPedidoDados>();
        foreach (var i in ItensForm)
        {
            if (i.Quantidade <= 0) throw new RegraDeNegocioException($"Informe a quantidade de \"{i.Nome}\" (número inteiro maior que zero).");
            if (i.PrecoCentavos is not long preco) throw new RegraDeNegocioException($"Informe o preço de \"{i.Nome}\". Exemplo: 49,90");
            itens.Add(new ItemPedidoDados(i.ProdutoId, i.Quantidade, i.Nome, preco, i.Tamanho));
        }
        if (!int.TryParse(ValidadeDias.Trim(), out var dias)) throw new RegraDeNegocioException("Validade: um número de 1 a 365 dias.");
        long desconto = 0;
        if (!string.IsNullOrWhiteSpace(Desconto) && !Dinheiro.TentarConverter(Desconto, out desconto))
            throw new RegraDeNegocioException("Desconto inválido. Exemplo: 10,00");

        var dados = new DadosOrcamento(itens, ClienteCadastrado?.Id, ClienteNome,
            ClienteCadastrado is null ? ClienteTelefone : null, dias, desconto, Observacao);
        var salvo = _editando is int id ? await _orcamentos.AlterarAsync(id, dados) : await _orcamentos.CriarAsync(dados);

        FormAberto = false;
        LimparForm();
        _editando = null;
        Busca = "";
        await RecarregarMantendoAsync(salvo.Id);
        MostrarSucesso($"Orçamento {salvo.Numero} salvo.");
    });
}
