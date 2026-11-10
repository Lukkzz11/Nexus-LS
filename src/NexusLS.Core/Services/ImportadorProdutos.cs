using System.Globalization;
using System.Text;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Um produto lido do CSV, já validado, pronto para cadastrar.</summary>
public record ProdutoImportado(
    int Linha,
    DadosProduto Dados,
    int EstoqueSemTamanho,
    IReadOnlyList<(string Tamanho, int Estoque)> Tamanhos);

public record ProblemaImportacao(int Linha, string Motivo)
{
    public override string ToString() => $"Linha {Linha}: {Motivo}";
}

/// <summary>O que o arquivo contém, antes de gravar qualquer coisa.</summary>
public record AnaliseImportacao(
    IReadOnlyList<ProdutoImportado> Produtos,
    IReadOnlyList<ProblemaImportacao> Problemas,
    IReadOnlyList<string> ColunasReconhecidas,
    string? ErroDoArquivo = null)
{
    public bool PodeImportar => ErroDoArquivo is null && Produtos.Count > 0;
}

public record ResultadoImportacao(int Importados, IReadOnlyList<ProblemaImportacao> Ignorados, int Atualizados = 0, int SemMudanca = 0);

/// <summary>
/// Importa produtos de um arquivo CSV (o Excel salva assim: "CSV (separado por vírgulas)" ou "CSV UTF-8").
/// A primeira linha tem os títulos das colunas, em qualquer ordem. Só o nome e o preço de venda são obrigatórios.
/// Regras de segurança: NUNCA altera nem apaga produto existente (se já existe, a linha é ignorada e explicada),
/// e uma linha ruim não impede as outras.
/// </summary>
public class ImportadorProdutos
{
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;

    public ImportadorProdutos(ProdutoService produtos, EstoqueService estoque)
    {
        _produtos = produtos;
        _estoque = estoque;
    }

    // ---------- leitura do arquivo ----------

    /// <summary>Lê os bytes do arquivo: UTF-8 (com ou sem BOM) e, se não for UTF-8 válido, Windows-1252 (Excel antigo).</summary>
    public static string Decodificar(byte[] bytes)
    {
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            var texto = utf8.GetString(bytes);
            return texto.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static readonly Dictionary<string, string[]> Apelidos = new()
    {
        ["nome"] = new[] { "nome", "produto", "descricao", "nomedoproduto", "descricaodoproduto", "item" },
        ["codigo"] = new[] { "codigodebarras", "codigobarras", "codbarras", "codigo", "ean", "gtin", "barras", "cod" },
        ["referencia"] = new[] { "referencia", "ref", "sku", "modelo" },
        ["categoria"] = new[] { "categoria", "grupo", "departamento", "secao" },
        ["venda"] = new[] { "precovarejo", "varejo", "precodevenda", "precovenda", "valorvenda", "valor", "preco", "venda" },
        ["atacado"] = new[] { "precoatacado", "atacado", "precodeatacado", "valoratacado" },
        ["custo"] = new[] { "precocusto", "precodecusto", "custo", "valorcusto", "valordecusto" },
        ["estoque"] = new[] { "estoque", "quantidade", "qtd", "qtde", "saldo", "estoqueatual", "estoqueinicial" },
        ["minimo"] = new[] { "estoqueminimo", "minimo", "min", "estoquemin" },
        ["tamanho"] = new[] { "tamanho", "tam", "grade" },
    };

    private static readonly string[] NomesBonitos = { "nome", "codigo", "referencia", "categoria", "venda", "atacado", "custo", "estoque", "minimo", "tamanho" };

    private static string Normalizar(string texto)
    {
        var decomposto = (texto ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Separa o texto em linhas e campos, respeitando aspas e descobrindo o separador (; , tab |).</summary>
    public static List<string[]> LerCsv(string texto)
    {
        var primeira = texto.Split('\n', 2)[0];
        var separador = new[] { ';', ',', '\t', '|' }.OrderByDescending(s => primeira.Count(c => c == s)).First();

        var linhas = new List<string[]>();
        var campos = new List<string>();
        var campo = new StringBuilder();
        var entreAspas = false;

        void FecharCampo() { campos.Add(campo.ToString()); campo.Clear(); }
        void FecharLinha()
        {
            FecharCampo();
            linhas.Add(campos.ToArray());
            campos = new List<string>();
        }

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreAspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                    else entreAspas = false;
                }
                else campo.Append(c);
            }
            else if (c == '"') entreAspas = true;
            else if (c == separador) FecharCampo();
            else if (c == '\r') { }
            else if (c == '\n') FecharLinha();
            else campo.Append(c);
        }
        if (campo.Length > 0 || campos.Count > 0) FecharLinha();
        return linhas;
    }

