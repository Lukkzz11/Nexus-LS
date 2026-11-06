using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public enum SituacaoLinhaImportacao { Novo, Atualizado, SemMudanca, Erro }

/// <summary>O que uma linha do CSV diz do cliente. Nulo = a coluna não existe ou a célula está vazia: o valor atual é mantido.</summary>
public record DadosImportadosCliente(string Nome, string Documento, string? RazaoSocial, string? Telefone, string? Email,
    string? Observacao, long? LimiteCreditoCentavos, long? CreditoCentavos);

/// <summary>Uma linha da pré-visualização: o que vai acontecer com ela e, se for erro, o motivo.</summary>
public record LinhaImportacaoCliente(int Numero, SituacaoLinhaImportacao Situacao, string Motivo, string Nome, string Documento,
    DadosImportadosCliente? Dados = null, int? ClienteId = null)
{
    public string SituacaoTexto => Situacao switch
    {
        SituacaoLinhaImportacao.Novo => "Novo",
        SituacaoLinhaImportacao.Atualizado => "Atualiza",
        SituacaoLinhaImportacao.SemMudanca => "Sem mudança",
        _ => "Erro"
    };
}

/// <summary>A pré-visualização da importação: contagens e uma linha para cada linha do arquivo.</summary>
public record PreviaImportacaoClientes(IReadOnlyList<LinhaImportacaoCliente> Linhas, string? Erro = null)
{
    public int Novos => Linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.Novo);
    public int Atualizados => Linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.Atualizado);
    public int SemMudanca => Linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.SemMudanca);
    public int Erros => Linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.Erro);
    public bool PodeImportar => Erro is null && Novos + Atualizados > 0;

    public string Resumo => Erro ?? $"{Novos} novo(s) · {Atualizados} para atualizar · {SemMudanca} sem mudança · {Erros} com erro";
}

/// <summary>
/// Lê o CSV de clientes (as mesmas colunas da exportação, em qualquer ordem) e compara com os clientes que já existem, sem
/// gravar nada. A linha casa com um cliente ativo pelo CPF/CNPJ (só os dígitos): casou = atualiza; não casou = novo.
/// Cada linha com problema vira um erro com o motivo e não impede as outras. Nunca apaga nem desativa ninguém. Código puro.
/// </summary>
public static class ImportadorClientes
{
    /// <summary>Os títulos que a exportação escreve (e que a importação entende).</summary>
    public static readonly string[] Colunas =
        { "CPF/CNPJ", "Nome", "Razão social", "Telefone", "E-mail", "Crédito", "Limite a prazo", "Observação", "Situação" };

    private static readonly Dictionary<string, string[]> Apelidos = new()
    {
        ["documento"] = new[] { "cpfcnpj", "cpfoucnpj", "cnpjcpf", "documento", "cpf", "cnpj" },
        ["nome"] = new[] { "nome", "cliente", "nomedocliente" },
        ["razao"] = new[] { "razaosocial", "razao" },
        ["telefone"] = new[] { "telefone", "celular", "whatsapp", "fone", "contato", "numerodecontato", "telefonedecontato" },
        ["email"] = new[] { "email", "emaildecontato", "emailcontato" },
        ["credito"] = new[] { "credito", "creditodocliente", "saldodecredito", "saldo" },
        ["limite"] = new[] { "limiteaprazo", "limitedecredito", "limite", "limiteprazo" },
        ["obs"] = new[] { "observacao", "observacoes", "obs" },
        ["situacao"] = new[] { "situacao", "status" },
    };

    private static readonly Regex PadraoEmail = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static bool EmailValido(string? email) => !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= 150 && PadraoEmail.IsMatch(email.Trim());

