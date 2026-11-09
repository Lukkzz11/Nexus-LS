using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Etapa desconto e pagamento: fechar a venda (grava venda e baixa de estoque pelo VendaService).</summary>
public partial class PdvViewModel
{
    // ================= Etapa: desconto → pagamento =================

    [ObservableProperty] private string _acrescimoTexto = "";

    private void AbrirDesconto()
    {
        if (_carrinho.Vazio)
        {
            MostrarErro("O carrinho está vazio. Adicione um produto antes de fechar a venda.");
            return;
        }

        _descontoSel = new SelecaoDesconto();
        DescontoTexto = DescontoInicial();
        _descontoSel.Texto = DescontoTexto; // se o texto não mudou, o evento não dispara
        AtualizarOpcoesDesconto();
        AtualizarResumoDesconto();
        Mensagem = "";
        IrPara(EtapaPdv.Desconto);
    }

    private void ConfirmarDesconto()
    {
        try
        {
            _descontoResolvido = _descontoSel.Resolver(_carrinho.SubtotalCentavos);
        }
        catch (RegraDeNegocioException ex)
        {
            MostrarErro(ex.Message);
            return;
        }

        var total = _carrinho.SubtotalCentavos - _descontoResolvido.ValorEmCentavos;
        _pagamento = new SelecaoPagamento(total);
        EscolhendoParcelas = false;
        if (_clienteFixo is not null) PrepararPagamentoDoCliente();
        TotalFinalTexto = Dinheiro.Formatar(total);
        ValorRecebidoTexto = _pagamento.ValorRecebidoTexto;
        ClienteBusca = "";
        ClientesResultado.Clear();

        OpcoesPagamento.Clear();
        foreach (var forma in SelecaoPagamento.Formas) OpcoesPagamento.Add(new OpcaoViewModel(SelecaoPagamento.Nome(forma)));
        OpcoesParcela.Clear();
        for (var n = 1; n <= SelecaoPagamento.MaxParcelas; n++)
            OpcoesParcela.Add(new OpcaoViewModel($"{n}x", $"{TaxasCartao.Percentual(n):0}% de acréscimo"));

        Mensagem = "";
        AtualizarPagamento();
        IrPara(EtapaPdv.Pagamento);
    }

    /// <summary>No crédito: true depois do Enter que leva às parcelas (as setas passam a escolher o número delas).</summary>
    [ObservableProperty] private bool _escolhendoParcelas;

    private void AtualizarOpcoesDesconto()
    {
        if (OpcoesDesconto.Count == 0)
        {
            OpcoesDesconto.Add(new OpcaoViewModel("R$", "valor em reais"));
            OpcoesDesconto.Add(new OpcaoViewModel("%", "percentual"));
        }
        OpcoesDesconto[0].Selecionada = _descontoSel.Tipo == TipoDesconto.Reais;
        OpcoesDesconto[1].Selecionada = _descontoSel.Tipo == TipoDesconto.Percentual;
        DescontoDica = _descontoSel.Tipo == TipoDesconto.Reais ? "Digite o valor. Ex.: 5,00" : "Digite o percentual. Ex.: 10";
        AtualizarResumoDesconto();
    }

    private void AtualizarResumoDesconto()
    {
        var subtotal = _carrinho.SubtotalCentavos;
        try
        {
            var d = _descontoSel.Resolver(subtotal);
            DescontoResumo = d.ValorEmCentavos > 0
                ? $"Desconto {Dinheiro.Formatar(d.ValorEmCentavos)}  ·  Total {Dinheiro.Formatar(subtotal - d.ValorEmCentavos)}"
                : $"Sem desconto  ·  Total {Dinheiro.Formatar(subtotal)}";
        }
        catch (RegraDeNegocioException)
        {
            DescontoResumo = "Valor inválido";
        }
    }

    /// <summary>Depois de ← →: outra forma de pagamento. Leva o cursor ao campo que ela pede.</summary>
    private void FormaMudou()
    {
        EscolhendoParcelas = false;
        AtualizarPagamento();
        Mensagem = "";

        if (EmAPrazo && _clienteFixo is not null)
        {
            PrepararPagamentoDoCliente();
            FocoSolicitado?.Invoke(AlvoFoco.Modal);
        }
        else if (EmAPrazo)
        {
            ClienteBusca = "";
            _ = AtualizarClientesAsync();
            FocoSolicitado?.Invoke(AlvoFoco.Cliente);
        }
        else
        {
            FocoSolicitado?.Invoke(EmDinheiro ? AlvoFoco.ValorRecebido : AlvoFoco.Modal);
        }
    }

    private void AtualizarPagamento()
    {
        for (var i = 0; i < OpcoesPagamento.Count; i++)
            OpcoesPagamento[i].Selecionada = SelecaoPagamento.Formas[i] == _pagamento!.Forma;
        for (var i = 0; i < OpcoesParcela.Count; i++)
            OpcoesParcela[i].Selecionada = i + 1 == _pagamento!.Parcelas;

        OnPropertyChanged(nameof(EmDinheiro));
        OnPropertyChanged(nameof(EmCredito));
        OnPropertyChanged(nameof(EmAPrazo));
        TotalFinalTexto = Dinheiro.Formatar(_pagamento!.TotalCentavos);
        AcrescimoTexto = _pagamento.AcrescimoCentavos > 0
            ? $"Acréscimo do crédito em {_pagamento.Parcelas}x ({TaxasCartao.Percentual(_pagamento.Parcelas):0}%): {Dinheiro.Formatar(_pagamento.AcrescimoCentavos)}  ·  {_pagamento.Parcelas}x de {Dinheiro.Formatar((_pagamento.TotalCentavos + _pagamento.Parcelas - 1) / _pagamento.Parcelas)}"
            : "";
        AtualizarTroco();
    }

