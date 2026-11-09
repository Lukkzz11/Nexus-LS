using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusLS.BarTender.Models.Designer;

public partial class ModeloEtiquetaDesign : ObservableObject
{
    [ObservableProperty]
    private string _nome = "Modelo 50x30 mm";

    [ObservableProperty]
    private double _larguraMm = 50.0;

    [ObservableProperty]
    private double _alturaMm = 30.0;

    public ObservableCollection<ElementoDesign> Elementos { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public void Salvar(string caminho)
    {
        var json = JsonSerializer.Serialize(this, JsonOpts);
        var dir = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(caminho, json);
    }

    public static ModeloEtiquetaDesign Carregar(string caminho)
    {
        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException("Arquivo de modelo não encontrado.", caminho);
        }

        var json = File.ReadAllText(caminho);
        var modelo = JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, JsonOpts);
        return modelo ?? CriarPadrao50x30();
    }

    // =========================================================================
    // 1. PADRÃO GÔNDOLA VAREJO (50 × 30 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarPadrao50x30()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Padrão Gôndola Varejo (50 × 30 mm)",
            LarguraMm = 50.0,
            AlturaMm = 30.0
        };

        // 1. Cabeçalho Nome da Loja
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 1.5,
            Largura = 46.0,
            Altura = 4.0,
            Texto = "NEXUS LS",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // 2. Linha separadora
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 5.8,
            Largura = 46.0,
            Altura = 0.5,
            EspessuraBorda = 0.8
        });

        // 3. Nome do Produto (dinâmico)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 6.6,
            Largura = 46.0,
            Altura = 7.0,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 9.0,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // 4. Referência
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 14.0,
            Largura = 25.0,
            Altura = 3.5,
            Texto = "REF: {Ref}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 7.5,
            Negrito = false,
            Alinhamento = TextAlignment.Left
        });

        // 5. Tamanho
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 36.0,
            Y = 13.5,
            Largura = 12.0,
            Altura = 4.0,
            Texto = "{Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // 6. Preço Varejo (Destaque grande)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 17.5,
            Largura = 46.0,
            Altura = 5.5,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 12.0,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // 7. Código de Barras (Code 128 / EAN-13)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 2.0,
            Y = 22.8,
            Largura = 46.0,
            Altura = 6.2,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 2. VESTUÁRIO COMPLETA C/ LOGO & GRADE (60 × 40 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarVestuario60x40()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Vestuário Completa c/ Logo & Grade (60 × 40 mm)",
            LarguraMm = 60.0,
            AlturaMm = 40.0
        };

        // Logo no canto superior esquerdo
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Imagem,
            X = 2.0,
            Y = 2.0,
            Largura = 11.0,
            Altura = 11.0,
            ManterProporcao = true
        });

        // Nome da Marca / Loja
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 15.0,
            Y = 2.0,
            Largura = 43.0,
            Altura = 4.0,
            Texto = "NEXUS LIFE SURF",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Linha divisória
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 15.0,
            Y = 6.5,
            Largura = 43.0,
            Altura = 0.5,
            EspessuraBorda = 0.8
        });

        // Descrição do Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 13.5,
            Largura = 56.0,
            Altura = 7.0,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 9.5,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Referência
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 21.0,
            Largura = 32.0,
            Altura = 3.5,
            Texto = "REF: {Ref}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 7.5,
            Negrito = false
        });

        // Moldura do Tamanho em Destaque
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Retangulo,
            X = 38.0,
            Y = 20.5,
            Largura = 20.0,
            Altura = 5.0,
            EspessuraBorda = 0.8,
            Preenchido = false
        });

        // Texto do Tamanho
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 38.0,
            Y = 21.0,
            Largura = 20.0,
            Altura = 4.0,
            Texto = "TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Composição / Informação legal
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 25.5,
            Largura = 56.0,
            Altura = 3.0,
            Texto = "100% ALGODÃO | INDÚSTRIA BRASILEIRA",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 6.5,
            Alinhamento = TextAlignment.Left
        });

        // Preço Varejo
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 29.5,
            Largura = 28.0,
            Altura = 6.0,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 13.0,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Preço Atacado
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 35.8,
            Largura = 28.0,
            Altura = 3.2,
            Texto = "ATACADO: R$ {PrecoAtacado}",
            Variavel = VariavelEtiqueta.PrecoAtacado,
            TamanhoFonte = 6.5,
            Negrito = false,
            Alinhamento = TextAlignment.Left
        });

        // Código de Barras
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 31.0,
            Y = 29.5,
            Largura = 27.0,
            Altura = 9.5,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 3. PROMOÇÃO & OFERTA 'DE / POR' (50 × 30 mm)
    // =========================================================================
    // =========================================================================
    // TAG VESTUÁRIO / ROUPAS (TAMANHO GIGANTE + REF + CÓDIGO) - 35 × 60 mm
    // Estilo Hangtag padrão de confecção (Life Surf / Moda / Vestuário)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarTagVestuarioRefTamanho35x60()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Tag Vestuário / Roupas (Tamanho Gigante + Ref + Código) - 35 × 60 mm",
            LarguraMm = 35.0,
            AlturaMm = 60.0
        };

        // 1. Marca / Nome da Loja (Topo)
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Marca / Logotipo",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 2.5,
            Largura = 31.0,
            Altura = 5.0,
            Texto = "LIFESURF",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 11.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // 2. Slogan da Marca
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Slogan",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 7.5,
            Largura = 31.0,
            Altura = 3.0,
            Texto = "FREEDOM WITHOUT LIMITS",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 4.8,
            Negrito = false,
            Alinhamento = TextAlignment.Center
        });

        // 3. Tamanho em Destaque Gigante (como no modelo físico 'M')
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Tamanho Gigante",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 12.5,
            Largura = 31.0,
            Altura = 14.5,
            Texto = "{Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 28.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // 4. Rótulo 'REF.:'
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Rótulo REF",
            Tipo = TipoElemento.Texto,
            X = 2.5,
            Y = 28.5,
            Largura = 12.0,
            Altura = 4.0,
            Texto = "REF.:",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 8.5,
            Negrito = false,
            Alinhamento = TextAlignment.Left
        });

        // 5. Número da Referência (ex: 8606)
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Número Referência",
            Tipo = TipoElemento.Texto,
            X = 15.0,
            Y = 28.0,
            Largura = 17.5,
            Altura = 4.5,
            Texto = "{Ref}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 10.5,
            Negrito = true,
            Alinhamento = TextAlignment.Right
        });

        // 6. Código de Barras 1D Centralizado (com números legíveis embaixo)
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Código de Barras 1D",
            Tipo = TipoElemento.CodigoBarras,
            X = 2.5,
            Y = 33.5,
            Largura = 30.0,
            Altura = 15.0,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        // 7. Descrição do Produto na Base (ex: 'CAMISA DRYFIT')
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Nome do Produto",
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 51.5,
            Largura = 31.0,
            Altura = 6.0,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 7.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        return m;
    }

    public static ModeloEtiquetaDesign CriarPromocaoDePor50x30()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Promoção & Oferta 'DE / POR' (50 × 30 mm)",
            LarguraMm = 50.0,
            AlturaMm = 30.0
        };

        // Faixa de Destaque
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 1.2,
            Largura = 46.0,
            Altura = 4.5,
            Texto = "⚡ LIQUIDAÇÃO / OFERTA ⚡",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Linha
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 6.0,
            Largura = 46.0,
            Altura = 0.5,
            EspessuraBorda = 0.8
        });

        // Nome do Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 7.0,
            Largura = 46.0,
            Altura = 6.0,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Preço Antigo DE (Riscado)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 13.5,
            Largura = 22.0,
            Altura = 4.5,
            Texto = "DE: R$ 149,90",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 8.0,
            Negrito = false,
            Riscado = true,
            Alinhamento = TextAlignment.Center
        });

        // Preço Promocional POR (Bold)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 24.0,
            Y = 12.5,
            Largura = 24.0,
            Altura = 6.5,
            Texto = "POR: R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 12.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Linha
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 19.5,
            Largura = 46.0,
            Altura = 0.5,
            EspessuraBorda = 0.6
        });

        // Código de Barras
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 2.0,
            Y = 20.5,
            Largura = 46.0,
            Altura = 8.5,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 4. SUPERMERCADO ATACADO & VAREJO (70 × 35 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarSupermercadoAtacadoVarejo70x35()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Supermercado Atacado & Varejo (70 × 35 mm)",
            LarguraMm = 70.0,
            AlturaMm = 35.0
        };

        // Nome da Loja
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 1.5,
            Largura = 66.0,
            Altura = 3.5,
            Texto = "NEXUS ATACAREJO & VAREJO",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 8.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Nome do Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 5.2,
            Largura = 66.0,
            Altura = 6.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 9.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Quadro Varejo (Esquerda)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Retangulo,
            X = 2.0,
            Y = 12.0,
            Largura = 32.0,
            Altura = 11.5,
            EspessuraBorda = 0.8
        });
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 3.0,
            Y = 12.5,
            Largura = 30.0,
            Altura = 3.0,
            Texto = "PREÇO VAREJO",
            TamanhoFonte = 6.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 3.0,
            Y = 16.0,
            Largura = 30.0,
            Altura = 6.5,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 13.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Quadro Atacado (Direita)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Retangulo,
            X = 36.0,
            Y = 12.0,
            Largura = 32.0,
            Altura = 11.5,
            EspessuraBorda = 0.8
        });
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 37.0,
            Y = 12.5,
            Largura = 30.0,
            Altura = 3.0,
            Texto = "A PARTIR DE 5 UNIDADES",
            TamanhoFonte = 6.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 37.0,
            Y = 16.0,
            Largura = 30.0,
            Altura = 6.5,
            Texto = "R$ {PrecoAtacado}",
            Variavel = VariavelEtiqueta.PrecoAtacado,
            TamanhoFonte = 13.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Código de Barras
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 4.0,
            Y = 24.5,
            Largura = 62.0,
            Altura = 9.5,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 5. MODERNA C/ LOGO & QR CODE PIX (50 × 40 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarComLogoEQrCode50x40()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Moderna c/ Logo & QR Code Pix (50 × 40 mm)",
            LarguraMm = 50.0,
            AlturaMm = 40.0
        };

        // Logo
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Imagem,
            X = 2.0,
            Y = 2.0,
            Largura = 13.0,
            Altura = 13.0,
            ManterProporcao = true
        });

        // Loja
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 17.0,
            Y = 2.0,
            Largura = 31.0,
            Altura = 4.0,
            Texto = "NEXUS LS",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Nome Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 17.0,
            Y = 6.5,
            Largura = 31.0,
            Altura = 8.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Preço Varejo
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 17.0,
            Largura = 30.0,
            Altura = 7.0,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 14.0,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Ref & Tamanho
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 24.5,
            Largura = 30.0,
            Altura = 3.5,
            Texto = "REF: {Ref} - TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 7.5,
            Alinhamento = TextAlignment.Left
        });

        // QR Code no canto inferior direito
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 33.0,
            Y = 18.0,
            Largura = 15.0,
            Altura = 15.0,
            TipoCodigo = TipoSimboloCodigo.QrCode,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = false
        });

        // Código 1D na base
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 2.0,
            Y = 29.5,
            Largura = 30.0,
            Altura = 9.0,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 6. GÔNDOLA MINI / FARMÁCIA (33 × 22 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarGondolaFarmacia33x22()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Gôndola Mini / Farmácia (33 × 22 mm)",
            LarguraMm = 33.0,
            AlturaMm = 22.0
        };

        // Nome
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 1.0,
            Largura = 31.0,
            Altura = 5.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 7.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Referência
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 6.8,
            Largura = 31.0,
            Altura = 2.5,
            Texto = "REF: {Ref}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 5.5,
            Alinhamento = TextAlignment.Center
        });

        // Preço
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 9.5,
            Largura = 31.0,
            Altura = 4.8,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 10.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Código
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 2.0,
            Y = 14.5,
            Largura = 29.0,
            Altura = 6.8,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 7. JOIAS, RELÓGIOS & ÓCULOS (35 × 15 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarCompactoJoias35x15()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Joias, Relógios & Óculos (35 × 15 mm)",
            LarguraMm = 35.0,
            AlturaMm = 15.0
        };

        // Nome Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 1.0,
            Largura = 33.0,
            Altura = 3.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 6.5,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Ref & Tamanho
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 4.8,
            Largura = 18.0,
            Altura = 2.8,
            Texto = "REF: {Ref} TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 5.5
        });

        // Preço
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 8.0,
            Largura = 18.0,
            Altura = 5.8,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 9.5,
            Negrito = true
        });

        // Código de barras à direita
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 20.0,
            Y = 5.0,
            Largura = 14.0,
            Altura = 8.8,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = false
        });

        return m;
    }

    // =========================================================================
    // 8. ÓCULOS & JOIAS BORBOLETA DUPLA (50 × 15 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarOculosBorboleta50x15()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Óculos & Joias Borboleta Dupla (50 × 15 mm)",
            LarguraMm = 50.0,
            AlturaMm = 15.0
        };

        // Linha divisória central (marca de dobra)
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 25.0,
            Y = 1.0,
            Largura = 0.5,
            Altura = 13.0,
            EspessuraBorda = 0.5
        });

        // Face Esquerda: Loja & Ref
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 1.5,
            Largura = 23.0,
            Altura = 3.5,
            Texto = "NEXUS ÓCULOS",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 6.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 5.5,
            Largura = 23.0,
            Altura = 3.0,
            Texto = "REF: {Ref}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 6.0,
            Alinhamento = TextAlignment.Center
        });
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 1.0,
            Y = 9.0,
            Largura = 23.0,
            Altura = 3.0,
            Texto = "TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 6.0,
            Alinhamento = TextAlignment.Center
        });

        // Face Direita: Preço & Código
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 26.0,
            Y = 1.5,
            Largura = 23.0,
            Altura = 4.8,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 9.5,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 26.5,
            Y = 6.8,
            Largura = 22.0,
            Altura = 6.8,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = false
        });

        return m;
    }

    // =========================================================================
    // 9. CAIXA, PACOTE & IDENTIFICAÇÃO (100 × 50 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarCaixaEnvioMedio100x50()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Caixa, Pacote & Identificação (100 × 50 mm)",
            LarguraMm = 100.0,
            AlturaMm = 50.0
        };

        // Moldura externa
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Retangulo,
            X = 2.0,
            Y = 2.0,
            Largura = 96.0,
            Altura = 46.0,
            EspessuraBorda = 1.2
        });

        // Cabeçalho
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 4.0,
            Y = 4.0,
            Largura = 92.0,
            Altura = 4.5,
            Texto = "NEXUS DISTRIBUIÇÃO & EXPEDIÇÃO",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 9.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        // Linha
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 4.0,
            Y = 9.2,
            Largura = 92.0,
            Altura = 0.5,
            EspessuraBorda = 0.8
        });

        // Nome Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 4.0,
            Y = 11.5,
            Largura = 68.0,
            Altura = 9.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 11.0,
            Negrito = true
        });

        // Detalhes
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 4.0,
            Y = 22.0,
            Largura = 68.0,
            Altura = 4.0,
            Texto = "REF: {Ref}  |  TAM: {Tamanho}  |  CÓD: {Codigo}",
            TamanhoFonte = 8.0
        });

        // QR Code à direita
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 75.0,
            Y = 11.5,
            Largura = 21.0,
            Altura = 21.0,
            TipoCodigo = TipoSimboloCodigo.QrCode,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = false
        });

        // Código de Barras largo
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 4.0,
            Y = 27.5,
            Largura = 68.0,
            Altura = 18.0,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 10. LOGÍSTICA & TRANSPORTE PADRÃO (100 × 150 mm)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarLogistica100x150()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Logística & Transporte Padrão (100 × 150 mm)",
            LarguraMm = 100.0,
            AlturaMm = 150.0
        };

        // Borda externa
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Retangulo,
            X = 3.0,
            Y = 3.0,
            Largura = 94.0,
            Altura = 144.0,
            EspessuraBorda = 1.5
        });

        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 6.0,
            Y = 6.0,
            Largura = 88.0,
            Altura = 8.0,
            Texto = "DOCUMENTO AUXILIAR DE TRANSPORTE",
            Variavel = VariavelEtiqueta.Nenhuma,
            TamanhoFonte = 11.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Linha,
            X = 6.0,
            Y = 15.0,
            Largura = 88.0,
            Altura = 1.0,
            EspessuraBorda = 1.0
        });

        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 6.0,
            Y = 18.0,
            Largura = 88.0,
            Altura = 14.0,
            Texto = "DESTINATÁRIO / PRODUTO:\n{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 10.0,
            Negrito = true
        });

        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 10.0,
            Y = 40.0,
            Largura = 80.0,
            Altura = 35.0,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 35.0,
            Y = 85.0,
            Largura = 30.0,
            Altura = 30.0,
            TipoCodigo = TipoSimboloCodigo.QrCode,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = false
        });

        return m;
    }

    // =========================================================================
    // 11. FOLHA A4 PIMACO 6180 (63,5 × 25,4 mm - 30 Etiquetas)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarFolhaA4Pimaco6180()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Folha A4 Pimaco 6180 (63,5 × 25,4 mm - 30 Etiquetas)",
            LarguraMm = 63.5,
            AlturaMm = 25.4
        };

        // Loja
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 1.5,
            Largura = 38.0,
            Altura = 3.5,
            Texto = "NEXUS LS",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 7.5,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 5.2,
            Largura = 38.0,
            Altura = 6.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 8.5,
            Negrito = true,
            Alinhamento = TextAlignment.Left
        });

        // Ref & Tamanho
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 2.0,
            Y = 12.0,
            Largura = 38.0,
            Altura = 3.0,
            Texto = "REF: {Ref} - TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 7.0
        });

        // Preço à direita
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 41.0,
            Y = 2.0,
            Largura = 20.5,
            Altura = 6.0,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 12.0,
            Negrito = true,
            Alinhamento = TextAlignment.Right
        });

        // Código de barras
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 2.0,
            Y = 15.5,
            Largura = 59.5,
            Altura = 8.5,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        return m;
    }

    // =========================================================================
    // 12. FOLHA A4 PIMACO 6182 (101,6 × 38,1 mm - 14 Etiquetas)
    // =========================================================================
    public static ModeloEtiquetaDesign CriarFolhaA4Pimaco6182()
    {
        var m = new ModeloEtiquetaDesign
        {
            Nome = "Folha A4 Pimaco 6182 (101,6 × 38,1 mm - 14 Etiquetas)",
            LarguraMm = 101.6,
            AlturaMm = 38.1
        };

        // Logo
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Imagem,
            X = 3.0,
            Y = 3.0,
            Largura = 14.0,
            Altura = 14.0,
            ManterProporcao = true
        });

        // Loja
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 20.0,
            Y = 3.0,
            Largura = 46.0,
            Altura = 4.0,
            Texto = "NEXUS LS",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 9.0,
            Negrito = true
        });

        // Produto
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 20.0,
            Y = 7.5,
            Largura = 46.0,
            Altura = 7.5,
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = 10.0,
            Negrito = true
        });

        // Detalhes
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 20.0,
            Y = 15.5,
            Largura = 46.0,
            Altura = 3.5,
            Texto = "REF: {Ref}  |  TAM: {Tamanho}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = 8.0
        });

        // Preço
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.Texto,
            X = 68.0,
            Y = 3.0,
            Largura = 30.0,
            Altura = 8.0,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 15.0,
            Negrito = true,
            Alinhamento = TextAlignment.Right
        });

        // Código de barras
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 3.0,
            Y = 20.5,
            Largura = 63.0,
            Altura = 15.5,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });

        // QR Code à direita
        m.Elementos.Add(new ElementoDesign
        {
            Tipo = TipoElemento.CodigoBarras,
            X = 73.0,
            Y = 12.0,
            Largura = 25.0,
            Altura = 24.0,
            TipoCodigo = TipoSimboloCodigo.QrCode,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = false
        });

        return m;
    }

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
