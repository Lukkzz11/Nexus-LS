using System.Globalization;
using System.Text;
using System.Xml.Linq;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Fiscal;

/// <summary>Dados do emitente (a loja), vindos da página Loja.</summary>
public sealed record EmitenteNfce(
    string? Documento, string? InscricaoEstadual, string? RazaoSocial, string? NomeFantasia,
    string? Logradouro, string? Numero, string? Bairro, string? Cep, string? Municipio, string? Uf,
    string? CodigoMunicipioIbge)
{
    /// <summary>Lê os campos da loja. Cidade e UF vêm juntas em "Cidade - UF" (ou "Cidade/UF"). O código IBGE do município não existe no projeto: fica nulo.</summary>
    public static EmitenteNfce De(ConfiguracaoLoja loja)
    {
        var (cidade, uf) = SepararCidadeUf(loja.CidadeUf);
        return new EmitenteNfce(loja.Documento, loja.InscricaoEstadual, loja.RazaoSocial, loja.NomeLoja,
            loja.Endereco, loja.EnderecoNumero, loja.Bairro, loja.Cep, cidade, uf, null);
    }

    public static (string? Cidade, string? Uf) SepararCidadeUf(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return (null, null);
        var t = texto.Trim();
        int i = t.LastIndexOfAny(new[] { '-', '/', ',' });
        if (i < 0) return (t, null);
        var uf = t[(i + 1)..].Trim().ToUpperInvariant();
        var cidade = t[..i].Trim();
        return uf.Length == 2 ? (cidade.Length == 0 ? null : cidade, uf) : (t, null);
    }
}

/// <summary>Configuração fiscal (página Fiscal). Nada de certificado nem de código secreto do CSC.</summary>
public sealed record ConfigNfce(string? Regime, string Ambiente, int Serie, string? CscId, string? NcmPadrao, string? CfopPadrao);

/// <summary>
/// Resultado da montagem. <see cref="Xml"/> é nulo quando a venda não pode virar nota (<see cref="Bloqueio"/>).
/// <see cref="Pendencias"/> lista o que falta nos cadastros; <see cref="Avisos"/> lista as etapas que ainda não existem (assinatura, QR Code, envio).
/// </summary>
public sealed record ResultadoNfce(string? Xml, string? Chave, int Numero, string? Bloqueio, IReadOnlyList<string> Pendencias, IReadOnlyList<string> Avisos)
{
    public bool PodeGerarXml => Xml is not null;
    /// <summary>Cadastros completos? (Não quer dizer que dá para emitir: veja os avisos.)</summary>
    public bool CadastrosCompletos => Bloqueio is null && Pendencias.Count == 0;
}

/// <summary>
/// Monta o XML de CONFERÊNCIA de uma NFC-e (modelo 65, layout 4.00) a partir de uma venda já gravada. Não assina, não transmite, não lê
/// certificado nem CSC, não usa rede. O arquivo gerado não tem validade fiscal.
/// </summary>
public static class MontadorNfce
{
    public const string AvisoNaoAssinado = "SEM VALIDADE FISCAL — conferência. XML NÃO ASSINADO e NÃO transmitido à SEFAZ.";
    public const string TextoHomologacao = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Código da forma de pagamento (tPag) da NT: 01 dinheiro, 02 cheque, 03 crédito, 04 débito, 05 crédito loja (crediário), 17 Pix, 99 outros.</summary>
    public static string CodigoPagamento(FormaPagamento forma) => forma switch
    {
        FormaPagamento.Dinheiro => "01",
        FormaPagamento.Cheque => "02",
        FormaPagamento.CartaoCredito => "03",
        FormaPagamento.CartaoDebito => "04",
        FormaPagamento.APrazo => "05",
        FormaPagamento.Pix => "17",
        _ => "99",
    };

    /// <summary>Número da nota = posição da venda entre as vendas não canceladas (ordenadas pelo Id), começando em 1: sem pular números e sem coluna nova.</summary>
    public static int NumeroSequencial(IEnumerable<int> idsVendasValidas, int vendaId)
    {
        var ordenados = idsVendasValidas.Distinct().OrderBy(i => i).ToList();
        int pos = ordenados.IndexOf(vendaId);
        return pos < 0 ? ordenados.Count(i => i < vendaId) + 1 : pos + 1;
    }

