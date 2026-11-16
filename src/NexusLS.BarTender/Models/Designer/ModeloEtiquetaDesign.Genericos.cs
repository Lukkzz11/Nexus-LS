using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace NexusLS.BarTender.Models.Designer;

/// <summary>Modelo em branco, composição têxtil e geradores genéricos por tamanho.</summary>
public partial class ModeloEtiquetaDesign
{
    public static ModeloEtiquetaDesign CriarEmBranco(double larguraMm = 50.0, double alturaMm = 30.0, string? nome = null)
    {
        return new ModeloEtiquetaDesign
        {
            Nome = nome ?? $"Em Branco ({larguraMm:0.#} × {alturaMm:0.#} mm)",
            LarguraMm = Math.Max(10.0, larguraMm),
            AlturaMm = Math.Max(10.0, alturaMm)
        };
    }

    public static ModeloEtiquetaDesign CriarComposicaoTextilVertical40x70()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Composição Têxtil Vertical (40 × 70 mm)",
            LarguraMm = 40.0,
            AlturaMm = 70.0
        };

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Cabeçalho / Marca",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 2.5,
            Largura = 36.0,
            Altura = 5.0,
            Texto = "NEXUS CONFECÇÕES",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 10.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "CNPJ / Indústria",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 7.5,
            Largura = 36.0,
            Altura = 6.0,
            Texto = "CNPJ 00.000.000/0001-00\nINDÚSTRIA BRASILEIRA",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 6.0,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Linha 1",
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 14.5,
            Largura = 36.0,
            Altura = 0.5,
            EspessuraBorda = 0.8
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Tamanho",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 16.0,
            Largura = 36.0,
            Altura = 13.0,
            Texto = "TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 22.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Linha 2",
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 30.0,
            Largura = 36.0,
            Altura = 0.5,
            EspessuraBorda = 0.8
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Composição dos Fios",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 31.5,
            Largura = 36.0,
            Altura = 8.0,
            Texto = "COMPOSIÇÃO:\n100% ALGODÃO / COTTON",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 7.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Instruções de Lavagem",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 40.5,
            Largura = 36.0,
            Altura = 11.0,
            Texto = "LAVAR À MÃO OU MÁQUINA ATÉ 40°C\nNÃO USAR ALVEJANTE À BASE DE CLORO\nSECAR EM VARAL À SOMBRA\nPASSAR A FERRO ATÉ 110°C",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 5.2,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Linha 3",
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 52.5,
            Largura = 36.0,
            Altura = 0.5,
            EspessuraBorda = 0.8
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Referência",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 53.5,
            Largura = 36.0,
            Altura = 3.5,
            Texto = "REF: {Ref}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 7.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Código de Barras",
            Tipo = TipoElemento.CodigoBarras,
            X = 2.0,
            Y = 57.5,
            Largura = 36.0,
            Altura = 10.5,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    public static ModeloEtiquetaDesign CriarComposicaoTextilCompacta35x50()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Composição Têxtil Compacta (35 × 50 mm)",
            LarguraMm = 35.0,
            AlturaMm = 50.0
        };

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Marca",
            Tipo = TipoElemento.Texto,
            X = 1.5,
            Y = 1.5,
            Largura = 32.0,
            Altura = 4.0,
            Texto = "NEXUS CONFECÇÕES",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "CNPJ",
            Tipo = TipoElemento.Texto,
            X = 1.5,
            Y = 5.5,
            Largura = 32.0,
            Altura = 4.5,
            Texto = "CNPJ 00.000.000/0001-00 - IND. BRASILEIRA",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 5.0,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Tamanho",
            Tipo = TipoElemento.Texto,
            X = 1.5,
            Y = 10.5,
            Largura = 32.0,
            Altura = 9.0,
            Texto = "TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 16.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Composição",
            Tipo = TipoElemento.Texto,
            X = 1.5,
            Y = 20.0,
            Largura = 32.0,
            Altura = 5.0,
            Texto = "COMPOSIÇÃO: 100% ALGODÃO",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 6.2,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Cuidados",
            Tipo = TipoElemento.Texto,
            X = 1.5,
            Y = 25.5,
            Largura = 32.0,
            Altura = 8.0,
            Texto = "LAVAR À MÃO - NÃO USAR CLORO\nSECAR EM VARAL - PASSAR A 110°C",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 5.0,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Código de Barras",
            Tipo = TipoElemento.CodigoBarras,
            X = 1.5,
            Y = 34.5,
            Largura = 32.0,
            Altura = 13.5,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    public static ModeloEtiquetaDesign CriarEtiquetaComposicaoGenerica(double larguraMm, double alturaMm, string? nome = null)
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = nome ?? $"Composição ({larguraMm:0.#} × {alturaMm:0.#} mm)",
            LarguraMm = Math.Max(10.0, larguraMm),
            AlturaMm = Math.Max(10.0, alturaMm)
        };

        double margem = 2.0;
        double w = Math.Max(5.0, m.LarguraMm - (margem * 2));

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Marca",
            Tipo = TipoElemento.Texto,
            X = margem,
            Y = 2.0,
            Largura = w,
            Altura = 5.0,
            Texto = "{Loja}",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 9.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Tamanho",
            Tipo = TipoElemento.Texto,
            X = margem,
            Y = 8.0,
            Largura = w,
            Altura = 10.0,
            Texto = "TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 18.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Composição",
            Tipo = TipoElemento.Texto,
            X = margem,
            Y = 19.0,
            Largura = w,
            Altura = 5.0,
            Texto = "100% ALGODÃO",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 7.0,
            Alinhamento = TextAlignment.Center
        });

        if (m.AlturaMm >= 35.0)
        {
            m.Elementos.Add(new ElementoDesign
            {
                Nome = "Código de Barras",
                Tipo = TipoElemento.CodigoBarras,
                X = margem,
                Y = Math.Max(25.0, m.AlturaMm - 14.0),
                Largura = w,
                Altura = 12.0,
                TipoCodigo = TipoSimboloCodigo.Code128,
                Variavel = VariavelEtiqueta.CodigoBarras,
                MostrarTextoLegivel = true
            });
        }

        return m;
    }

    public static ModeloEtiquetaDesign CriarEtiquetaNormalGenerica(double larguraMm, double alturaMm, string? nome = null)
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = nome ?? $"Etiqueta ({larguraMm:0.#} × {alturaMm:0.#} mm)",
            LarguraMm = Math.Max(10.0, larguraMm),
            AlturaMm = Math.Max(10.0, alturaMm)
        };

        double margem = 2.0;
        double w = Math.Max(5.0, m.LarguraMm - (margem * 2));

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Cabeçalho",
            Tipo = TipoElemento.Texto,
            X = margem,
            Y = 2.0,
            Largura = w,
            Altura = 4.5,
            Texto = "{Loja}",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Nome do Produto",
            Tipo = TipoElemento.Texto,
            X = margem,
            Y = 7.0,
            Largura = w,
            Altura = 5.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Preço",
            Tipo = TipoElemento.Texto,
            X = margem,
            Y = 13.0,
            Largura = w,
            Altura = 6.5,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 12.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        if (m.AlturaMm >= 25.0)
        {
            m.Elementos.Add(new ElementoDesign
            {
                Nome = "Código de Barras",
                Tipo = TipoElemento.CodigoBarras,
                X = margem,
                Y = Math.Max(19.0, m.AlturaMm - 10.0),
                Largura = w,
                Altura = 8.0,
                TipoCodigo = TipoSimboloCodigo.Code128,
                Variavel = VariavelEtiqueta.CodigoBarras,
                MostrarTextoLegivel = true
            });
        }

        return m;
    }

    // Compatibilidade reversa
    public static ModeloEtiquetaDesign CriarComLogoEQrCode50x30() => CriarComLogoEQrCode50x40();
    public static ModeloEtiquetaDesign CriarCompacto40x25() => CriarCompactoJoias35x15();
}