    public static PreviaImportacaoClientes Analisar(string? csv, IReadOnlyCollection<Cliente> existentes)
    {
        var tabela = ImportadorProdutos.LerCsv(csv ?? "");
        var primeira = tabela.FindIndex(l => l.Any(c => !string.IsNullOrWhiteSpace(c)));
        if (primeira < 0) return Falha("O arquivo está vazio.");

        var colunas = new Dictionary<string, int>();
        for (var i = 0; i < tabela[primeira].Length; i++)
        {
            var chave = Normalizar(tabela[primeira][i]);
            var achou = Apelidos.FirstOrDefault(a => a.Value.Contains(chave)).Key;
            if (achou is not null && !colunas.ContainsKey(achou)) colunas[achou] = i;
        }
        if (!colunas.ContainsKey("documento"))
            return Falha("Não encontrei a coluna \"CPF/CNPJ\" na primeira linha. Use o arquivo exportado pelo Nexus como modelo.");
        if (!colunas.ContainsKey("nome"))
            return Falha("Não encontrei a coluna \"Nome\" na primeira linha. Use o arquivo exportado pelo Nexus como modelo.");

        var ativosPorDocumento = existentes.Where(c => c.Ativo && ValidadorDocumento.SoDigitos(c.Documento).Length > 0)
            .GroupBy(c => ValidadorDocumento.SoDigitos(c.Documento)).ToDictionary(g => g.Key, g => g.ToList());

        var linhas = new List<LinhaImportacaoCliente>();
        var vistos = new Dictionary<string, int>(); // documento -> primeira linha do arquivo
        for (var indice = primeira + 1; indice < tabela.Count; indice++)
        {
            var campos = tabela[indice];
            if (campos.All(string.IsNullOrWhiteSpace)) continue;
            var numero = indice + 1; // a linha como a planilha mostra (a 1 é o título)

            string Ler(string coluna) => colunas.TryGetValue(coluna, out var i) && i < campos.Length ? Limpar(campos[i]) : "";
            var nome = Ler("nome");
            var docTexto = Ler("documento");

            LinhaImportacaoCliente Erro(string motivo) => new(numero, SituacaoLinhaImportacao.Erro, motivo, nome, docTexto);

            if (string.IsNullOrWhiteSpace(nome)) { linhas.Add(Erro("Nome em branco.")); continue; }
            if (nome.Length > 200) { linhas.Add(Erro("Nome com mais de 200 letras.")); continue; }
            var doc = ValidadorDocumento.Validar(docTexto);
            if (!doc.Valido) { linhas.Add(Erro(doc.Erro!)); continue; }
            if (vistos.TryGetValue(doc.Digitos, out var jaVisto))
            { linhas.Add(Erro($"CPF/CNPJ repetido: já aparece na linha {jaVisto}.")); continue; }
            vistos[doc.Digitos] = numero;

            var razao = Ler("razao"); var telefone = Ler("telefone"); var email = Ler("email"); var obs = Ler("obs");
            if (razao.Length > 200) { linhas.Add(Erro("Razão social com mais de 200 letras.")); continue; }
            if (telefone.Length > 30) { linhas.Add(Erro("Telefone com mais de 30 caracteres.")); continue; }
            if (obs.Length > 500) { linhas.Add(Erro("Observação com mais de 500 letras.")); continue; }
            if (email.Length > 0 && !EmailValido(email)) { linhas.Add(Erro("E-mail inválido.")); continue; }

            long? credito = null, limite = null;
            var creditoTexto = Ler("credito"); var limiteTexto = Ler("limite");
            if (creditoTexto.Length > 0)
            {
                if (!Dinheiro.TentarConverter(creditoTexto, out var c)) { linhas.Add(Erro("Crédito inválido (use um valor como 50,00, sem negativo).")); continue; }
                credito = c;
            }
            if (limiteTexto.Length > 0)
            {
                if (!Dinheiro.TentarConverter(limiteTexto, out var l)) { linhas.Add(Erro("Limite a prazo inválido (use um valor como 500,00, sem negativo).")); continue; }
                limite = l;
            }

            var dados = new DadosImportadosCliente(nome, doc.Digitos, Nulo(razao), Nulo(telefone), Nulo(email), Nulo(obs), limite, credito);

            if (!ativosPorDocumento.TryGetValue(doc.Digitos, out var donos))
            { linhas.Add(new(numero, SituacaoLinhaImportacao.Novo, "Cliente novo.", nome, doc.Digitos, dados)); continue; }
            if (donos.Count > 1)
            { linhas.Add(Erro($"Há {donos.Count} clientes ativos com este documento no Nexus; resolva no cadastro antes de importar.")); continue; }

            var atual = donos[0];
            var mudancas = Diferencas(atual, dados);
            linhas.Add(mudancas.Count == 0
                ? new(numero, SituacaoLinhaImportacao.SemMudanca, $"Igual ao cadastro de {atual.Nome}.", nome, doc.Digitos, dados, atual.Id)
                : new(numero, SituacaoLinhaImportacao.Atualizado, $"Atualiza {atual.Nome}: {string.Join(", ", mudancas)}.", nome, doc.Digitos, dados, atual.Id));
        }

        return linhas.Count == 0 ? Falha("O arquivo só tem os títulos das colunas, nenhum cliente.") : new PreviaImportacaoClientes(linhas);
    }

