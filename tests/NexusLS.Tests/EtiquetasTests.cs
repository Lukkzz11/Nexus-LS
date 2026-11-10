using NexusLS.Core.Etiquetas;

namespace NexusLS.Tests;

public class Code128Tests
{
    [Fact]
    public void A_tabela_tem_os_107_simbolos()
    {
        Assert.Equal(107, Code128.TabelaDePadroes.Count);
    }

    [Fact]
    public void Todo_simbolo_tem_11_modulos_e_o_de_fim_tem_13()
    {
        for (var i = 0; i < 106; i++)
            Assert.Equal(11, Code128.TabelaDePadroes[i].Sum(c => c - '0'));
        Assert.Equal(13, Code128.TabelaDePadroes[106].Sum(c => c - '0'));
    }

    [Fact]
    public void Todo_simbolo_tem_barras_de_largura_total_par_e_seis_elementos()
    {
        // Regra do Code 128: a soma das larguras das 3 barras de cada símbolo é par. Ajuda a pegar erro de digitação na tabela.
        for (var i = 0; i < 106; i++)
        {
            var p = Code128.TabelaDePadroes[i];
            Assert.Equal(6, p.Length);
            Assert.True(p.All(c => c is >= '1' and <= '4'), $"símbolo {i}: largura fora de 1 a 4");
            var barras = (p[0] - '0') + (p[2] - '0') + (p[4] - '0');
            Assert.True(barras % 2 == 0, $"símbolo {i} ({p}): soma das barras ímpar");
        }
    }

    [Fact]
    public void Nao_ha_dois_simbolos_com_o_mesmo_padrao()
    {
        Assert.Equal(107, Code128.TabelaDePadroes.Distinct().Count());
    }

    [Fact]
    public void Conjunto_C_para_numeros_com_quantidade_par_de_digitos()
    {
        var s = Code128.Simbolos("123456");

        Assert.Equal(new[] { Code128.InicioC, 12, 34, 56 }, s.Take(4));
        // verificador = (105 + 12*1 + 34*2 + 56*3) % 103 = 353 % 103 = 44
        Assert.Equal(44, s[^2]);
        Assert.Equal(Code128.Fim, s[^1]);
    }

    [Fact]
    public void Conjunto_B_para_texto_ou_quantidade_impar_de_digitos()
    {
        var s = Code128.Simbolos("Ab1");

        Assert.Equal(new[] { Code128.InicioB, 'A' - ' ', 'b' - ' ', '1' - ' ' }, s.Take(4));
        // verificador = (104 + 33*1 + 66*2 + 17*3) % 103 = 320 % 103 = 11
        Assert.Equal(11, s[^2]);

        Assert.Equal(Code128.InicioB, Code128.Simbolos("12345")[0]); // 5 dígitos: não dá para parear
    }

    [Fact]
    public void Os_modulos_comecam_por_barra_terminam_no_fim_e_tem_o_tamanho_certo()
    {
        var texto = "7891234567895"; // 13 dígitos: B para o 1º, troca para C e 6 pares
        var modulos = Code128.Modulos(texto);

        // início B(11) + 1º dígito(11) + troca para C(11) + 6 pares(66) + verificador(11) + fim(13)
        Assert.Equal(11 + 11 + 11 + 6 * 11 + 11 + 13, modulos.Count);
        Assert.True(modulos[0]);          // começa com barra
        Assert.True(modulos[^1]);         // o símbolo de fim termina em barra
        Assert.True(modulos[^2]);         // ...com 2 módulos de barra
        Assert.False(modulos[^3]);
    }

    [Fact]
    public void Numero_impar_de_digitos_usa_B_no_primeiro_depois_C_com_o_verificador_certo()
    {
        // 104 (início B), '7' = 23, 99 (troca para C), pares 78 91 23 45 67 89 5? -> o 13º dígito forma par com o anterior:
        // "7" + "89 12 34 56 78 95"
        var simbolos = Code128.Simbolos("7891234567895");

        Assert.Equal(new[] { 104, 23, 99, 89, 12, 34, 56, 78, 95, 83, 106 }, simbolos);
    }

