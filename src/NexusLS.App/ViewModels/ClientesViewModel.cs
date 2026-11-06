using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoForma(string Texto, FormaPagamento Forma);

/// <summary>Uma venda a prazo na tela, com os textos prontos.</summary>
public record LinhaTitulo(int VendaId, string Data, string Vencimento, string Total, string Pago, string Saldo, string Situacao,
    string Ajuste = "", long TotalCentavos = 0, DateTime? VencimentoData = null)
{
    /// <summary>O resto da linha depois do número da venda; só mostra o ajuste quando existe.</summary>
    public string Detalhe => $" {Total} · {Situacao}\nvence {Vencimento} · falta {Saldo}" + (string.IsNullOrEmpty(Ajuste) ? "" : $"\n{Ajuste}");   // linhas próprias: "R$" nunca fica longe do valor
}

/// <summary>Cadastro de clientes (CPF/CNPJ, nome, razão social, telefone, e-mail, crédito, limite a prazo, observação) com o histórico de compras, a dívida a prazo, importação e exportação em CSV.</summary>
public partial class ClientesViewModel : ViewModelBase
{
    private readonly ClienteService _clientes;
    private readonly TituloPrazoService? _titulosPrazo;
    private readonly Func<DateTime> _hoje;
    private int _versaoBusca;
    private bool _silenciar; // true enquanto a seleção da lista é refeita pelo programa (não pelo operador)

    public ClientesViewModel(ClienteService clientes, TituloPrazoService? titulosPrazo = null, Func<DateTime>? hoje = null)
    {
        _clientes = clientes;
        _hoje = hoje ?? (() => DateTime.Now);
        _titulosPrazo = titulosPrazo;
        _pagamentoForma = FormasDePagamento[0];
        Novo();
        _ = BuscarAsync();
    }

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public ObservableCollection<Venda> Compras { get; } = new();
    public ObservableCollection<LinhaTitulo> Titulos { get; } = new();

    /// <summary>Histórico dos ajustes de título do cliente (de → para, motivo, quem, quando), do mais recente para o mais antigo.</summary>
    public ObservableCollection<string> Ajustes { get; } = new();
    public bool PodeEditarTitulo => _titulosPrazo is not null;

    // ----- Ajuste de título a prazo (a venda não muda; só o valor devido e o vencimento) -----
    [ObservableProperty] private LinhaTitulo? _tituloSelecionado;
    [ObservableProperty] private string _ajusteValorTexto = "";
    [ObservableProperty] private DateTime? _ajusteVencimento;
    [ObservableProperty] private string _ajusteMotivo = "";

    partial void OnTituloSelecionadoChanged(LinhaTitulo? value)
    {
        AjusteValorTexto = value is null ? "" : Dinheiro.FormatarParaEdicao(value.TotalCentavos);
        AjusteVencimento = value?.VencimentoData;
        AjusteMotivo = "";
    }

    public IReadOnlyList<OpcaoForma> FormasDePagamento { get; } = new[]
    {
        new OpcaoForma("Dinheiro", FormaPagamento.Dinheiro),
        new OpcaoForma("Pix", FormaPagamento.Pix),
        new OpcaoForma("Cartão de débito", FormaPagamento.CartaoDebito),
        new OpcaoForma("Cartão de crédito", FormaPagamento.CartaoCredito),
        new OpcaoForma("Cheque", FormaPagamento.Cheque)
    };

    // ----- Lista -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _mostrarInativos;
    [ObservableProperty] private Cliente? _selecionado;

