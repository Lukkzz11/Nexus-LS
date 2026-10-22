using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Etapa busca, tamanho e quantidade: achar o produto e colocá-lo no carrinho.</summary>
public partial class PdvViewModel
{
    // ================= Etapa: busca → tamanho → quantidade =================

    private async Task AtualizarResultadosAsync(string texto)
    {
        _buscaCts?.Cancel();
        var cts = _buscaCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token); // espera o operador parar de digitar
            texto = texto.Trim();
            if (texto.Length == 0)
            {
                Resultados.Clear();
                return;
            }

            var lista = await _produtos.BuscarAsync(texto);
            if (cts.IsCancellationRequested) return;

            Resultados.Clear();
            foreach (var p in lista.Take(8)) Resultados.Add(p);
            ResultadoSelecionado = Resultados.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // chegou uma digitação mais nova; esta busca não vale mais
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao buscar: " + ex.Message);
        }
    }

    /// <summary>
    /// Enter na busca: item destacado com ↑↓, senão código de barras exato (leitor), senão o primeiro resultado.
    /// Depois abre SEMPRE o modal de tamanho.
    /// </summary>
    private async Task EscolherProdutoAsync()
    {
        var texto = Busca;
        var destacado = _desceu ? ResultadoSelecionado : null;

        var produto = await _produtos.EscolherParaVendaAsync(texto, destacado);
        if (produto is null)
        {
            MostrarErro($"Nenhum produto encontrado para \"{texto.Trim()}\".");
            return;
        }

        _gradeReal = await _produtos.ListarTamanhosAsync(produto.Id);
        if (produto.VenderSemBaixa)
            foreach (var t in _gradeReal) t.Estoque = Produto.EstoqueVirtual; // sem controle: nunca esgota
        var opcoes = GradeTamanhos.OpcoesParaVenda(_gradeReal, _carrinho.Tabela)
            .Select(t => new ProdutoTamanho   // o estoque mostrado já desconta o que está no carrinho
            {
                Id = t.Id,
                ProdutoId = t.ProdutoId,
                Tamanho = t.Tamanho,
                Estoque = Math.Max(0, t.Estoque - _carrinho.QuantidadeDe(produto.Id, t.Tamanho))
            })
            .ToList();

        _produtoAtual = produto;
        _grade = new GradeTamanhos(opcoes);
        ProdutoModalTexto = $"{produto.Nome}  ·  {Dinheiro.Formatar(produto.PrecoPara(_carrinho.Tabela))}";

        OpcoesTamanho.Clear();
        foreach (var o in _grade.Opcoes)
            OpcoesTamanho.Add(new OpcaoViewModel(o.Tamanho,
                produto.VenderSemBaixa ? "sem controle de estoque" : o.Estoque > 0 ? $"{o.Estoque} em estoque" : "esgotado", o.Estoque <= 0));
        AtualizarSelecaoTamanho();

        Resultados.Clear();
        Mensagem = "";
        IrPara(EtapaPdv.Tamanho);

        // Modo rápido: com uma única opção disponível, não há o que perguntar.
        if (_grade.Opcoes.Count == 1 && _grade.Opcoes[0].Estoque > 0
            && _configuracao is not null && !(await _configuracao.ObterAsync()).PerguntarTamanho)
            ConfirmarTamanho();
    }

    private void MoverGrade(DirecaoGrade direcao)
    {
        _grade!.Mover(direcao);
        AtualizarSelecaoTamanho();
        Mensagem = "";
    }

    private void AtualizarSelecaoTamanho()
    {
        for (var i = 0; i < OpcoesTamanho.Count; i++) OpcoesTamanho[i].Selecionada = i == _grade!.Indice;
    }

    private void ConfirmarTamanho()
    {
        var escolhido = _grade!.Selecionado;
        if (escolhido is null || escolhido.Estoque <= 0)
        {
            MostrarErro("Este tamanho está esgotado. Escolha outro.");
            return;
        }

        _tamanhoAtual = _gradeReal.First(t => t.Tamanho == escolhido.Tamanho);
        _quantidadeSel = new SeletorQuantidade(escolhido.Estoque);
        QuantidadeTexto = "1";
        TamanhoModalTexto = escolhido.Tamanho == Tamanhos.SemTamanho ? "Sem tamanho" : $"Tamanho {escolhido.Tamanho}";
        QuantidadeMaximoTexto = _produtoAtual!.VenderSemBaixa ? "Sem controle de estoque" : $"Disponível: {escolhido.Estoque}";
        Mensagem = "";
        IrPara(EtapaPdv.Quantidade);
    }

    private void ConfirmarQuantidade()
    {
        try
        {
            if (!int.TryParse(QuantidadeTexto, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var quantidade) || quantidade < 1)
            {
                MostrarErro("Digite uma quantidade de 1 em diante.");
                return;
            }

            var linha = _carrinho.Adicionar(_produtoAtual!, _tamanhoAtual!, quantidade);

            Busca = "";
            Sincronizar();
            ItemSelecionado = Itens.FirstOrDefault(i => i.Linha == linha);
            IrPara(EtapaPdv.Busca);
            MostrarSucesso($"{quantidade}× {_produtoAtual!.Nome} adicionado.");
        }
        catch (RegraDeNegocioException ex)
        {
            MostrarErro(ex.Message);
        }
    }

    private void VoltarParaBusca()
    {
        IrPara(EtapaPdv.Busca);
        Mensagem = "";
    }
}