    private static List<string> Diferencas(Cliente c, DadosImportadosCliente d)
    {
        var m = new List<string>();
        if (!string.Equals(c.Nome, d.Nome, StringComparison.Ordinal)) m.Add("nome");
        if (c.Documento != d.Documento) m.Add("documento sem máscara");
        if (d.RazaoSocial is not null && c.RazaoSocial != d.RazaoSocial) m.Add("razão social");
        if (d.Telefone is not null && c.Telefone != d.Telefone) m.Add("telefone");
        if (d.Email is not null && c.Email != d.Email) m.Add("e-mail");
        if (d.Observacao is not null && c.Observacao != d.Observacao) m.Add("observação");
        if (d.LimiteCreditoCentavos is long l && l != c.LimiteCreditoCentavos) m.Add("limite a prazo");
        if (d.CreditoCentavos is long cr && cr != c.CreditoCentavos) m.Add($"crédito {Dinheiro.Formatar(c.CreditoCentavos)} → {Dinheiro.Formatar(cr)}");
        return m;
    }

    private static PreviaImportacaoClientes Falha(string erro) => new(Array.Empty<LinhaImportacaoCliente>(), erro);

    private static string? Nulo(string t) => t.Length == 0 ? null : t;

    /// <summary>Tira espaços e o apóstrofo que a exportação põe na frente de texto que o Excel leria como fórmula (= + - @).</summary>
    private static string Limpar(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length > 1 && t[0] == '\'' && t[1] is '=' or '+' or '-' or '@') t = t[1..];
        return t;
    }

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
}

/// <summary>Exportação dos clientes em CSV (UTF-8 com BOM, separador ponto e vírgula, valores com vírgula): o mesmo padrão do <see cref="ExportadorCsv"/>.</summary>
public static class ExportadorClientes
{
    private const char Separador = ';';

    public static string Csv(IEnumerable<Cliente> clientes)
    {
        var csv = new StringBuilder();
        Linha(csv, ImportadorClientes.Colunas);
        foreach (var c in clientes.OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id))
            Linha(csv, c.DocumentoFormatado, c.Nome, c.RazaoSocial ?? "", c.Telefone ?? "", c.Email ?? "",
                Dinheiro.FormatarSimples(c.CreditoCentavos), Dinheiro.FormatarSimples(c.LimiteCreditoCentavos),
                c.Observacao ?? "", c.Ativo ? "Ativo" : "Inativo");
        return csv.ToString();
    }

    /// <summary>Grava em UTF-8 com BOM (o Excel mostra os acentos certos).</summary>
    public static void Salvar(string caminho, IEnumerable<Cliente> clientes) => ExportadorCsv.Salvar(caminho, Csv(clientes));

    private static void Linha(StringBuilder csv, params string[] campos) =>
        csv.Append(string.Join(Separador, campos.Select(Campo))).Append("\r\n");

    /// <summary>Aspas quando há separador, aspas ou quebra de linha; apóstrofo na frente de texto que começa com = + - @ (o Excel o leria como fórmula).</summary>
    private static string Campo(string? texto)
    {
        texto ??= "";
        if (texto.Length > 0 && texto[0] is '=' or '+' or '-' or '@') texto = "'" + texto;
        return texto.IndexOfAny(new[] { Separador, '"', '\r', '\n' }) >= 0 ? "\"" + texto.Replace("\"", "\"\"") + "\"" : texto;
    }
}
