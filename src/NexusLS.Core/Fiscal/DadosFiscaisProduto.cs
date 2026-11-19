using System.Globalization;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Fiscal;

/// <summary>
/// Dados fiscais do produto como o operador digita (texto). Vazio = usa o padrão (limpa o campo do produto).
/// Alíquotas em porcentagem com vírgula ou ponto ("18" ou "18,00"). Valores gravados no produto: alíquota em
/// centésimos de ponto percentual (18,00% = 1800), como o resto do sistema guarda dinheiro em centavos.
/// </summary>
public sealed record DadosFiscaisProduto(
    string? Cest = null,
    string? Origem = null,
    string? CstIcms = null,
    string? AliquotaIcms = null,
    string? CstPis = null,
    string? AliquotaPis = null,
    string? CstCofins = null,
    string? AliquotaCofins = null,
    string? Unidade = null,
    string? CstIbsCbs = null,
    string? ClassTribIbsCbs = null);

public static class RegrasFiscaisProduto
{
    public static readonly string[] Unidades = { "UN", "PC", "PAR", "CX", "KG", "G", "LT", "ML", "MT", "M2", "CJ", "DZ" };

    /// <summary>CSOSN (Simples Nacional, 3 números) ou CST do ICMS (regime normal, 2 números).</summary>
    public static readonly string[] CsosnConhecidos = { "101", "102", "103", "201", "202", "203", "300", "400", "500", "900" };

    public static readonly string[] OrigensTexto =
    {
        "0 - Nacional", "1 - Estrangeira (importação direta)", "2 - Estrangeira (mercado interno)", "3 - Nacional (importação 40% a 70%)",
        "4 - Nacional (processos básicos)", "5 - Nacional (importação até 40%)", "6 - Estrangeira (sem similar nacional)",
        "7 - Estrangeira (sem similar, mercado interno)", "8 - Nacional (importação acima de 70%)"
    };

    /// <summary>Valida o que foi digitado e grava (já normalizado) no produto.</summary>
    public static void Aplicar(Produto p, DadosFiscaisProduto d)
    {
        p.Cest = Digitos(d.Cest, 7, "O CEST tem 7 números.");
        p.OrigemMercadoria = Origem(d.Origem);
        p.CstIcms = CstIcms(d.CstIcms);
        p.AliquotaIcmsBp = Aliquota(d.AliquotaIcms, "ICMS");
        p.CstPis = Digitos(d.CstPis, 2, "O CST do PIS tem 2 números.");
        p.AliquotaPisBp = Aliquota(d.AliquotaPis, "PIS");
        p.CstCofins = Digitos(d.CstCofins, 2, "O CST da COFINS tem 2 números.");
        p.AliquotaCofinsBp = Aliquota(d.AliquotaCofins, "COFINS");
        p.UnidadeComercial = Unidade(d.Unidade);
        p.CstIbsCbs = Digitos(d.CstIbsCbs, 3, "O CST do IBS/CBS tem 3 números.");
        p.ClassTribIbsCbs = Digitos(d.ClassTribIbsCbs, 6, "O cClassTrib do IBS/CBS tem 6 números.");
        if ((p.CstIbsCbs is null) != (p.ClassTribIbsCbs is null))
            throw new RegraDeNegocioException("IBS/CBS: preencha o CST e o cClassTrib juntos (ou deixe os dois vazios).");
    }

    /// <summary>Texto dos campos a partir do produto, para a tela.</summary>
    public static DadosFiscaisProduto Ler(Produto p) => new(
        p.Cest, p.OrigemMercadoria.ToString(CultureInfo.InvariantCulture), p.CstIcms, TextoAliquota(p.AliquotaIcmsBp),
        p.CstPis, TextoAliquota(p.AliquotaPisBp), p.CstCofins, TextoAliquota(p.AliquotaCofinsBp),
        p.UnidadeComercial, p.CstIbsCbs, p.ClassTribIbsCbs);

    public static string TextoAliquota(int bp) =>
        bp <= 0 ? "" : (bp / 100m).ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"));

    private static string? Digitos(string? texto, int tamanho, string erro)
    {
        var t = (texto ?? "").Replace(".", "").Trim();
        if (t.Length == 0) return null;
        if (t.Length != tamanho || !t.All(char.IsAsciiDigit)) throw new RegraDeNegocioException(erro);
        return t;
    }

    private static int Origem(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return 0;
        if (t.Length >= 1 && char.IsAsciiDigit(t[0]) && t[0] <= '8' && (t.Length == 1 || !char.IsAsciiDigit(t[1]))) return t[0] - '0';
        throw new RegraDeNegocioException("A origem da mercadoria vai de 0 a 8.");
    }

    private static string? CstIcms(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return null;
        if ((t.Length == 2 || t.Length == 3) && t.All(char.IsAsciiDigit)) return t;
        throw new RegraDeNegocioException("O CSOSN tem 3 números (Simples Nacional) e o CST do ICMS tem 2 (regime normal).");
    }

    private static string? Unidade(string? texto)
    {
        var t = (texto ?? "").Trim().ToUpperInvariant();
        if (t.Length == 0) return null;
        if (t.Length > 6 || !t.All(char.IsAsciiLetterOrDigit)) throw new RegraDeNegocioException("A unidade comercial tem até 6 letras (ex.: UN, PAR, KG).");
        return t;
    }

    /// <summary>Alíquota em % → centésimos (18,5 vira 1850). Vazio = 0. Vai de 0 a 100.</summary>
    public static int Aliquota(string? texto, string tributo)
    {
        var t = (texto ?? "").Replace("%", "").Trim().Replace(',', '.');
        if (t.Length == 0) return 0;
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 100)
            throw new RegraDeNegocioException($"A alíquota de {tributo} deve ficar entre 0 e 100%.");
        return (int)Math.Round(v * 100m, MidpointRounding.AwayFromZero);
    }
}