    /// <summary>Divide um valor entre os itens na proporção de cada um; a sobra de centavos fica no último item, e a soma fecha sempre.</summary>
    public static long[] Ratear(long total, IReadOnlyList<long> bases)
    {
        var r = new long[bases.Count];
        long soma = bases.Sum();
        if (total == 0 || soma <= 0 || r.Length == 0) return r;
        long dado = 0;
        for (int i = 0; i < r.Length - 1; i++)
        {
            r[i] = (long)((decimal)total * bases[i] / soma);
            dado += r[i];
        }
        r[^1] = total - dado;
        return r;
    }

    /// <param name="idsVendasValidas">Ids de todas as vendas não canceladas (para numerar sem pular).</param>
    /// <param name="produtos">Produtos da venda por Id (para NCM, CFOP e código). Produto apagado = só o padrão.</param>
    public static ResultadoNfce Montar(Venda? venda, EmitenteNfce emitente, ConfigNfce config,
        IReadOnlyDictionary<int, Produto> produtos, IEnumerable<int> idsVendasValidas)
    {
        if (venda is null) return Bloqueada("Não há venda para conferir.");
        if (venda.Cancelada) return Bloqueada($"A venda {venda.Id} foi cancelada: venda cancelada não vira nota.");
        if (venda.Itens.Count == 0) return Bloqueada($"A venda {venda.Id} não tem itens.");

        var pend = new List<string>();
        var avisos = new List<string>();

        // Emitente
        var doc = ValidadorDocumento.Validar(emitente.Documento);
        var cnpj = doc.Digitos;
        if (!(doc.Valido && doc.Tipo == TipoDocumento.Cnpj)) pend.Add("CNPJ da loja inválido ou ausente (página Loja).");
        var ie = SoDigitosOuIsento(emitente.InscricaoEstadual);
        if (ie is null) pend.Add("Inscrição estadual da loja ausente (página Loja). Use só números ou ISENTO.");
        if (string.IsNullOrWhiteSpace(emitente.RazaoSocial)) pend.Add("Razão social da loja ausente (página Loja).");
        if (string.IsNullOrWhiteSpace(emitente.Logradouro)) pend.Add("Endereço (rua) da loja ausente (página Loja).");
        if (string.IsNullOrWhiteSpace(emitente.Bairro)) pend.Add("Bairro da loja ausente (página Loja).");
        var cep = ValidadorDocumento.SoDigitos(emitente.Cep);
        if (cep.Length != 8) pend.Add("CEP da loja ausente ou incompleto (página Loja).");
        if (string.IsNullOrWhiteSpace(emitente.Municipio)) pend.Add("Cidade da loja ausente (página Loja, campo Cidade/UF).");
        var uf = emitente.Uf?.Trim().ToUpperInvariant();
        int codUf = 0;
        if (uf is null || !ChaveAcessoNfce.CodigoUf.TryGetValue(uf, out codUf)) pend.Add("UF da loja ausente ou desconhecida (página Loja, campo Cidade/UF, ex.: Caruaru - PE).");
        var ibge = ValidadorDocumento.SoDigitos(emitente.CodigoMunicipioIbge);
        bool ibgeOk = ibge.Length == 7 && (codUf == 0 || ibge.StartsWith(codUf.ToString(Inv), StringComparison.Ordinal));
        if (!ibgeOk)
            pend.Add($"Código IBGE do município{(string.IsNullOrWhiteSpace(emitente.Municipio) ? "" : " de " + emitente.Municipio)} (7 números) não está disponível no Nexus: não foi inventado e precisa ser informado antes da emissão.");

        // Configuração fiscal
        string? crt = config.Regime switch { "SimplesNacional" => "1", "Mei" => "4", "LucroPresumidoReal" => "3", _ => null };
        if (crt is null) pend.Add("Regime tributário não escolhido (página Fiscal).");
        if (config.Serie is < 1 or > 999) pend.Add("Série da nota inválida (página Fiscal, 1 a 999).");
        if (string.IsNullOrWhiteSpace(config.CscId)) pend.Add("ID do CSC não informado (página Fiscal).");
        if (crt == "3") pend.Add("Regime normal (lucro presumido/real): a tributação (CST, alíquotas de ICMS/PIS/COFINS) ainda não é montada pelo Nexus.");
        bool producao = config.Ambiente == PreparoFiscal.Producao;

        // Itens
        var cfopPadrao = string.IsNullOrWhiteSpace(config.CfopPadrao) ? "5102" : config.CfopPadrao.Trim();
        var bases = venda.Itens.Select(i => i.TotalCentavos).ToList();
        var descontos = Ratear(venda.DescontoCentavos, bases);
        var outros = Ratear(venda.AcrescimoCentavos, bases);
        long somaItens = bases.Sum();
        if (somaItens != venda.SubtotalCentavos)
            pend.Add($"A soma dos itens ({Real(somaItens)}) não bate com o subtotal da venda ({Real(venda.SubtotalCentavos)}).");
        long vNf = somaItens - venda.DescontoCentavos + venda.AcrescimoCentavos;
        if (vNf != venda.TotalCentavos)
            pend.Add($"O total calculado da nota ({Real(vNf)}) não bate com o total da venda ({Real(venda.TotalCentavos)}).");

        var dets = new List<XElement>();
        var ibsCbsInformado = false;
        int n = 0;
        foreach (var item in venda.Itens)
        {
            n++;
            produtos.TryGetValue(item.ProdutoId, out var prod);
            var ncm = ValidadorDocumento.SoDigitos(!string.IsNullOrWhiteSpace(prod?.Ncm) ? prod!.Ncm : config.NcmPadrao);
            var nome = item.NomeProduto;
            if (ncm.Length != 8) pend.Add($"NCM ausente ou inválido no item {n} ({nome}): preencha no produto ou o NCM padrão na página Fiscal.");
            var cfop = ValidadorDocumento.SoDigitos(!string.IsNullOrWhiteSpace(prod?.Cfop) ? prod!.Cfop : cfopPadrao);
            if (cfop.Length != 4) pend.Add($"CFOP inválido no item {n} ({nome}): precisa ter 4 números.");
            if (item.Quantidade <= 0 || item.PrecoUnitarioCentavos <= 0) pend.Add($"Item {n} ({nome}) com quantidade ou preço zerado.");
            if (item.PrecoUnitarioCentavos * item.Quantidade != item.TotalCentavos) pend.Add($"Item {n} ({nome}): quantidade x preço não bate com o total do item.");

            var cod = prod?.CodigoInterno ?? prod?.Referencia ?? item.ProdutoId.ToString(Inv);
            var xProd = item.Tamanho == Tamanhos.SemTamanho ? nome : $"{nome} {item.Tamanho}";
            if (!producao && n == 1) xProd = TextoHomologacao;
            var gtin = GtinValido(prod?.CodigoBarras) ? prod!.CodigoBarras!.Trim() : "SEM GTIN";

            var prodEl = new XElement(Ns + "prod",
                E("cProd", Limpa(cod)), E("cEAN", gtin), E("xProd", Limpa(xProd)));
            if (ncm.Length == 8) prodEl.Add(E("NCM", ncm));
            var cest = ValidadorDocumento.SoDigitos(prod?.Cest);
            if (cest.Length == 7) prodEl.Add(E("CEST", cest));
            var un = string.IsNullOrWhiteSpace(prod?.UnidadeComercial) ? "UN" : prod!.UnidadeComercial!.Trim();
            prodEl.Add(E("CFOP", cfop),
                E("uCom", un), E("qCom", Dec4(item.Quantidade)), E("vUnCom", Dec4(item.PrecoUnitarioCentavos / 100m)), E("vProd", Dec2(item.TotalCentavos)),
                E("cEANTrib", gtin), E("uTrib", un), E("qTrib", Dec4(item.Quantidade)), E("vUnTrib", Dec4(item.PrecoUnitarioCentavos / 100m)));
            if (descontos[n - 1] > 0) prodEl.Add(E("vDesc", Dec2(descontos[n - 1])));
            if (outros[n - 1] > 0) prodEl.Add(E("vOutro", Dec2(outros[n - 1])));
            prodEl.Add(E("indTot", "1"));

            var imposto = new XElement(Ns + "imposto");
            var orig = (prod?.OrigemMercadoria ?? 0).ToString(Inv);
            if (crt is "1" or "4")
            {
                var csosn = string.IsNullOrWhiteSpace(prod?.CstIcms) ? "102" : prod!.CstIcms!;
                switch (csosn)
                {
                    case "102" or "103" or "300" or "400":
                        imposto.Add(new XElement(Ns + "ICMS", new XElement(Ns + "ICMSSN102", E("orig", orig), E("CSOSN", csosn))));
                        break;
                    case "101":
                        var pCred = prod?.AliquotaIcmsBp ?? 0;
                        imposto.Add(new XElement(Ns + "ICMS", new XElement(Ns + "ICMSSN101", E("orig", orig), E("CSOSN", "101"),
                            E("pCredSN", Dec2(pCred)), E("vCredICMSSN", Dec2(item.TotalCentavos * pCred / 10000)))));
                        break;
                    case "500":
                        imposto.Add(new XElement(Ns + "ICMS", new XElement(Ns + "ICMSSN500", E("orig", orig), E("CSOSN", "500"))));
                        break;
                    default:
                        pend.Add($"CSOSN {csosn} do item {n} ({nome}) ainda não é montado pelo Nexus: use 101, 102, 103, 300, 400 ou 500, ou confirme com o contador.");
                        break;
                }
            }
            imposto.Add(new XElement(Ns + "PIS", MontarPisCofins("PIS", prod?.CstPis, prod?.AliquotaPisBp ?? 0, item.TotalCentavos, n, nome, pend)),
                new XElement(Ns + "COFINS", MontarPisCofins("COFINS", prod?.CstCofins, prod?.AliquotaCofinsBp ?? 0, item.TotalCentavos, n, nome, pend)));
            if (!string.IsNullOrWhiteSpace(prod?.CstIbsCbs) && !string.IsNullOrWhiteSpace(prod?.ClassTribIbsCbs))
            {
                imposto.Add(new XElement(Ns + "IBSCBS", E("CST", prod!.CstIbsCbs), E("cClassTrib", prod.ClassTribIbsCbs)));
                ibsCbsInformado = true;
            }
            dets.Add(new XElement(Ns + "det", new XAttribute("nItem", n), prodEl, imposto));
        }

        // Número e chave
        int numero = NumeroSequencial(idsVendasValidas, venda.Id);
        int cNf = (int)((venda.Id * 7919L + 12_345_678L) % 100_000_000L);
        if (cNf == numero) cNf = (cNf + 1) % 100_000_000;
        string? chave = null;
        int serie = config.Serie is >= 1 and <= 999 ? config.Serie : 1;
        if (cnpj.Length == 14 && codUf != 0)
            chave = ChaveAcessoNfce.Montar(codUf, venda.DataHora, cnpj, 65, serie, numero, 1, cNf);
        else pend.Add("A chave de acesso não pôde ser calculada (precisa de CNPJ e UF da loja).");

        var offset = TimeZoneInfo.Local.GetUtcOffset(venda.DataHora);
        var dhEmi = new DateTimeOffset(DateTime.SpecifyKind(venda.DataHora, DateTimeKind.Unspecified), offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv);

        var ide = new XElement(Ns + "ide",
            E("cUF", codUf == 0 ? "" : codUf.ToString(Inv)), E("cNF", cNf.ToString("00000000", Inv)), E("natOp", "VENDA"), E("mod", "65"),
            E("serie", serie.ToString(Inv)), E("nNF", numero.ToString(Inv)), E("dhEmi", dhEmi), E("tpNF", "1"), E("idDest", "1"));
        if (ibgeOk) ide.Add(E("cMunFG", ibge));
        ide.Add(E("tpImp", "4"), E("tpEmis", "1"), E("cDV", chave is null ? "" : chave[43].ToString()), E("tpAmb", producao ? "1" : "2"),
            E("finNFe", "1"), E("indFinal", "1"), E("indPres", "1"), E("procEmi", "0"), E("verProc", "NexusLS-conferencia"));

        var ender = new XElement(Ns + "enderEmit", E("xLgr", Limpa(emitente.Logradouro)), E("nro", string.IsNullOrWhiteSpace(emitente.Numero) ? "S/N" : Limpa(emitente.Numero)),
            E("xBairro", Limpa(emitente.Bairro)));
        if (ibgeOk) ender.Add(E("cMun", ibge));
        ender.Add(E("xMun", Limpa(emitente.Municipio)), E("UF", uf ?? ""), E("CEP", cep));
        var emit = new XElement(Ns + "emit", E("CNPJ", cnpj), E("xNome", Limpa(emitente.RazaoSocial)));
        if (!string.IsNullOrWhiteSpace(emitente.NomeFantasia)) emit.Add(E("xFant", Limpa(emitente.NomeFantasia)));
        emit.Add(ender, E("IE", ie ?? ""), E("CRT", crt ?? ""));

        var total = new XElement(Ns + "total", new XElement(Ns + "ICMSTot",
            E("vBC", "0.00"), E("vICMS", "0.00"), E("vICMSDeson", "0.00"), E("vFCP", "0.00"), E("vBCST", "0.00"), E("vST", "0.00"),
            E("vFCPST", "0.00"), E("vFCPSTRet", "0.00"), E("vProd", Dec2(somaItens)), E("vFrete", "0.00"), E("vSeg", "0.00"),
            E("vDesc", Dec2(venda.DescontoCentavos)), E("vII", "0.00"), E("vIPI", "0.00"), E("vIPIDevol", "0.00"),
            E("vPIS", "0.00"), E("vCOFINS", "0.00"), E("vOutro", Dec2(venda.AcrescimoCentavos)), E("vNF", Dec2(vNf))));

        var pag = new XElement(Ns + "pag");
        long pago = venda.TotalCentavos - venda.ValeTrocaCentavos;
        if (pago > 0)
        {
            long vPag = pago;
            if (venda.FormaPagamento == FormaPagamento.Dinheiro && venda.ValorRecebidoCentavos is > 0) vPag = venda.ValorRecebidoCentavos.Value;
            var det = new XElement(Ns + "detPag", E("indPag", venda.FormaPagamento == FormaPagamento.APrazo ? "1" : "0"),
                E("tPag", CodigoPagamento(venda.FormaPagamento)), E("vPag", Dec2(vPag)));
            if (venda.FormaPagamento is FormaPagamento.CartaoCredito or FormaPagamento.CartaoDebito)
                det.Add(new XElement(Ns + "card", E("tpIntegra", "2")));
            pag.Add(det);
        }
        if (venda.ValeTrocaCentavos > 0)
            pag.Add(new XElement(Ns + "detPag", E("indPag", "0"), E("tPag", "99"), E("vPag", Dec2(venda.ValeTrocaCentavos)), E("xPag", "Vale-troca")));
        if (venda.TrocoCentavos > 0) pag.Add(E("vTroco", Dec2(venda.TrocoCentavos)));

        var infNfe = new XElement(Ns + "infNFe", new XAttribute("versao", "4.00"));
        if (chave is not null) infNfe.Add(new XAttribute("Id", "NFe" + chave));
        infNfe.Add(ide, emit, dets, total, new XElement(Ns + "transp", E("modFrete", "9")), pag);

        avisos.Add("XML NÃO ASSINADO: falta o certificado digital (A1) e a assinatura do XML (nó Signature ausente).");
        avisos.Add("Sem QR Code e sem URL de consulta: dependem do código secreto do CSC (que o Nexus não guarda) e da SEFAZ da UF.");
        avisos.Add("Nada foi enviado à SEFAZ: falta a etapa de transmissão (autorização), o protocolo e o DANFE NFC-e.");
        avisos.Add("A tributação (ICMS CSOSN 102, PIS/COFINS CST 07) é provisória: confirme com o contador antes de emitir.");
        if (ibsCbsInformado) avisos.Add("IBS/CBS: o XML leva só o CST e o cClassTrib dos produtos classificados; bases, alíquotas e valores (grupo gIBSCBS da NT 2025.002) ainda não são calculados pelo Nexus.");
        if (producao) avisos.Add("O ambiente está em Produção, mas este arquivo é só de conferência.");
        if (venda.Parcelas > 1 && venda.FormaPagamento == FormaPagamento.CartaoCredito) avisos.Add("Venda parcelada no cartão: a nota informa só o valor total; confirme o tratamento do acréscimo com o contador.");

        var nfe = new XElement(Ns + "NFe", infNfe);           // sem o nó Signature: não assinado
        var documento = new XDocument(new XComment($" {AvisoNaoAssinado} "), nfe);
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n").Append(documento.ToString()).ToString();
        return new ResultadoNfce(xml, chave, numero, null, pend, avisos);
    }