    // ---------- análise (não grava nada) ----------

    public static AnaliseImportacao Analisar(string csv)
    {
        var linhas = LerCsv(csv ?? "");
        if (linhas.Count == 0)
            return Erro("O arquivo está vazio.");

        // Descobre qual coluna é qual pelos títulos da primeira linha.
        var colunas = new Dictionary<string, int>();
        var cabecalho = linhas[0].Select(Normalizar).ToArray();
        foreach (var chave in NomesBonitos)
        {
            var indice = Array.FindIndex(cabecalho, h => Apelidos[chave].Contains(h));
            if (indice >= 0 && !colunas.ContainsValue(indice)) colunas[chave] = indice;
        }
        // "preço" sozinho e "valor" são ambíguos com atacado/custo: a ordem em Apelidos já dá prioridade a varejo.

        if (!colunas.ContainsKey("nome"))
            return Erro("Não achei a coluna do nome. A primeira linha do arquivo precisa ter os títulos, e um deles deve ser \"Nome\" (ou \"Produto\", \"Descrição\").");
        if (!colunas.ContainsKey("venda"))
            return Erro("Não achei a coluna do preço de venda. Use o título \"Preço\", \"Preço varejo\" ou \"Valor\".");

        string Valor(string[] l, string chave) =>
            colunas.TryGetValue(chave, out var i) && i < l.Length ? l[i].Trim() : "";

        var problemas = new List<ProblemaImportacao>();
        var grupos = new Dictionary<string, Grupo>();
        var ordem = new List<Grupo>();

        for (var n = 1; n < linhas.Count; n++)
        {
            var l = linhas[n];
            var numero = n + 1;
            if (l.All(string.IsNullOrWhiteSpace)) continue; // linha em branco

            var nome = Valor(l, "nome");
            if (nome.Length == 0) { problemas.Add(new(numero, "sem nome.")); continue; }

            if (!Dinheiro.TentarConverter(Valor(l, "venda"), out var venda))
            { problemas.Add(new(numero, $"{nome}: preço de venda ausente ou inválido (\"{Valor(l, "venda")}\").")); continue; }

            long atacado = 0, custo = 0;
            if (Valor(l, "atacado").Length > 0 && !Dinheiro.TentarConverter(Valor(l, "atacado"), out atacado))
            { problemas.Add(new(numero, $"{nome}: preço de atacado inválido (\"{Valor(l, "atacado")}\").")); continue; }
            if (Valor(l, "custo").Length > 0 && !Dinheiro.TentarConverter(Valor(l, "custo"), out custo))
            { problemas.Add(new(numero, $"{nome}: preço de custo inválido (\"{Valor(l, "custo")}\").")); continue; }

            if (!TentarInteiro(Valor(l, "estoque"), out var estoque) || estoque < 0)
            { problemas.Add(new(numero, $"{nome}: estoque inválido (\"{Valor(l, "estoque")}\"). Use um número inteiro, sem negativo.")); continue; }
            if (!TentarInteiro(Valor(l, "minimo"), out var minimo) || minimo < 0)
            { problemas.Add(new(numero, $"{nome}: estoque mínimo inválido (\"{Valor(l, "minimo")}\").")); continue; }

            var codigo = Valor(l, "codigo");
            var referencia = Valor(l, "referencia");
            var tamanho = Valor(l, "tamanho").ToUpperInvariant();
            if (tamanho.Length > 30) { problemas.Add(new(numero, $"{nome}: tamanho com mais de 30 caracteres.")); continue; }
            if (string.Equals(tamanho, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase)) tamanho = "";

            // Linhas do mesmo produto (mesmo código de barras, ou mesmo nome + referência) viram um produto com vários tamanhos.
            var chave = codigo.Length > 0 ? "c:" + codigo : "n:" + nome.ToLowerInvariant() + "|" + referencia.ToLowerInvariant();
            if (!grupos.TryGetValue(chave, out var grupo))
            {
                grupo = new Grupo(numero, new DadosProduto(nome, codigo.Length > 0 ? codigo : null, venda, custo, minimo,
                    referencia.Length > 0 ? referencia : null, atacado,
                    Valor(l, "categoria").Length > 0 ? Valor(l, "categoria") : null));
                grupos[chave] = grupo;
                ordem.Add(grupo);
            }
            if (grupo.Tamanhos.ContainsKey(tamanho))
            {
                problemas.Add(new(numero, $"{nome}: linha repetida{(tamanho.Length > 0 ? $" para o tamanho {tamanho}" : "")} (já aparece na linha {grupo.Linha}). Ignorada."));
                continue;
            }
            grupo.Tamanhos[tamanho] = estoque;
        }

        var produtos = new List<ProdutoImportado>();
        foreach (var g in ordem)
        {
            g.Tamanhos.TryGetValue("", out var semTamanho);
            var comTamanho = g.Tamanhos.Where(t => t.Key.Length > 0).Select(t => (t.Key, t.Value)).ToList();
            produtos.Add(new ProdutoImportado(g.Linha, g.Dados, semTamanho, comTamanho));
        }
        var reconhecidas = colunas.Keys.Select(k => k switch
        {
            "nome" => "Nome", "codigo" => "Código de barras", "referencia" => "Referência", "categoria" => "Categoria",
            "venda" => "Preço de venda", "atacado" => "Preço de atacado", "custo" => "Preço de custo",
            "estoque" => "Estoque", "minimo" => "Estoque mínimo", _ => "Tamanho"
        }).ToList();
        return new AnaliseImportacao(produtos, problemas, reconhecidas);
    }

