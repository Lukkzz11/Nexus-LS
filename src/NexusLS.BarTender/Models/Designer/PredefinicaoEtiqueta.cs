using Wpf.Ui.Controls;

namespace NexusLS.BarTender.Models.Designer;

public class PredefinicaoEtiqueta
{
    public string Nome { get; set; } = "";
    public string Categoria { get; set; } = "Geral";
    public string Dimensoes { get; set; } = "";
    public double LarguraMm { get; set; } = 50.0;
    public double AlturaMm { get; set; } = 30.0;
    public string TipoSugerido { get; set; } = "Normal"; // "Normal", "Composicao", "EmBranco"
    public string Descricao { get; set; } = "";
    public SymbolRegular Simbolo { get; set; } = SymbolRegular.Tag24;
    public Func<ModeloEtiquetaDesign> Fabrica { get; set; } = null!;

    public static List<PredefinicaoEtiqueta> ObterCatalogoCompleto()
    {
        return new List<PredefinicaoEtiqueta>
        {
            // === VAREJO & GÔNDOLA (ETIQUETAS NORMAIS) ===
            new()
            {
                Nome = "Padrão Gôndola Varejo (50 × 30 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "50 × 30 mm",
                LarguraMm = 50.0,
                AlturaMm = 30.0,
                TipoSugerido = "Normal",
                Descricao = "O formato mais usado no comércio: cabeçalho da loja, nome do produto, referência, tamanho, preço em destaque e código de barras.",
                Simbolo = SymbolRegular.Tag24,
                Fabrica = ModeloEtiquetaDesign.CriarPadrao50x30
            },
            new()
            {
                Nome = "Supermercado Atacado & Varejo (70 × 35 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "70 × 35 mm",
                LarguraMm = 70.0,
                AlturaMm = 35.0,
                TipoSugerido = "Normal",
                Descricao = "Gôndola estilo atacarejo: dois quadros comparativos com preço de varejo unitário e preço promocional a partir de 5 unidades.",
                Simbolo = SymbolRegular.Cart24,
                Fabrica = ModeloEtiquetaDesign.CriarSupermercadoAtacadoVarejo70x35
            },
            new()
            {
                Nome = "Moderna c/ Logo & QR Code Pix (50 × 40 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "50 × 40 mm",
                LarguraMm = 50.0,
                AlturaMm = 40.0,
                TipoSugerido = "Normal",
                Descricao = "Layout premium com logotipo da loja, preço em tamanho grande, código de barras 1D e QR Code para pagamento Pix ou catálogo online.",
                Simbolo = SymbolRegular.QrCode24,
                Fabrica = ModeloEtiquetaDesign.CriarComLogoEQrCode50x40
            },
            new()
            {
                Nome = "Gôndola Mini / Farmácia (33 × 22 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "33 × 22 mm",
                LarguraMm = 33.0,
                AlturaMm = 22.0,
                TipoSugerido = "Normal",
                Descricao = "Formato compacto de 3 colunas padrão para farmácias, perfumarias e prateleiras com espaço reduzido.",
                Simbolo = SymbolRegular.Tag24,
                Fabrica = ModeloEtiquetaDesign.CriarGondolaFarmacia33x22
            },
            new()
            {
                Nome = "Promoção & Oferta 'DE / POR' (50 × 30 mm)",
                Categoria = "Promoções & Ofertas",
                Dimensoes = "50 × 30 mm",
                LarguraMm = 50.0,
                AlturaMm = 30.0,
                TipoSugerido = "Normal",
                Descricao = "Perfeita para liquidações e queima de estoque: faixa de oferta, preço 'DE' riscado e preço 'POR' em negrito com código de barras.",
                Simbolo = SymbolRegular.Flash24,
                Fabrica = ModeloEtiquetaDesign.CriarPromocaoDePor50x30
            },

            // === COMPOSIÇÃO TÊXTIL & VESTUÁRIO ===
            new()
            {
                Nome = "Composição Têxtil Vertical (40 × 70 mm)",
                Categoria = "Composição Têxtil",
                Dimensoes = "40 × 70 mm",
                LarguraMm = 40.0,
                AlturaMm = 70.0,
                TipoSugerido = "Composicao",
                Descricao = "Padrão têxtil/cetim oficial: Razão social, CNPJ, Indústria Brasileira, Composição de tecidos, Tamanho em destaque e Ícones de lavagem.",
                Simbolo = SymbolRegular.ShoppingBag24,
                Fabrica = ModeloEtiquetaDesign.CriarComposicaoTextilVertical40x70
            },
            new()
            {
                Nome = "Composição Têxtil Compacta (35 × 50 mm)",
                Categoria = "Composição Têxtil",
                Dimensoes = "35 × 50 mm",
                LarguraMm = 35.0,
                AlturaMm = 50.0,
                TipoSugerido = "Composicao",
                Descricao = "Formato compacto para confecções, moda íntima, praia e infantil com composição, lavagem resumida e código de barras.",
                Simbolo = SymbolRegular.ShoppingBag24,
                Fabrica = ModeloEtiquetaDesign.CriarComposicaoTextilCompacta35x50
            },
            new()
            {
                Nome = "Vestuário Completa c/ Logo & Grade (60 × 40 mm)",
                Categoria = "Vestuário & Moda",
                Dimensoes = "60 × 40 mm",
                LarguraMm = 60.0,
                AlturaMm = 40.0,
                TipoSugerido = "Composicao",
                Descricao = "Etiqueta completa para confecção: logotipo, composição têxtil, caixa com tamanho em destaque, preço de varejo e atacado.",
                Simbolo = SymbolRegular.ShoppingBag24,
                Fabrica = ModeloEtiquetaDesign.CriarVestuario60x40
            },
            new()
            {
                Nome = "Tag Vestuário / Roupas (Tamanho Gigante + Ref + Código) - 35 × 60 mm",
                Categoria = "Vestuário & Moda",
                Dimensoes = "35 × 60 mm",
                LarguraMm = 35.0,
                AlturaMm = 60.0,
                TipoSugerido = "Normal",
                Descricao = "Padrão de confecção e marcas de moda (Life Surf): marca no topo, tamanho gigante no centro ('M'), rótulo REF, código de barras e descrição na base.",
                Simbolo = SymbolRegular.Tag24,
                Fabrica = ModeloEtiquetaDesign.CriarTagVestuarioRefTamanho35x60
            },

            // === JOIAS & ÓCULOS ===
            new()
            {
                Nome = "Joias, Relógios & Óculos (35 × 15 mm)",
                Categoria = "Joias & Óculos",
                Dimensoes = "35 × 15 mm",
                LarguraMm = 35.0,
                AlturaMm = 15.0,
                TipoSugerido = "Normal",
                Descricao = "Etiqueta adesiva ultra compacta para anéis, brincos, pulseiras, colares e hastes de óculos.",
                Simbolo = SymbolRegular.Sparkle24,
                Fabrica = ModeloEtiquetaDesign.CriarCompactoJoias35x15
            },
            new()
            {
                Nome = "Óculos & Joias Borboleta Dupla (50 × 15 mm)",
                Categoria = "Joias & Óculos",
                Dimensoes = "50 × 15 mm",
                LarguraMm = 50.0,
                AlturaMm = 15.0,
                TipoSugerido = "Normal",
                Descricao = "Formato borboleta dobrável com duas faces: lado esquerdo com loja e referência, lado direito com preço e código de barras.",
                Simbolo = SymbolRegular.Tag24,
                Fabrica = ModeloEtiquetaDesign.CriarOculosBorboleta50x15
            },

            // === LOGÍSTICA & CAIXAS ===
            new()
            {
                Nome = "Caixa, Pacote & Identificação (100 × 50 mm)",
                Categoria = "Logística & Caixas",
                Dimensoes = "100 × 50 mm",
                LarguraMm = 100.0,
                AlturaMm = 50.0,
                TipoSugerido = "Normal",
                Descricao = "Identificação de caixas de papelão, caixas coletivas e volumes para estoque ou expedição com QR Code e código largo.",
                Simbolo = SymbolRegular.Box24,
                Fabrica = ModeloEtiquetaDesign.CriarCaixaEnvioMedio100x50
            },
            new()
            {
                Nome = "Logística & Transporte Padrão (100 × 150 mm)",
                Categoria = "Logística & Caixas",
                Dimensoes = "100 × 150 mm",
                LarguraMm = 100.0,
                AlturaMm = 150.0,
                TipoSugerido = "Normal",
                Descricao = "Etiqueta padrão para logística, transportadoras, pallets e envios do Mercado Livre / Correios com grande legibilidade.",
                Simbolo = SymbolRegular.Box24,
                Fabrica = ModeloEtiquetaDesign.CriarLogistica100x150
            },

            // === FOLHAS A4 ===
            new()
            {
                Nome = "Folha A4 Pimaco 6180 (63,5 × 25,4 mm - 30 Etiquetas)",
                Categoria = "Folhas A4",
                Dimensoes = "63,5 × 25,4 mm",
                LarguraMm = 63.5,
                AlturaMm = 25.4,
                TipoSugerido = "Normal",
                Descricao = "Etiqueta para folhas carta/A4 de papel comum ou adesivo Pimaco 6180 (30 etiquetas por folha). Não requer impressora térmica.",
                Simbolo = SymbolRegular.Document24,
                Fabrica = ModeloEtiquetaDesign.CriarFolhaA4Pimaco6180
            },
            new()
            {
                Nome = "Folha A4 Pimaco 6182 (101,6 × 38,1 mm - 14 Etiquetas)",
                Categoria = "Folhas A4",
                Dimensoes = "101,6 × 38,1 mm",
                LarguraMm = 101.6,
                AlturaMm = 38.1,
                TipoSugerido = "Normal",
                Descricao = "Etiqueta média para folha A4 Pimaco 6182 (14 etiquetas por folha). Ideal para pastas, caixas organizadoras e produtos maiores.",
                Simbolo = SymbolRegular.Document24,
                Fabrica = ModeloEtiquetaDesign.CriarFolhaA4Pimaco6182
            },

            // === EM BRANCO (PERSONALIZADO) ===
            new()
            {
                Nome = "Em Branco Gôndola (50 × 30 mm)",
                Categoria = "Personalizado (Em Branco)",
                Dimensoes = "50 × 30 mm",
                LarguraMm = 50.0,
                AlturaMm = 30.0,
                TipoSugerido = "EmBranco",
                Descricao = "Folha limpa no formato 50x30 mm para desenhar e posicionar seus próprios elementos do zero.",
                Simbolo = SymbolRegular.Square24,
                Fabrica = () => ModeloEtiquetaDesign.CriarEmBranco(50.0, 30.0, "Em Branco 50x30 mm")
            },
            new()
            {
                Nome = "Em Branco Têxtil Vertical (40 × 70 mm)",
                Categoria = "Personalizado (Em Branco)",
                Dimensoes = "40 × 70 mm",
                LarguraMm = 40.0,
                AlturaMm = 70.0,
                TipoSugerido = "EmBranco",
                Descricao = "Folha limpa vertical 40x70 mm ideal para composição têxtil personalizada.",
                Simbolo = SymbolRegular.Square24,
                Fabrica = () => ModeloEtiquetaDesign.CriarEmBranco(40.0, 70.0, "Em Branco Têxtil 40x70 mm")
            },
            new()
            {
                Nome = "Em Branco Caixa (100 × 50 mm)",
                Categoria = "Personalizado (Em Branco)",
                Dimensoes = "100 × 50 mm",
                LarguraMm = 100.0,
                AlturaMm = 50.0,
                TipoSugerido = "EmBranco",
                Descricao = "Folha limpa 100x50 mm para etiquetas de caixas e identificação.",
                Simbolo = SymbolRegular.Square24,
                Fabrica = () => ModeloEtiquetaDesign.CriarEmBranco(100.0, 50.0, "Em Branco 100x50 mm")
            },
            new()
            {
                Nome = "Em Branco Transporte (100 × 150 mm)",
                Categoria = "Personalizado (Em Branco)",
                Dimensoes = "100 × 150 mm",
                LarguraMm = 100.0,
                AlturaMm = 150.0,
                TipoSugerido = "EmBranco",
                Descricao = "Folha limpa 100x150 mm padrão para transporte e logística.",
                Simbolo = SymbolRegular.Square24,
                Fabrica = () => ModeloEtiquetaDesign.CriarEmBranco(100.0, 150.0, "Em Branco 100x150 mm")
            }
        };
    }
}