    /// <summary>PIS/COFINS do item: CST 01/02 com alíquota (PISAliq), CST de não incidência 04 a 09 (PISNT) ou o padrão provisório 07.</summary>
    private static XElement MontarPisCofins(string tributo, string? cst, int aliquotaBp, long baseCentavos, int n, string nome, List<string> pend)
    {
        var c = string.IsNullOrWhiteSpace(cst) ? "07" : cst!;
        if (c is "01" or "02")
        {
            if (aliquotaBp <= 0) pend.Add($"{tributo} CST {c} do item {n} ({nome}) precisa da alíquota.");
            return new XElement(Ns + tributo + "Aliq", E("CST", c), E("vBC", Dec2(baseCentavos)), E(tributo == "PIS" ? "pPIS" : "pCOFINS", Dec2(aliquotaBp)),
                E(tributo == "PIS" ? "vPIS" : "vCOFINS", Dec2(baseCentavos * aliquotaBp / 10000)));
        }
        if (c is "04" or "05" or "06" or "07" or "08" or "09") return new XElement(Ns + tributo + "NT", E("CST", c));
        pend.Add($"{tributo} CST {c} do item {n} ({nome}) ainda não é montado pelo Nexus: use 01, 02 ou 04 a 09, ou confirme com o contador.");
        return new XElement(Ns + tributo + "NT", E("CST", "07"));
    }