    [Fact]
    public void Numero_par_de_digitos_gera_codigo_mais_curto_que_o_conjunto_B()
    {
        var curto = Code128.Modulos("12345678").Count;   // C: início + 4 pares + verificador + fim
        Assert.Equal(11 + 4 * 11 + 11 + 13, curto);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("café")]
    [InlineData("tab\t")]
    public void Texto_que_nao_pode_ser_codificado_e_recusado(string? texto)
    {
        Assert.False(Code128.PodeCodificar(texto));
        if (!string.IsNullOrEmpty(texto)) Assert.Throws<ArgumentException>(() => Code128.Simbolos(texto));
    }

    [Fact]
    public void Aceita_espaco_e_simbolos_comuns()
    {
        Assert.True(Code128.PodeCodificar("REF-100 /A_2"));
        Assert.Equal(0, Code128.Simbolos(" ")[1]); // o espaço é o símbolo 0 no conjunto B
    }
}

public class DiagramadorEtiquetasTests
{
    private static ItemEtiqueta Item(string nome, int quantidade) =>
        new(1, nome, "", 1000, null, quantidade);

    [Fact]
    public void Bobina_tem_uma_etiqueta_por_pagina_sempre_no_canto()
    {
        var paginas = DiagramadorEtiquetas.Paginar(new[] { Item("A", 3) }, FormatoEtiqueta.Bobina50x30);

        Assert.Equal(3, paginas.Count);
        Assert.All(paginas, p => Assert.Equal((0.0, 0.0), (Assert.Single(p).XMm, p[0].YMm)));
    }

    [Fact]
    public void Folha_a4_enche_da_esquerda_para_a_direita_e_de_cima_para_baixo()
    {
        var f = FormatoEtiqueta.FolhaA4Tres_por_Sete;
        var pagina = Assert.Single(DiagramadorEtiquetas.Paginar(new[] { Item("A", 4) }, f));

        Assert.Equal(4, pagina.Count);
        Assert.Equal((7.2, 15.15), (pagina[0].XMm, pagina[0].YMm));
        Assert.Equal(7.2 + 66.0, pagina[1].XMm, 5);          // 63,5 de etiqueta + 2,5 de espaço
        Assert.Equal(15.15, pagina[1].YMm, 5);
        Assert.Equal(7.2 + 2 * 66.0, pagina[2].XMm, 5);
        Assert.Equal(7.2, pagina[3].XMm, 5);                 // quarta: volta à primeira coluna...
        Assert.Equal(15.15 + 38.1, pagina[3].YMm, 5);        // ...na segunda linha
    }

    [Fact]
    public void Passando_de_21_etiquetas_a_folha_a4_abre_outra_pagina()
    {
        var paginas = DiagramadorEtiquetas.Paginar(new[] { Item("A", 25) }, FormatoEtiqueta.FolhaA4Tres_por_Sete);

        Assert.Equal(new[] { 21, 4 }, paginas.Select(p => p.Count));
    }

    [Fact]
    public void Cada_copia_ocupa_uma_posicao_e_itens_diferentes_seguem_em_sequencia()
    {
        var paginas = DiagramadorEtiquetas.Paginar(new[] { Item("A", 2), Item("B", 1) }, FormatoEtiqueta.FolhaA4Tres_por_Sete);

        Assert.Equal(new[] { "A", "A", "B" }, paginas[0].Select(p => p.Item.Nome));
    }

