using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Tela de sucesso, venda em andamento (rascunho) e edição pela linha do carrinho.</summary>
public partial class PdvViewModel
{
    // ================= Etapa: sucesso =================

    private void AtualizarOpcoesSucesso()
    {
        if (OpcoesSucesso.Count == 0)
        {
            OpcoesSucesso.Add(new OpcaoViewModel("Reimprimir cupom"));
            OpcoesSucesso.Add(new OpcaoViewModel("Nova venda"));
        }
        OpcoesSucesso[0].Selecionada = _sucessoSel.Opcao == OpcaoSucesso.Reimprimir;
        OpcoesSucesso[1].Selecionada = _sucessoSel.Opcao == OpcaoSucesso.NovaVenda;
    }

    private void NovaVenda()
    {
        Busca = "";
        Mensagem = "";
        IrPara(EtapaPdv.Busca);
    }

    /// <summary>Reimprime o cupom da venda que acabou de ser finalizada (mesmo com a impressão automática desligada).</summary>
    private async Task ReimprimirCupomAsync()
    {
        await _cupons.ImprimirAsync(_ultimaVendaId);
        MostrarSucesso("Cupom enviado para a impressora.");
    }

    // ================= Rascunho =================

    /// <summary>
    /// Chamado ao abrir o programa: se a venda anterior ficou no meio (o app foi fechado com itens no
    /// carrinho), ela volta, já conferida com o estoque e os produtos de agora.
    /// </summary>
    public Task RestaurarRascunhoAsync() => ExecutarAsync(async () =>
    {
        var restaurado = await _rascunho.RestaurarAsync();
        if (restaurado is null) return;

        _carrinho = restaurado.Carrinho;
        Itens.Clear();
        Sincronizar();
        OnPropertyChanged(nameof(TabelaTexto));
        OnPropertyChanged(nameof(EhAtacado));

        var texto = $"Venda em andamento restaurada ({_carrinho.QuantidadeTotal} unidade(s)).";
        if (restaurado.Avisos.Count > 0)
        {
            texto += " " + string.Join(" ", restaurado.Avisos);
            MostrarErro(texto);
        }
        else
        {
            MostrarSucesso(texto);
        }
    });

    private void SalvarRascunho()
    {
        try
        {
            _rascunho.Salvar(_carrinho, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falha ao gravar o rascunho (disco cheio, pasta bloqueada) não pode atrapalhar a venda.
        }
    }

    // ================= Edição pela linha do carrinho =================

    [RelayCommand]
    private void Aumentar(ItemCarrinhoViewModel? item)
    {
        if (item is null) return;
        Editar(() => _carrinho.AlterarQuantidade(item.Linha, item.Linha.Quantidade + 1));
    }

    [RelayCommand]
    private void Diminuir(ItemCarrinhoViewModel? item)
    {
        if (item is null) return;
        if (item.Linha.Quantidade <= 1) Editar(() => _carrinho.Remover(item.Linha));
        else Editar(() => _carrinho.AlterarQuantidade(item.Linha, item.Linha.Quantidade - 1));
    }

    [RelayCommand]
    private void Remover(ItemCarrinhoViewModel? item)
    {
        if (item is not null) Editar(() => _carrinho.Remover(item.Linha));
    }

    [RelayCommand]
    private void RemoverSelecionado()
    {
        if (ItemSelecionado is { } item) Editar(() => _carrinho.Remover(item.Linha));
    }

    void IEditorCarrinho.EditarQuantidade(ItemCarrinhoViewModel item, int quantidade) =>
        Editar(() => _carrinho.AlterarQuantidade(item.Linha, quantidade));

    /// <summary>Vazio devolve o preço de tabela; qualquer valor vira o preço de UMA unidade.</summary>
    void IEditorCarrinho.EditarPrecoUnitario(ItemCarrinhoViewModel item, string texto) => Editar(() =>
    {
        if (string.IsNullOrWhiteSpace(texto)) _carrinho.RestaurarPreco(item.Linha);
        else _carrinho.DefinirPrecoUnitario(item.Linha, LerPreco(texto));
    });

    /// <summary>Vazio devolve o preço de tabela; qualquer valor vira o TOTAL da linha.</summary>
    void IEditorCarrinho.EditarTotal(ItemCarrinhoViewModel item, string texto) => Editar(() =>
    {
        if (string.IsNullOrWhiteSpace(texto)) _carrinho.RestaurarPreco(item.Linha);
        else _carrinho.DefinirTotalLinha(item.Linha, LerPreco(texto));
    });

    private static long LerPreco(string texto) =>
        Dinheiro.TentarConverter(texto, out var centavos)
            ? centavos
            : throw new RegraDeNegocioException("Valor inválido. Exemplo: 12,50");
}