    // ----- Cadastro -----
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _razaoSocial = "";
    [ObservableProperty] private string _email = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentoDica))]
    [NotifyPropertyChangedFor(nameof(DocumentoComErro))]
    private string _documento = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _limiteTexto = "";

    // ----- Crédito do cliente (saldo a favor) -----
    [ObservableProperty] private string _creditoAtualTexto = "R$ 0,00";
    [ObservableProperty] private string _novoCreditoTexto = "";
    [ObservableProperty] private string _creditoMotivo = "";

    /// <summary>Ajustes do crédito do cliente aberto (de → para, motivo, quem, quando), do mais recente para o mais antigo.</summary>
    public ObservableCollection<string> HistoricoCredito { get; } = new();

    // ----- Recebimento de dívida -----
    [ObservableProperty] private string _pagamentoValorTexto = "";
    [ObservableProperty] private OpcaoForma _pagamentoForma = null!;
    [ObservableProperty] private string _pagamentoObservacao = "";
    [ObservableProperty] private string _resumoDivida = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaEditando))]
    [NotifyPropertyChangedFor(nameof(TituloFormulario))]
    private int? _idEditando; // null = cadastrando um cliente novo

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoBotaoAtivo))]
    private bool _clienteAtivo = true;

    // ----- Histórico do cliente aberto -----
    [ObservableProperty] private string _resumoCompras = "";
    [ObservableProperty] private string _resumoAPrazo = "";

    [ObservableProperty] private string _resumoLista = "";

    /// <summary>Diz, enquanto o operador digita, se o número é CPF ou CNPJ e se está certo.</summary>
    public string DocumentoDica
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Documento)) return "Obrigatório: CPF (11 números) ou CNPJ (14 números), com ou sem pontos.";
            var r = ValidadorDocumento.Validar(Documento);
            return r.Valido ? (r.Tipo == TipoDocumento.Cpf ? "CPF válido." : "CNPJ válido.") : (r.Digitos.Length is 11 or 14 ? r.Erro! : "Digitando…");
        }
    }

    public bool DocumentoComErro
    {
        get
        {
            var r = ValidadorDocumento.Validar(Documento);
            return !r.Valido && r.Digitos.Length is 11 or 14;
        }
    }

    public bool EstaEditando => IdEditando is not null;
    public string TituloFormulario => EstaEditando ? "Editar cliente" : "Novo cliente";
    public string TextoBotaoAtivo => ClienteAtivo ? "Desativar" : "Reativar";

    partial void OnBuscaChanged(string value) => _ = BuscarAsync();
    partial void OnMostrarInativosChanged(bool value) => _ = BuscarAsync();

    /// <summary>O operador escolheu um cliente na lista: abre o cadastro e o histórico dele.</summary>
    partial void OnSelecionadoChanged(Cliente? value)
    {
        if (_silenciar || value is null) return; // a lista foi recarregada: não apaga o que está sendo editado
        _ = ExecutarAsync(() => AbrirAsync(value));
    }

    [RelayCommand]
    private Task BuscarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versaoBusca; // se o operador digita rápido, só a busca mais recente atualiza a lista
        var lista = await _clientes.BuscarAsync(Busca, MostrarInativos);
        if (versao != _versaoBusca) return;

        Clientes.Clear();
        foreach (var c in lista) Clientes.Add(c);
        ResumoLista = lista.Count == 1 ? "1 cliente" : $"{lista.Count} clientes";
        if (IdEditando is int id) Reselecionar(id);
    });

    private void Reselecionar(int id)
    {
        _silenciar = true;
        try { Selecionado = Clientes.FirstOrDefault(c => c.Id == id); }
        finally { _silenciar = false; }
    }

    [RelayCommand]
    private void Novo()
    {
        _silenciar = true;
        try { Selecionado = null; }
        finally { _silenciar = false; }

        IdEditando = null;
        Nome = "";
        Documento = "";
        RazaoSocial = "";
        Email = "";
        Telefone = "";
        Observacao = "";
        LimiteTexto = "";
        CreditoAtualTexto = Dinheiro.Formatar(0);
        NovoCreditoTexto = "";
        CreditoMotivo = "";
        HistoricoCredito.Clear();
        ClienteAtivo = true;
        ResumoDivida = "";
        Titulos.Clear();
        PagamentoValorTexto = "";
        PagamentoObservacao = "";
        ResumoCompras = "";
        ResumoAPrazo = "";
        Compras.Clear();
        Mensagem = "";
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        long limite = 0;
        if (!string.IsNullOrWhiteSpace(LimiteTexto) && !Dinheiro.TentarConverter(LimiteTexto, out limite))
            throw new RegraDeNegocioException("Limite de crédito inválido. Digite um valor como 500,00 (vazio = sem limite).");
        var dados = new DadosCliente(Nome, Documento, Telefone, Observacao, limite, RazaoSocial, Email, _clientes.ExigirDocumentoNaTela);

        if (IdEditando is int id)
        {
            await _clientes.EditarAsync(id, dados);
            await BuscarAsync();
            await CarregarHistoricoAsync(id);
            MostrarSucesso("Cliente atualizado.");
        }
        else
        {
            var novo = await _clientes.CadastrarAsync(dados);
            await BuscarAsync();
            await AbrirAsync(novo);
            MostrarSucesso("Cliente cadastrado.");
        }
    });

    [RelayCommand]
    private Task AlternarAtivoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id) return;

        var estavaAtivo = ClienteAtivo;
        if (estavaAtivo) await _clientes.DesativarAsync(id);
        else await _clientes.ReativarAsync(id);

        Novo();
        await BuscarAsync();
        MostrarSucesso(estavaAtivo ? "Cliente desativado." : "Cliente reativado.");
    });

    private async Task AbrirAsync(Cliente cliente)
    {
        IdEditando = cliente.Id;
        Nome = cliente.Nome;
        Documento = cliente.DocumentoFormatado;
        RazaoSocial = cliente.RazaoSocial ?? "";
        Email = cliente.Email ?? "";
        Telefone = cliente.Telefone ?? "";
        Observacao = cliente.Observacao ?? "";
        LimiteTexto = cliente.LimiteCreditoCentavos > 0 ? Dinheiro.FormatarParaEdicao(cliente.LimiteCreditoCentavos) : "";
        ClienteAtivo = cliente.Ativo;
        Mensagem = "";
        CreditoAtualTexto = Dinheiro.Formatar(cliente.CreditoCentavos);
        NovoCreditoTexto = cliente.CreditoCentavos > 0 ? Dinheiro.FormatarParaEdicao(cliente.CreditoCentavos) : "";
        CreditoMotivo = "";

        await CarregarHistoricoAsync(cliente.Id);
        Reselecionar(cliente.Id);
    }

    private async Task CarregarHistoricoAsync(int clienteId)
    {
        var h = await _clientes.HistoricoAsync(clienteId);

        Compras.Clear();
        foreach (var v in h.Vendas) Compras.Add(v);

        ResumoCompras = h.Compras == 0
            ? "Ainda não comprou."
            : $"{h.Compras} compra(s) · Total {Dinheiro.Formatar(h.TotalCompradoCentavos)} · Última em {h.UltimaCompra:dd/MM/yyyy}";
        ResumoAPrazo = h.TotalAPrazoCentavos > 0 ? $"Vendido a prazo: {Dinheiro.Formatar(h.TotalAPrazoCentavos)}" : "";

        HistoricoCredito.Clear();
        foreach (var a in await _clientes.HistoricoCreditoAsync(clienteId))
            HistoricoCredito.Add($"{Dinheiro.Formatar(a.SaldoAnteriorCentavos)} → {Dinheiro.Formatar(a.SaldoNovoCentavos)} · {a.Motivo}" +
                                 $"{(string.IsNullOrWhiteSpace(a.Operador) ? "" : " · " + a.Operador)} · {a.Quando:dd/MM/yyyy HH:mm}");

        var conta = await _clientes.ContaAsync(clienteId);
        var agora = _hoje();
        var selecionadoId = TituloSelecionado?.VendaId;
        Titulos.Clear();
        foreach (var t in conta.Titulos)
            Titulos.Add(new LinhaTitulo(t.VendaId, $"{t.DataVenda:dd/MM/yyyy}", $"{t.Vencimento:dd/MM/yyyy}",
                Dinheiro.Formatar(t.TotalCentavos), Dinheiro.Formatar(t.PagoCentavos), Dinheiro.Formatar(t.SaldoCentavos),
                t.Quitado ? "Pago" : t.VencidoEm(agora) ? "VENCIDO" : "Em aberto",
                t.Ajustado ? $"ajustado (venda: {Dinheiro.Formatar(t.ValorOriginalCentavos)})" : "", t.TotalCentavos, t.Vencimento));
        TituloSelecionado = Titulos.FirstOrDefault(t => t.VendaId == selecionadoId);

        Ajustes.Clear();
        if (_titulosPrazo is not null)
            foreach (var a in await _titulosPrazo.HistoricoDoClienteAsync(clienteId))
            {
                var partes = new List<string>();
                if (a.MudouValor) partes.Add($"{Dinheiro.Formatar(a.ValorAnteriorCentavos)} → {Dinheiro.Formatar(a.ValorNovoCentavos)}");
                if (a.MudouVencimento) partes.Add($"vence {a.VencimentoAnterior:dd/MM/yyyy} → {a.VencimentoNovo:dd/MM/yyyy}");
                Ajustes.Add($"Venda #{a.VendaId}: {string.Join(" · ", partes)} · {a.Motivo}" +
                            $"{(string.IsNullOrWhiteSpace(a.Operador) ? "" : " · " + a.Operador)} · {a.Quando:dd/MM/yyyy HH:mm}");
            }

        var vencido = conta.Titulos.Where(t => t.VencidoEm(agora)).Sum(t => t.SaldoCentavos);
        ResumoDivida = conta.Titulos.Count == 0
            ? "Sem vendas a prazo."
            : $"Deve {Dinheiro.Formatar(conta.SaldoDevedorCentavos)} · Já pagou {Dinheiro.Formatar(conta.TotalPagoCentavos)}"
              + (vencido > 0 ? $"\nVENCIDO: {Dinheiro.Formatar(vencido)}" : "");
    }

    /// <summary>Muda o crédito do cliente aberto para o saldo digitado. Exige motivo; vai para o histórico e para a trilha de atividades.</summary>
    [RelayCommand]
    private Task SalvarCreditoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id) return;
        long novo = 0;
        if (!string.IsNullOrWhiteSpace(NovoCreditoTexto) && !Dinheiro.TentarConverter(NovoCreditoTexto, out novo))
            throw new RegraDeNegocioException("Saldo de crédito inválido. Digite um valor como 50,00 (vazio = zero).");

        await _clientes.AjustarCreditoAsync(id, novo, CreditoMotivo);
        CreditoMotivo = "";
        await BuscarAsync();
        var cliente = Clientes.FirstOrDefault(c => c.Id == id);
        if (cliente is not null) await AbrirAsync(cliente);
        MostrarSucesso($"Crédito do cliente ajustado para {Dinheiro.Formatar(novo)}.");
    });

    // ----- Exportar e importar (CSV) -----
    /// <summary>A tela define a caixa "Salvar como" do Windows (nulo = cancelou).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;
    /// <summary>A tela define a caixa "Abrir arquivo" do Windows (nulo = cancelou).</summary>
    public Func<string?> EscolherArquivoParaAbrir { get; set; } = () => null;

    /// <summary>Linhas da pré-visualização da importação (uma por linha do arquivo).</summary>
    public ObservableCollection<LinhaImportacaoCliente> LinhasImportacao { get; } = new();
    [ObservableProperty] private bool _importacaoAberta;
    [ObservableProperty] private bool _importacaoPodeConfirmar;
    [ObservableProperty] private string _resumoImportacao = "";
    [ObservableProperty] private string _arquivoImportacao = "";
    private string? _csvImportacao;

    /// <summary>Grava todos os clientes (inclusive os desativados) num CSV do Excel: UTF-8 com BOM, separador ponto e vírgula.</summary>
    [RelayCommand]
    private Task ExportarAsync() => ExecutarAsync(async () =>
    {
        var lista = await _clientes.ListarParaExportacaoAsync();
        if (lista.Count == 0) throw new RegraDeNegocioException("Não há clientes cadastrados para exportar.");

        var caminho = EscolherArquivoParaSalvar($"clientes-{_hoje():yyyy-MM-dd}.csv");
        if (caminho is null) return;

        ExportadorClientes.Salvar(caminho, lista);
        MostrarSucesso($"Clientes exportados: {lista.Count}. Arquivo salvo: {caminho}");
    });

    /// <summary>Lê o CSV escolhido e mostra a pré-visualização. Nada é gravado até o operador confirmar.</summary>
    [RelayCommand]
    private Task ImportarAsync() => ExecutarAsync(async () =>
    {
        var caminho = EscolherArquivoParaAbrir();
        if (caminho is null) return;

        var texto = ImportadorProdutos.Decodificar(await File.ReadAllBytesAsync(caminho));
        await PreverAsync(texto, Path.GetFileName(caminho));
    });

    /// <summary>Pré-visualiza um CSV já lido (usado também pelos testes).</summary>
    public async Task PreverAsync(string csv, string nomeDoArquivo = "")
    {
        var previa = await _clientes.PreverImportacaoAsync(csv);
        if (previa.Erro is not null) { MostrarErro(previa.Erro); return; }

        _csvImportacao = csv;
        ArquivoImportacao = nomeDoArquivo;
        LinhasImportacao.Clear();
        foreach (var l in previa.Linhas) LinhasImportacao.Add(l);
        ResumoImportacao = previa.Resumo;
        ImportacaoPodeConfirmar = previa.PodeImportar;
        ImportacaoAberta = true;
        Mensagem = "";
    }

    [RelayCommand]
    private void CancelarImportacao()
    {
        ImportacaoAberta = false;
        LinhasImportacao.Clear();
        _csvImportacao = null;
        MostrarSucesso("Importação cancelada. Nada foi gravado.");
    }

    /// <summary>Grava o que a pré-visualização mostrou (as linhas com erro ficam de fora).</summary>
    [RelayCommand]
    private Task ConfirmarImportacaoAsync() => ExecutarAsync(async () =>
    {
        if (_csvImportacao is null) return;
        var resultado = await _clientes.ImportarAsync(_csvImportacao);
        ImportacaoAberta = false;
        LinhasImportacao.Clear();
        _csvImportacao = null;
        await BuscarAsync();
        MostrarSucesso($"Importação concluída: {resultado.Novos} cliente(s) novo(s), {resultado.Atualizados} atualizado(s)" +
                       (resultado.Erros > 0 ? $", {resultado.Erros} linha(s) com erro ficaram de fora." : "."));
    });

    /// <summary>Muda o valor devido e/ou o vencimento do título escolhido. A dívida é recalculada por FIFO; a venda não é alterada.</summary>
    [RelayCommand]
    private Task SalvarAjusteTituloAsync() => ExecutarAsync(async () =>
    {
        if (_titulosPrazo is null || IdEditando is not int id) return;
        if (TituloSelecionado is not { } t) throw new RegraDeNegocioException("Escolha o título (a venda a prazo) que quer ajustar.");
        if (!Dinheiro.TentarConverter(AjusteValorTexto, out var valor))
            throw new RegraDeNegocioException("Valor inválido. Digite um valor como 80,00.");

        await _titulosPrazo.EditarAsync(t.VendaId, valor, AjusteVencimento, AjusteMotivo);
        AjusteMotivo = "";
        await CarregarHistoricoAsync(id);
        MostrarSucesso($"Título da venda #{t.VendaId} ajustado. A dívida do cliente foi recalculada.");
    });

    /// <summary>Recebe um pagamento da dívida do cliente aberto; ele abate as vendas mais antigas primeiro.</summary>
    [RelayCommand]
    private Task ReceberPagamentoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id) return;
        if (!Dinheiro.TentarConverter(PagamentoValorTexto, out var valor))
            throw new RegraDeNegocioException("Valor inválido. Digite um valor como 50,00.");

        await _clientes.RegistrarPagamentoAsync(id, valor, PagamentoForma.Forma, PagamentoObservacao);
        PagamentoValorTexto = "";
        PagamentoObservacao = "";
        await CarregarHistoricoAsync(id);
        MostrarSucesso($"Pagamento de {Dinheiro.Formatar(valor)} registrado.");
    });
}