    private static ResultadoNfce Bloqueada(string motivo) => new(null, null, 0, motivo, Array.Empty<string>(), Array.Empty<string>());

    private static XElement E(string nome, string? valor) => new(Ns + nome, valor ?? "");

    private static string Real(long c) => "R$ " + (c / 100m).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
    private static string Dec2(long centavos) => (centavos / 100m).ToString("0.00", Inv);
    private static string Dec4(decimal v) => v.ToString("0.0000", Inv);

    /// <summary>Tira caracteres que o XML não aceita (controle) e espaços sobrando. As aspas, &lt; e &amp; são escapados pelo próprio XML.</summary>
    public static string Limpa(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (c is '\t' or '\n' or '\r') sb.Append(' ');
            else if (!char.IsControl(c) && !(c >= '￾') && !char.IsSurrogate(c)) sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static string? SoDigitosOuIsento(string? ie)
    {
        if (string.IsNullOrWhiteSpace(ie)) return null;
        if (ie.Trim().Equals("ISENTO", StringComparison.OrdinalIgnoreCase)) return "ISENTO";
        var d = ValidadorDocumento.SoDigitos(ie);
        return d.Length is >= 2 and <= 14 ? d : null;
    }

    /// <summary>GTIN-8/12/13/14 com dígito verificador certo. Código de barras interno ou de outro tamanho vira "SEM GTIN".</summary>
    public static bool GtinValido(string? codigo)
    {
        var c = codigo?.Trim();
        if (c is null || !(c.Length is 8 or 12 or 13 or 14) || !c.All(char.IsAsciiDigit)) return false;
        int soma = 0;
        for (int i = c.Length - 2, peso = 3; i >= 0; i--, peso = peso == 3 ? 1 : 3) soma += (c[i] - '0') * peso;
        return (10 - soma % 10) % 10 == c[^1] - '0';
    }
}
