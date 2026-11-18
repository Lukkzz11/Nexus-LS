using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Movel;

/// <summary>Um tamanho da grade, com o estoque e o codigo que a etiqueta imprime.</summary>
public sealed record TamanhoConsulta(
    string Nome,
    int Estoque,
    string? Codigo,
    bool Selecionado);

/// <summary>O que a tela do celular precisa saber de um produto.</summary>
public sealed record ProdutoConsulta(
    int Id,
    string Nome,
    string? Referencia,
    string? Categoria,
    string? Cores,
    long PrecoVarejoCentavos,
    long PrecoAtacadoCentavos,
    int EstoqueTotal,
    bool EstoqueBaixo,
    bool VenderSemBaixa,
    string? Foto,
    string? TamanhoSelecionado,
    IReadOnlyList<TamanhoConsulta> Tamanhos);

/// <summary>Produto parecido, mostrado quando o codigo lido nao existe.</summary>
public sealed record SugestaoConsulta(
    int Id,
    string Nome,
    string? Referencia,
    string? Categoria,
    long PrecoVarejoCentavos,
    int EstoqueTotal);

public sealed record RespostaConsulta(
    bool Encontrado,
    string CodigoLido,
    string Mensagem,
    ProdutoConsulta? Produto,
    IReadOnlyList<SugestaoConsulta> Sugestoes);

/// <summary>
/// A regra da consulta no celular. Reaproveita os mesmos servicos do PDV, para o celular
/// nunca discordar do caixa: codigo de tamanho, referencia, codigo de barras e busca por nome
/// seguem exatamente a mesma ordem do <see cref="ProdutoService"/>.
/// </summary>
public sealed class ConsultaDeProduto
{
    private const int MaximoSugestoes = 12;

    private readonly ProdutoService _produtos;

    public ConsultaDeProduto(ProdutoService produtos) => _produtos = produtos;

    public async Task<RespostaConsulta> ConsultarAsync(string? codigoOuTexto, CancellationToken ct = default)
    {
        var texto = (codigoOuTexto ?? "").Trim();
        if (texto.Length == 0)
        {
            return new RespostaConsulta(false, "", "Digite ou leia um código.", null, Array.Empty<SugestaoConsulta>());
        }

        // 1) Codigo de tamanho (referencia + numero), igual a etiqueta impressa.
        var porTamanho = await _produtos.ObterPorCodigoDeTamanhoAsync(texto);
        if (porTamanho is not null)
        {
            return new RespostaConsulta(
                true,
                texto,
                "",
                await MontarAsync(porTamanho.Value.Produto, porTamanho.Value.Tamanho),
                Array.Empty<SugestaoConsulta>());
        }

        // 2) Referencia exata, 3) codigo de barras/codigo interno exato.
        var produto = await _produtos.ObterPorReferenciaAsync(texto)
            ?? await _produtos.ObterPorCodigoBarrasAsync(texto);

        if (produto is not null)
        {
            return new RespostaConsulta(true, texto, "", await MontarAsync(produto, null), Array.Empty<SugestaoConsulta>());
        }

        // 4) Busca por nome/referencia: um resultado vira o produto; varios viram sugestoes.
        var encontrados = await _produtos.BuscarAsync(texto);
        if (encontrados.Count == 1)
        {
            return new RespostaConsulta(true, texto, "", await MontarAsync(encontrados[0], null), Array.Empty<SugestaoConsulta>());
        }

        var sugestoes = encontrados.Take(MaximoSugestoes).Select(ParaSugestao).ToList();
        var mensagem = sugestoes.Count == 0
            ? $"Nenhum produto com \"{texto}\"."
            : "Nada com esse código. Veja se algum destes é o produto:";

        return new RespostaConsulta(false, texto, mensagem, null, sugestoes);
    }

    public async Task<IReadOnlyList<SugestaoConsulta>> BuscarAsync(string? texto, CancellationToken ct = default)
    {
        var termo = (texto ?? "").Trim();
        if (termo.Length < 2) return Array.Empty<SugestaoConsulta>();

        var lista = await _produtos.BuscarAsync(termo);
        return lista.Take(MaximoSugestoes).Select(ParaSugestao).ToList();
    }

    private async Task<ProdutoConsulta> MontarAsync(Produto produto, ProdutoTamanho? selecionado)
    {
        var tamanhos = await _produtos.ListarTamanhosAsync(produto.Id);

        // Produto sem grade mostra a linha "Sem tamanho"; com grade, ela nao interessa na loja.
        var mostrarSemTamanho = tamanhos.Count <= 1;
        var lista = tamanhos
            .Where(t => !t.EhSemTamanho || mostrarSemTamanho)
            .Select(t => new TamanhoConsulta(
                t.EhSemTamanho ? "Sem tamanho" : t.Tamanho,
                t.Estoque,
                t.EhSemTamanho ? null : t.CodigoBarras,
                selecionado is not null && t.Id == selecionado.Id))
            .ToList();

        var foto = string.IsNullOrWhiteSpace(produto.FotoArquivo) ? null : $"/api/foto/{produto.Id}";

        return new ProdutoConsulta(
            produto.Id,
            produto.Nome,
            produto.Referencia,
            produto.Categoria,
            produto.Cores,
            produto.PrecoVendaCentavos,
            produto.PrecoPara(TabelaPreco.Atacado),
            produto.EstoqueAtual,
            produto.EstoqueBaixo,
            produto.VenderSemBaixa,
            foto,
            lista.FirstOrDefault(t => t.Selecionado)?.Nome,
            lista);
    }

    private static SugestaoConsulta ParaSugestao(Produto p) =>
        new(p.Id, p.Nome, p.Referencia, p.Categoria, p.PrecoVendaCentavos, p.EstoqueAtual);
}
