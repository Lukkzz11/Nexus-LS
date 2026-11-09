using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Teclado do PDV: tradução das teclas em ações, digitação de tamanho e atalhos globais.</summary>
public partial class PdvViewModel
{
    // ================= Teclado =================

    /// <summary>
    /// Ponto único de entrada das teclas do fluxo. Devolve true se a tecla foi usada
    /// (a tela então não a repassa a mais ninguém). Enquanto uma operação está em andamento,
    /// Enter e F10 são engolidos: assim um Enter repetido nunca cria uma segunda venda.
    /// </summary>
    public bool ProcessarTecla(ComandoTecla tecla)
    {
        if (_ocupado) return tecla is ComandoTecla.Enter or ComandoTecla.F10;

        return Etapa switch
        {
            EtapaPdv.Busca => TeclaBusca(tecla),
            EtapaPdv.Tamanho => TeclaTamanho(tecla),
            EtapaPdv.Quantidade => TeclaQuantidade(tecla),
            EtapaPdv.Desconto => TeclaDesconto(tecla),
            EtapaPdv.Pagamento => TeclaPagamento(tecla),
            _ => TeclaSucesso(tecla)
        };
    }

    private bool TeclaBusca(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Enter:
                if (Busca.Trim().Length == 0) AbrirDesconto();          // busca vazia + carrinho cheio: fechar a venda
                else _ = ExecutarOcupadoAsync(EscolherProdutoAsync);    // senão: escolher o produto
                return true;
            case ComandoTecla.F10:
                AbrirDesconto();
                return true;
            case ComandoTecla.Cima:
            case ComandoTecla.Baixo:
                if (Resultados.Count == 0) return false;
                MoverResultado(tecla == ComandoTecla.Baixo ? +1 : -1);
                _desceu = true; // a partir daqui o Enter usa o item destacado
                return true;
            case ComandoTecla.Espaco:
                if (Busca.Length > 0) return false; // espaço no meio de um nome é só um espaço
                AlternarTabela();                   // com a busca vazia, o espaço troca varejo/atacado
                return true;
            case ComandoTecla.Esc:
                if (Resultados.Count > 0) Resultados.Clear();
                else if (Busca.Length > 0) Busca = "";
                _desceu = false;
                return true;
            default:
                return false; // ← → ficam para o cursor do campo de texto
        }
    }

    private bool TeclaTamanho(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Esquerda: MoverGrade(DirecaoGrade.Esquerda); return true;
            case ComandoTecla.Direita: MoverGrade(DirecaoGrade.Direita); return true;
            case ComandoTecla.Cima: MoverGrade(DirecaoGrade.Cima); return true;
            case ComandoTecla.Baixo: MoverGrade(DirecaoGrade.Baixo); return true;
            case ComandoTecla.Enter: ConfirmarTamanho(); return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaQuantidade(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Cima:
                if (QuantidadeValor >= _quantidadeSel!.Maximo)
                {
                    QuantidadeTexto = _quantidadeSel.Maximo.ToString();
                    MostrarErro($"Só há {_quantidadeSel.Maximo} unidade(s) disponíveis.");
                }
                else
                {
                    QuantidadeTexto = (QuantidadeValor + 1).ToString();
                    Mensagem = "";
                }
                return true;
            case ComandoTecla.Baixo:
                QuantidadeTexto = Math.Max(1, QuantidadeValor - 1).ToString();
                Mensagem = "";
                return true;
            case ComandoTecla.Enter: ConfirmarQuantidade(); return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaDesconto(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Esquerda:
            case ComandoTecla.Cima: // as opções são só duas: ↑ e ↓ também trocam entre elas
                _descontoSel.Esquerda();
                AtualizarOpcoesDesconto();
                return true;
            case ComandoTecla.Direita:
            case ComandoTecla.Baixo:
                _descontoSel.Direita();
                AtualizarOpcoesDesconto();
                return true;
            case ComandoTecla.Enter:
            case ComandoTecla.F10:
                ConfirmarDesconto();
                return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaPagamento(ComandoTecla tecla)
    {
        // No crédito, o Enter leva às parcelas; lá as setas escolhem e o Enter seguinte finaliza.
        if (EscolhendoParcelas && EmCredito)
        {
            switch (tecla)
            {
                case ComandoTecla.Esquerda:
                case ComandoTecla.Baixo:
                    if (_pagamento!.MenosParcelas()) AtualizarPagamento();
                    return true;
                case ComandoTecla.Direita:
                case ComandoTecla.Cima:
                    if (_pagamento!.MaisParcelas()) AtualizarPagamento();
                    return true;
                case ComandoTecla.Esc:
                    EscolhendoParcelas = false; // volta para a escolha da forma de pagamento
                    return true;
            }
        }

        switch (tecla)
        {
            case ComandoTecla.Enter when EmCredito && !EscolhendoParcelas:
                EscolhendoParcelas = true;
                Mensagem = "";
                return true;
            case ComandoTecla.Esquerda: _pagamento!.Anterior(); FormaMudou(); return true;
            case ComandoTecla.Direita: _pagamento!.Proxima(); FormaMudou(); return true;
            case ComandoTecla.Cima:
                if (EmAPrazo) MoverCliente(-1);
                else if (_pagamento!.MaisParcelas()) AtualizarPagamento();
                else if (_pagamento!.TrocarDeLinha()) FormaMudou();
                return true;
            case ComandoTecla.Baixo:
                if (EmAPrazo) MoverCliente(+1);
                else if (_pagamento!.MenosParcelas()) AtualizarPagamento();
                else if (_pagamento!.TrocarDeLinha()) FormaMudou();
                return true;
            case ComandoTecla.Enter:
            case ComandoTecla.F10:
                _ = ExecutarOcupadoAsync(FinalizarVendaAsync);
                return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaSucesso(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Esquerda:
            case ComandoTecla.Cima:
                _sucessoSel.Esquerda(); AtualizarOpcoesSucesso(); return true;
            case ComandoTecla.Direita:
            case ComandoTecla.Baixo:
                _sucessoSel.Direita(); AtualizarOpcoesSucesso(); return true;
            case ComandoTecla.Enter:
                if (DateTime.UtcNow - _sucessoDesde < _tempoMinimoSucesso) return true; // Enter apressado: engole
                if (_sucessoSel.Opcao == OpcaoSucesso.NovaVenda) NovaVenda();
                else _ = ExecutarOcupadoAsync(ReimprimirCupomAsync);
                return true;
            case ComandoTecla.Esc: NovaVenda(); return true;
            default: return false;
        }
    }

    // ================= Digitar o tamanho =================

    private string _digitadoNoTamanho = "";
    private DateTime _digitadoNoTamanhoEm = DateTime.MinValue;

    /// <summary>
    /// Na janela de tamanho, digitar "M", "GG" ou "38" salta direto para esse tamanho (letras seguidas, digitadas
    /// em menos de um segundo, formam o nome). Devolve true se o texto foi usado.
    /// </summary>
    public bool ProcessarTexto(string texto)
    {
        if (Etapa != EtapaPdv.Tamanho || _grade is null || string.IsNullOrWhiteSpace(texto)) return false;

        var agora = DateTime.UtcNow;
        if (agora - _digitadoNoTamanhoEm > TimeSpan.FromMilliseconds(900)) _digitadoNoTamanho = "";
        _digitadoNoTamanhoEm = agora;
        _digitadoNoTamanho += texto.Trim();

        var indice = AcharTamanho(_digitadoNoTamanho);
        if (indice < 0)
        {
            _digitadoNoTamanho = texto.Trim(); // não achou: recomeça só com a última tecla
            indice = AcharTamanho(_digitadoNoTamanho);
        }

        if (indice >= 0 && _grade.IrPara(indice))
        {
            AtualizarSelecaoTamanho();
            Mensagem = "";
        }
        return true;
    }

    private int AcharTamanho(string comeco) =>
        _grade!.Opcoes.ToList().FindIndex(o => o.Tamanho.StartsWith(comeco, StringComparison.OrdinalIgnoreCase));

    // ================= Atalhos globais =================

    /// <summary>F2: foco na busca (e a tela seleciona o texto). Se há uma modal do fluxo aberta, ela é fechada.</summary>
    public void PedirBusca()
    {
        if (Etapa is EtapaPdv.Tamanho or EtapaPdv.Quantidade or EtapaPdv.Desconto or EtapaPdv.Pagamento)
            VoltarParaBusca();
        else if (Etapa == EtapaPdv.Busca)
            FocoSolicitado?.Invoke(AlvoFoco.Busca);
    }

    /// <summary>F8: alterna a tabela de preço (varejo/atacado). Só na busca, para não mudar preços no meio de uma modal.</summary>
    [RelayCommand]
    private void AlternarTabela()
    {
        if (Etapa != EtapaPdv.Busca || _ocupado) return;

        _carrinho.MudarTabela(_carrinho.Tabela == TabelaPreco.Atacado ? TabelaPreco.Varejo : TabelaPreco.Atacado);
        Sincronizar();
        OnPropertyChanged(nameof(TabelaTexto));
        OnPropertyChanged(nameof(EhAtacado));
        MostrarSucesso($"Tabela de preço: {TabelaTexto}. (Espaço, com a busca vazia, troca de novo)");
        FocoSolicitado?.Invoke(AlvoFoco.Busca);
    }

    [RelayCommand]
    private void FecharVenda() => AbrirDesconto();

    /// <summary>Esvazia o carrinho depois de perguntar. O Esc não esvazia mais: foi só fechar o modal.</summary>
    [RelayCommand]
    private void Esvaziar()
    {
        if (_carrinho.Vazio || Etapa != EtapaPdv.Busca) return;
        if (!Confirmar("Esvaziar o carrinho? Todos os itens serão removidos.")) return;

        LimparCarrinho();
        Mensagem = "";
        FocoSolicitado?.Invoke(AlvoFoco.Busca);
    }
}