    private void AtualizarTroco()
    {
        if (_pagamento is null || _pagamento.Forma != FormaPagamento.Dinheiro)
        {
            TrocoTexto = "";
            return;
        }

        if (_pagamento.ValorRecebidoCentavos is not long recebido)
            TrocoTexto = "Valor recebido inválido";
        else if (recebido < _pagamento.TotalCentavos)
            TrocoTexto = $"Faltam {Dinheiro.Formatar(_pagamento.TotalCentavos - recebido)}";
        else
            TrocoTexto = $"Troco: {Dinheiro.Formatar(recebido - _pagamento.TotalCentavos)}";
    }

    private async Task AtualizarClientesAsync()
    {
        if (Etapa != EtapaPdv.Pagamento || !EmAPrazo || _clienteFixo is not null) return;

        _clientesCts?.Cancel();
        var cts = _clientesCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token);
            var lista = await _clientes.BuscarAsync(ClienteBusca);
            if (cts.IsCancellationRequested) return;

            ClientesResultado.Clear();
            foreach (var c in lista.Take(6)) ClientesResultado.Add(c);
            ClienteDestacado = ClientesResultado.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao buscar clientes: " + ex.Message);
        }
    }

    private void MoverCliente(int passo)
    {
        if (ClientesResultado.Count == 0) return;
        var atual = ClienteDestacado is null ? 0 : ClientesResultado.IndexOf(ClienteDestacado);
        ClienteDestacado = ClientesResultado[Math.Clamp(atual + passo, 0, ClientesResultado.Count - 1)];
    }

    /// <summary>
    /// Enter no pagamento. A venda a prazo sem cliente na lista cadastra o nome digitado na hora
    /// (primeiro Enter) e só finaliza no Enter seguinte. O VendaService valida tudo de novo e
    /// faz venda + baixa de estoque numa transação única.
    /// </summary>
    private async Task FinalizarVendaAsync()
    {
        if (_pagamento is null) return;

        if (EmAPrazo && ClienteDestacado is null && ClienteBusca.Trim().Length > 0)
        {
            var novo = await _clientes.CadastrarAsync(new DadosCliente(ClienteBusca));
            ClientesResultado.Clear();
            ClientesResultado.Add(novo);
            ClienteDestacado = novo;
            MostrarSucesso($"Cliente \"{novo.Nome}\" cadastrado. Tecle Enter para finalizar.");
            return;
        }

        var erro = _pagamento.Validar();
        if (erro is not null)
        {
            MostrarErro(erro);
            return;
        }

        // Caixa fechado: a venda ficaria fora de qualquer turno e não entraria no fechamento nem no relatório
        // de caixa. Quando a tela tem o serviço de caixa (programa de verdade), exige um turno aberto.
        // Nos testes de ViewModel sem caixa (_caixa nulo), a venda continua livre, como antes.
        if (_caixa is not null)
        {
            var aberto = await _caixa.ObterAbertoAsync();
            if (aberto is null)
            {
                await AtualizarCaixaAsync();
                MostrarErro("Abra o caixa antes de finalizar a venda. Sem um turno aberto, a venda fica fora do fechamento.");
                return;
            }
        }

        var venda = await _vendas.FinalizarAsync(_pagamento.ParaPedido(_carrinho, _descontoResolvido) with { ChaveUnica = _chaveVenda });

        var pagou = SelecaoPagamento.Nome(venda.FormaPagamento);
        if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1) pagou += $" em {venda.Parcelas}x";
        SucessoTitulo = $"Venda #{venda.Id} finalizada!";
        SucessoDetalhe = $"{Dinheiro.Formatar(venda.TotalCentavos)}  ·  {pagou}"
                         + (venda.TrocoCentavos > 0 ? $"  ·  Troco {Dinheiro.Formatar(venda.TrocoCentavos)}" : "");
        if (_clienteFixo is not null) SucessoDetalhe += await DetalheDoClienteAposVendaAsync();

        LimparCarrinho();
        _sucessoSel = new SelecaoSucesso();
        _sucessoDesde = DateTime.UtcNow;
        _ultimaVendaId = venda.Id;
        AtualizarOpcoesSucesso();
        Mensagem = "";
        IrPara(EtapaPdv.Sucesso);

        // O cupom sai DEPOIS da venda gravada: uma falha da impressora nunca desfaz nem bloqueia a venda.
        try
        {
            if (await _cupons.ImprimirAoFinalizarAsync(venda.Id)) MostrarSucesso("Cupom enviado para a impressora.");
        }
        catch (Exception ex)
        {
            MostrarErro($"Venda finalizada, mas o cupom não foi impresso: {ex.Message} Use \"Reimprimir cupom\".");
        }
    }
}
