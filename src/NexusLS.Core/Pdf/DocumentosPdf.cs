using System.Globalization;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Pdf;

/// <summary>Os documentos do Nexus em PDF, todos no mesmo padrão (cabeçalho da loja, tabelas, totais, assinatura, rodapé).</summary>
public static class DocumentosPdf
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Converte o logo da loja (PNG guardado nas configurações) em JPEG para entrar no PDF. Quem sabe decodificar imagem
    /// (o programa, com o WPF) preenche este gancho ao abrir; sem ele, o PDF sai só com o nome da loja.
    /// </summary>
    public static Func<byte[], byte[]?>? ConversorLogo { get; set; }

    /// <summary>Avisa o programa que um PDF foi gerado para o usuário (ele guarda uma cópia na gaveta). Nunca falha.</summary>
    public static void Registrar(string nomeArquivo, byte[] bytes)
    {
        try { CsvParaPdf.AoGerar?.Invoke(nomeArquivo, bytes); }
        catch (Exception) { }
    }

    public const string DeclaracaoRecebimento =
        "Declaro que conferi e recebi as mercadorias descritas neste comprovante, nas quantidades e valores acima.";

    /// <summary>Dados da loja para o cabeçalho (nome, CNPJ, endereço e contato). O logo é opcional (JPEG).</summary>
    public static EmpresaPdf Empresa(ConfiguracaoLoja loja, byte[]? logoJpeg = null)
    {
        var nome = !string.IsNullOrWhiteSpace(loja.RazaoSocial) ? loja.RazaoSocial!.Trim() : loja.NomeLoja;
        if (logoJpeg is null && loja.CupomMostrarLogo && loja.LogoCupom is { Length: > 0 } png)
        {
            try { logoJpeg = ConversorLogo?.Invoke(png); }
            catch (Exception) { logoJpeg = null; } // logo que não abre: o PDF sai sem ele
        }
        var linha1 = string.Join("  ·  ", new[]
        {
            string.IsNullOrWhiteSpace(loja.Documento) ? null : "CNPJ/CPF " + loja.Documento!.Trim(),
            string.IsNullOrWhiteSpace(loja.InscricaoEstadual) ? null : "IE " + loja.InscricaoEstadual!.Trim()
        }.Where(s => s is not null));
        var endereco = string.Join(", ", new[] { loja.Endereco, loja.EnderecoNumero, loja.Bairro, loja.CidadeUf }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        var contato = string.Join("  ·  ", new[] { loja.Telefone, loja.Email }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        var linha2 = string.Join("  ·  ", new[] { endereco, contato }.Where(s => s.Length > 0));
        return new EmpresaPdf(nome, linha1.Length == 0 ? null : linha1, linha2.Length == 0 ? null : linha2, logoJpeg);
    }

    // ------------------------------------------------------------------ comprovante de venda

    public static string NomeComprovante(Venda venda) => $"comprovante-venda-{venda.Id}.pdf";

    /// <summary>Comprovante de venda (cupom em PDF). Com <paramref name="pedirAssinatura"/>, sai o campo de assinatura do cliente.</summary>
    public static byte[] ComprovanteVenda(Venda venda, ConfiguracaoLoja loja, byte[]? logoJpeg = null, bool pedirAssinatura = true,
        IReadOnlyDictionary<int, string>? referencias = null)
    {
        var doc = new PdfDocumento($"Comprovante de venda nº {venda.Id}", Empresa(loja, logoJpeg), venda.DataHora,
            "Documento sem valor fiscal");

        var campos = new List<(string, string)>
        {
            ("DATA E HORA", venda.DataHora.ToString("dd/MM/yyyy HH:mm", Br)),
            ("OPERADOR", string.IsNullOrWhiteSpace(venda.Operador) ? "—" : venda.Operador!)
        };
        if (venda.Cliente is { } c)
        {
            campos.Add(("CLIENTE", c.Nome));
            campos.Add(("CPF/CNPJ", string.IsNullOrWhiteSpace(c.Documento) ? "—" : c.Documento!));
            if (!string.IsNullOrWhiteSpace(c.Telefone)) campos.Add(("TELEFONE", c.Telefone!));
        }
        else campos.Add(("CLIENTE", "Consumidor final"));
        doc.Campos(campos);
        if (venda.Cancelada) doc.Paragrafo("VENDA CANCELADA" + (string.IsNullOrWhiteSpace(venda.MotivoCancelamento) ? "" : " — " + venda.MotivoCancelamento), true, 11);

        doc.Secao("Itens");
        var colunas = new[]
        {
            new ColunaPdf("Qtd", 0.7, AlinhamentoPdf.Centro), new ColunaPdf("Descrição", 4.6),
            new ColunaPdf("Unitário", 1.4, AlinhamentoPdf.Direita), new ColunaPdf("Total", 1.4, AlinhamentoPdf.Direita)
        };
        var linhas = venda.Itens.Select(i =>
        {
            var nome = i.NomeProduto + (i.Tamanho == Tamanhos.SemTamanho ? "" : $" — {i.Tamanho}");
            var referencia = i.Referencia ?? (referencias is not null && referencias.TryGetValue(i.ProdutoId, out var r) ? r : null);
            if (!string.IsNullOrWhiteSpace(referencia)) nome += $" (ref. {referencia})";
            return new[] { i.Quantidade.ToString(Br), nome, Dinheiro.Formatar(i.PrecoUnitarioCentavos), Dinheiro.Formatar(i.TotalCentavos) };
        });
        doc.Tabela(colunas, linhas);

        var resumo = new List<(string, string)> { ("Subtotal", Dinheiro.Formatar(venda.SubtotalCentavos)) };
        if (venda.DescontoCentavos > 0) resumo.Add(("Desconto", "- " + Dinheiro.Formatar(venda.DescontoCentavos)));
        if (venda.AcrescimoCentavos > 0) resumo.Add(("Acréscimo", Dinheiro.Formatar(venda.AcrescimoCentavos)));
        if (venda.ValeTrocaCentavos > 0) resumo.Add(("Vale-troca usado", "- " + Dinheiro.Formatar(venda.ValeTrocaCentavos)));
        resumo.Add(("TOTAL", Dinheiro.Formatar(venda.TotalCentavos)));
        doc.Resumo(resumo);

        var forma = SelecaoPagamento.Nome(venda.FormaPagamento);
        if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1) forma += $" em {venda.Parcelas}x";
        var pagamento = new List<(string, string)> { ("FORMA DE PAGAMENTO", forma) };
        if (venda.FormaPagamento == FormaPagamento.Dinheiro && venda.ValorRecebidoCentavos is > 0)
        {
            pagamento.Add(("VALOR RECEBIDO", Dinheiro.Formatar(venda.ValorRecebidoCentavos.Value)));
            pagamento.Add(("TROCO", Dinheiro.Formatar(venda.TrocoCentavos)));
        }
        doc.Campos(pagamento, 3);

        doc.Espaco(8);
        if (!string.IsNullOrWhiteSpace(loja.MensagemCupom)) doc.Paragrafo(loja.MensagemCupom!, alinhamento: AlinhamentoPdf.Centro);
        if (loja.CupomMostrarPix && !string.IsNullOrWhiteSpace(loja.ChavePix)) doc.Paragrafo("Chave Pix da loja: " + loja.ChavePix, alinhamento: AlinhamentoPdf.Centro);
        if (pedirAssinatura && !venda.Cancelada) doc.Assinaturas(new[] { "Assinatura do cliente", "Loja" }, DeclaracaoRecebimento);
        return doc.Gerar();
    }

    // ------------------------------------------------------------------ relatório genérico

    /// <summary>
    /// Relatório em tabela no padrão do Nexus. <paramref name="resumo"/> (opcional) vira o quadro de totais;
    /// <paramref name="assinatura"/> pede "Responsável" e "Conferido por" no fim.
    /// </summary>
    public static byte[] Relatorio(string titulo, string? periodo, ConfiguracaoLoja loja, IReadOnlyList<ColunaPdf> colunas,
        IEnumerable<string[]> linhas, string[]? total = null, IReadOnlyList<(string, string)>? resumo = null,
        bool assinatura = true, byte[]? logoJpeg = null, DateTime? emitidoEm = null)
    {
        var doc = new PdfDocumento(titulo, Empresa(loja, logoJpeg), emitidoEm, periodo);
        if (resumo is { Count: > 0 }) doc.Secao("Resumo").Campos(resumo, 3);
        doc.Secao("Detalhamento").Tabela(colunas, linhas, total);
        if (assinatura) doc.Assinaturas(new[] { "Responsável", "Conferido por" });
        return doc.Gerar();
    }

    // ------------------------------------------------------------------ relatório do dia

    public static byte[] RelatorioDia(IEnumerable<Venda> vendas, DateTime dia, ConfiguracaoLoja loja, byte[]? logoJpeg = null, DateTime? emitidoEm = null)
    {
        var lista = vendas.ToList();
        var validas = lista.Where(v => !v.Cancelada).ToList();
        var total = validas.Sum(v => v.TotalCentavos);
        var pecas = validas.Sum(v => v.Itens.Sum(i => i.Quantidade));

        var doc = new PdfDocumento("Relatório do dia", Empresa(loja, logoJpeg), emitidoEm, dia.ToString("dd/MM/yyyy", Br));
        doc.Secao("Resumo").Campos(new (string, string)[]
        {
            ("VENDAS", validas.Count.ToString(Br)), ("PEÇAS VENDIDAS", pecas.ToString(Br)), ("TOTAL VENDIDO", Dinheiro.Formatar(total)),
            ("TICKET MÉDIO", Dinheiro.Formatar(validas.Count == 0 ? 0 : total / validas.Count)),
            ("DESCONTOS", Dinheiro.Formatar(validas.Sum(v => v.DescontoCentavos))),
            ("CANCELADAS (NÃO SOMADAS)", (lista.Count - validas.Count).ToString(Br))
        }, 3);

        doc.Secao("Por forma de pagamento").Tabela(
            new[] { new ColunaPdf("Forma", 4), new ColunaPdf("Vendas", 1, AlinhamentoPdf.Centro), new ColunaPdf("Total", 2, AlinhamentoPdf.Direita) },
            RelatorioService.ResumoPorForma(lista).Select(f => new[] { SelecaoPagamento.Nome(f.Forma), f.Vendas.ToString(Br), Dinheiro.Formatar(f.TotalCentavos) }),
            new[] { "Total", validas.Count.ToString(Br), Dinheiro.Formatar(total) });

        doc.Secao("Vendas do dia").Tabela(
            new[]
            {
                new ColunaPdf("Nº", 0.8), new ColunaPdf("Hora", 1), new ColunaPdf("Operador", 2.4), new ColunaPdf("Pagamento", 2.2),
                new ColunaPdf("Total", 1.6, AlinhamentoPdf.Direita)
            },
            lista.OrderBy(v => v.DataHora).ThenBy(v => v.Id).Select(v =>
            {
                var forma = SelecaoPagamento.Nome(v.FormaPagamento);
                if (v.FormaPagamento == FormaPagamento.CartaoCredito && v.Parcelas > 1) forma += $" {v.Parcelas}x";
                return new[] { "#" + v.Id, v.DataHora.ToString("HH:mm", Br), v.Operador ?? "", forma + (v.Cancelada ? " (CANCELADA)" : ""),
                    Dinheiro.Formatar(v.TotalCentavos) };
            }));

        doc.Assinaturas(new[] { "Responsável pelo caixa", "Conferido por" });
        return doc.Gerar();
    }

    // ------------------------------------------------------------------ relatório do caixa por período

    public static byte[] RelatorioCaixa(RelatorioCaixaPeriodo r, ConfiguracaoLoja loja, byte[]? logoJpeg = null)
    {
        var periodo = r.De == r.Ate ? r.De.ToString("dd/MM/yyyy", Br) : $"{r.De:dd/MM/yyyy} a {r.Ate:dd/MM/yyyy}";
        var doc = new PdfDocumento("Relatório do caixa", Empresa(loja, logoJpeg), r.EmitidoEm, periodo);
        doc.Secao("Resumo").Campos(new (string, string)[]
        {
            ("VENDAS", r.VendasQuantidade.ToString(Br)), ("TOTAL VENDIDO", Dinheiro.Formatar(r.VendasCentavos)),
            ("SANGRIAS", Dinheiro.Formatar(r.SangriasCentavos)), ("REFORÇOS", Dinheiro.Formatar(r.ReforcosCentavos)),
            ("DESPESAS", Dinheiro.Formatar(r.DespesasCentavos)), ("ENTRADAS AVULSAS", Dinheiro.Formatar(r.EntradasAvulsasCentavos)),
            ("DIFERENÇA NA CONFERÊNCIA", Dinheiro.Formatar(r.DiferencaTotalCentavos)), ("CANCELADAS (NÃO SOMADAS)", r.VendasCanceladasIgnoradas.ToString(Br))
        }, 3);

        doc.Secao("Vendas por forma de pagamento").Tabela(
            new[] { new ColunaPdf("Forma", 4), new ColunaPdf("Vendas", 1, AlinhamentoPdf.Centro), new ColunaPdf("Total", 2, AlinhamentoPdf.Direita) },
            r.VendasPorForma.Select(f => new[] { SelecaoPagamento.Nome(f.Forma), f.Vendas.ToString(Br), Dinheiro.Formatar(f.TotalCentavos) }),
            new[] { "Total", r.VendasQuantidade.ToString(Br), Dinheiro.Formatar(r.VendasCentavos) });

        doc.Secao("Turnos de caixa").Tabela(
            new[]
            {
                new ColunaPdf("Abertura", 2), new ColunaPdf("Fechamento", 2), new ColunaPdf("Operador", 1.6),
                new ColunaPdf("Vendas", 0.9, AlinhamentoPdf.Centro), new ColunaPdf("Total", 1.5, AlinhamentoPdf.Direita)
            },
            r.Turnos.Select(t => new[]
            {
                t.Abertura.ToString("dd/MM HH:mm", Br), t.Fechamento?.ToString("dd/MM HH:mm", Br) ?? "ABERTO", t.Operador ?? "",
                t.VendasValidas.ToString(Br), Dinheiro.Formatar(t.VendasCentavos)
            }));

        if (r.Movimentos.Count > 0)
            doc.Secao("Movimentos do caixa").Tabela(
                new[] { new ColunaPdf("Data", 1.6), new ColunaPdf("Tipo", 1.4), new ColunaPdf("Descrição", 4), new ColunaPdf("Valor", 1.5, AlinhamentoPdf.Direita) },
                r.Movimentos.Select(m => new[] { m.DataHora.ToString("dd/MM HH:mm", Br), m.Tipo.ToString(), m.Descricao ?? "", Dinheiro.Formatar(m.ValorCentavos) }));

        foreach (var aviso in r.Avisos) doc.Paragrafo("• " + aviso, tamanho: 8.5);
        doc.Assinaturas(new[] { "Responsável pelo caixa", "Conferido por" });
        return doc.Gerar();
    }
}