    [Fact]
    public void Primeira_posicao_pula_as_etiquetas_ja_usadas_da_folha()
    {
        var f = FormatoEtiqueta.FolhaA4Tres_por_Sete;

        var paginas = DiagramadorEtiquetas.Paginar(new[] { Item("A", 2) }, f, primeiraPosicao: 5); // 5ª = 2ª linha, 2ª coluna

        Assert.Equal(2, paginas[0].Count);
        Assert.Equal(7.2 + 66.0, paginas[0][0].XMm, 5);
        Assert.Equal(15.15 + 38.1, paginas[0][0].YMm, 5);
    }

    [Fact]
    public void Primeira_posicao_fora_da_folha_e_recusada_e_sem_itens_nao_ha_paginas()
    {
        var f = FormatoEtiqueta.FolhaA4Tres_por_Sete;

        Assert.Throws<ArgumentOutOfRangeException>(() => DiagramadorEtiquetas.Paginar(new[] { Item("A", 1) }, f, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagramadorEtiquetas.Paginar(new[] { Item("A", 1) }, f, 22));
        Assert.Empty(DiagramadorEtiquetas.Paginar(Array.Empty<ItemEtiqueta>(), f));
    }

    [Fact]
    public void Etiquetas_cabem_na_pagina_dos_formatos()
    {
        foreach (var f in FormatoEtiqueta.Todos)
        {
            var ultimaColuna = f.MargemEsquerdaMm + (f.Colunas - 1) * (f.EtiquetaLarguraMm + f.EspacoHorizontalMm) + f.EtiquetaLarguraMm;
            var ultimaLinha = f.MargemSuperiorMm + (f.Linhas - 1) * (f.EtiquetaAlturaMm + f.EspacoVerticalMm) + f.EtiquetaAlturaMm;

            Assert.True(ultimaColuna <= f.PaginaLarguraMm + 0.001, $"{f.Nome}: passa da largura da página");
            Assert.True(ultimaLinha <= f.PaginaAlturaMm + 0.001, $"{f.Nome}: passa da altura da página");
        }
    }

    [Fact]
    public void Total_de_etiquetas_soma_as_copias()
    {
        Assert.Equal(7, DiagramadorEtiquetas.TotalDeEtiquetas(new[] { Item("A", 3), Item("B", 4) }));
    }
}

public class PerfilDePapelTests
{
    [Fact]
    public void Bobina_em_fileiras_soma_margens_etiquetas_e_espacos()
    {
        var f = FormatoEtiqueta.BobinaEmFileiras(3, 33, 22, espacoHorizontalMm: 3, espacoVerticalMm: 2, margemLateralMm: 1.5);
        Assert.Equal(3 * 33 + 2 * 3 + 3, f.PaginaLarguraMm, 3);   // 108 mm de papel
        Assert.Equal(3, f.EtiquetasPorPagina);
        Assert.Empty(f.Validar(larguraMaximaDaImpressoraMm: 110));
    }

    [Fact]
    public void Papel_mais_largo_que_a_impressora_ou_medidas_invalidas_sao_recusados()
    {
        var f = FormatoEtiqueta.BobinaEmFileiras(3, 33, 22);
        Assert.Contains(f.Validar(100), e => e.Contains("impressora"));
        var ruim = f with { EtiquetaLarguraMm = 60 };   // 3 × 60 não cabe no papel de 108 mm
        Assert.Contains(ruim.Validar(), e => e.Contains("largura do papel"));
        Assert.Throws<ArgumentException>(() => FormatoEtiqueta.BobinaEmFileiras(0, 30, 20));
    }

    [Fact]
    public void Tres_colunas_enchem_a_fileira_da_esquerda_para_a_direita()
    {
        var itens = new[] { new ItemEtiqueta(1, "Camisa", "M", 5000, "123", 4) };
        var paginas = DiagramadorEtiquetas.Paginar(itens, FormatoEtiqueta.Bobina3Colunas33x22);
        Assert.Equal(2, paginas.Count);
        Assert.Equal(3, paginas[0].Count);
        Assert.True(paginas[0][1].XMm > paginas[0][0].XMm);
    }
}