    private static AnaliseImportacao Erro(string mensagem) =>
        new(Array.Empty<ProdutoImportado>(), Array.Empty<ProblemaImportacao>(), Array.Empty<string>(), mensagem);

    private static bool TentarInteiro(string texto, out int valor)
    {
        valor = 0;
        if (texto.Length == 0) return true; // vazio = 0
        return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out valor);
    }

    private sealed class Grupo
    {
        public Grupo(int linha, DadosProduto dados) { Linha = linha; Dados = dados; }
        public int Linha { get; }
        public DadosProduto Dados { get; }
        public Dictionary<string, int> Tamanhos { get; } = new();
    }

    // ---------- gravação ----------

    /// <summary>
    /// Compara com a base: produto novo é cadastrado; produto que já existe (mesmo código de barras, mesma referência,
    /// ou mesmo nome) é atualizado SÓ no que mudou (preços, nome, categoria, mínimo, referência, código) e ganha os
    /// tamanhos que faltavam. Campos vazios/zero no arquivo não apagam o que já existe, e o estoque de produto
    /// existente não é alterado (evita contar duas vezes). O estoque dos novos entra como "Importação de CSV".
    /// </summary>
    public async Task<ResultadoImportacao> ImportarAsync(
        AnaliseImportacao analise, IProgress<int>? progresso = null, CancellationToken ct = default)
    {
        var importados = 0;
        var atualizados = 0;
        var semMudanca = 0;
        var ignorados = new List<ProblemaImportacao>();
        var feitos = 0;

        var existentes = await _produtos.ListarComTamanhosAsync();

        foreach (var p in analise.Produtos)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var atual = Achar(existentes, p.Dados);
                if (atual is null)
                {
                    var produto = await _produtos.CadastrarAsync(p.Dados, 0);
                    if (p.EstoqueSemTamanho > 0)
                        await _estoque.AjustarAsync(produto.Id, TipoMovimentacao.Entrada, p.EstoqueSemTamanho, "Importação de CSV");
                    foreach (var (tamanho, estoque) in p.Tamanhos)
                    {
                        await _produtos.AdicionarTamanhoAsync(produto.Id, tamanho);
                        if (estoque > 0)
                            await _estoque.AjustarAsync(produto.Id, TipoMovimentacao.Entrada, estoque, "Importação de CSV", tamanho);
                    }
                    importados++;
                    existentes.Add(produto);
                }
                else
                {
                    var mudou = false;
                    var novos = Mesclar(atual, p.Dados);
                    if (novos is not null)
                    {
                        await _produtos.EditarAsync(atual.Id, novos);
                        mudou = true;
                    }
                    var jaTem = atual.Tamanhos.Select(t => t.Tamanho.ToUpperInvariant()).ToHashSet();
                    foreach (var (tamanho, estoque) in p.Tamanhos)
                    {
                        if (jaTem.Contains(tamanho.ToUpperInvariant())) continue;
                        await _produtos.AdicionarTamanhoAsync(atual.Id, tamanho);
                        if (estoque > 0)
                            await _estoque.AjustarAsync(atual.Id, TipoMovimentacao.Entrada, estoque, "Importação de CSV", tamanho);
                        mudou = true;
                    }
                    if (mudou) atualizados++; else semMudanca++;
                }
            }
            catch (RegraDeNegocioException ex)
            {
                ignorados.Add(new ProblemaImportacao(p.Linha, $"{p.Dados.Nome}: {ex.Message}"));
            }
            progresso?.Report(++feitos);
        }
        return new ResultadoImportacao(importados, ignorados, atualizados, semMudanca);
    }

    private static Produto? Achar(List<Produto> base_, DadosProduto d)
    {
        static string N(string? t) => (t ?? "").Trim().Replace(" ", "").ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(d.CodigoBarras))
        {
            var c = d.CodigoBarras.Trim();
            var porCodigo = base_.FirstOrDefault(x => x.CodigoBarras == c || x.CodigoInterno == c);
            if (porCodigo is not null) return porCodigo;
        }
        if (!string.IsNullOrWhiteSpace(d.Referencia))
        {
            var r = N(d.Referencia);
            var porRef = base_.FirstOrDefault(x => N(x.Referencia) == r);
            if (porRef is not null) return porRef;
        }
        var nome = N(d.Nome);
        return base_.FirstOrDefault(x => N(x.Nome) == nome && (string.IsNullOrWhiteSpace(d.Referencia) || N(x.Referencia) == N(d.Referencia)));
    }

    /// <summary>Os dados já cadastrados com o que veio no arquivo por cima; nulo se nada mudou.</summary>
    private static DadosProduto? Mesclar(Produto atual, DadosProduto csv)
    {
        var novo = new DadosProduto(
            csv.Nome.Trim(),
            string.IsNullOrWhiteSpace(csv.CodigoBarras) ? atual.CodigoBarras : csv.CodigoBarras.Trim(),
            csv.PrecoVendaCentavos,
            csv.PrecoCustoCentavos > 0 ? csv.PrecoCustoCentavos : atual.PrecoCustoCentavos,
            csv.EstoqueMinimo > 0 ? csv.EstoqueMinimo : atual.EstoqueMinimo,
            string.IsNullOrWhiteSpace(csv.Referencia) ? atual.Referencia : csv.Referencia.Trim(),
            csv.PrecoAtacadoCentavos > 0 ? csv.PrecoAtacadoCentavos : atual.PrecoAtacadoCentavos,
            string.IsNullOrWhiteSpace(csv.Categoria) ? atual.Categoria : csv.Categoria.Trim(),
            atual.VenderSemBaixa, atual.Sobra, atual.Cores);

        var igual = novo.Nome == atual.Nome
                    && novo.CodigoBarras == atual.CodigoBarras
                    && novo.PrecoVendaCentavos == atual.PrecoVendaCentavos
                    && novo.PrecoCustoCentavos == atual.PrecoCustoCentavos
                    && novo.EstoqueMinimo == atual.EstoqueMinimo
                    && novo.Referencia == atual.Referencia
                    && novo.PrecoAtacadoCentavos == atual.PrecoAtacadoCentavos
                    && novo.Categoria == atual.Categoria;
        return igual ? null : novo;
    }
}
