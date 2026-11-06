namespace NexusLS.BarTender.Models.Designer;

public class PredefinicaoEtiqueta
{
    public string Nome { get; set; } = "";
    public string Categoria { get; set; } = "Geral";
    public string Dimensoes { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Icone { get; set; } = "🏷️";
    public Func<ModeloEtiquetaDesign> Fabrica { get; set; } = null!;

    public static List<PredefinicaoEtiqueta> ObterCatalogoCompleto()
    {
        return new List<PredefinicaoEtiqueta>
        {
            new()
            {
                Nome = "Padrão Gôndola Varejo (50 × 30 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "50 × 30 mm",
                Descricao = "O formato mais usado no comércio: cabeçalho da loja, nome do produto, referência, tamanho, preço em destaque e código de barras.",
                Icone = "🏷️",
                Fabrica = ModeloEtiquetaDesign.CriarPadrao50x30
            },
            new()
            {
                Nome = "Vestuário Completa c/ Logo & Grade (60 × 40 mm)",
                Categoria = "Vestuário & Moda",
                Dimensoes = "60 × 40 mm",
                Descricao = "Etiqueta completa para confecção: logotipo, composição têxtil, caixa com tamanho em destaque, preço de varejo e atacado.",
                Icone = "👕",
                Fabrica = ModeloEtiquetaDesign.CriarVestuario60x40
            },
            new()
            {
                Nome = "Promoção & Oferta 'DE / POR' (50 × 30 mm)",
                Categoria = "Promoções & Ofertas",
                Dimensoes = "50 × 30 mm",
                Descricao = "Perfeita para liquidações e queima de estoque: faixa de oferta, preço 'DE' riscado e preço 'POR' em negrito com código de barras.",
                Icone = "⚡",
                Fabrica = ModeloEtiquetaDesign.CriarPromocaoDePor50x30
            },
            new()
            {
                Nome = "Supermercado Atacado & Varejo (70 × 35 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "70 × 35 mm",
                Descricao = "Gôndola estilo atacarejo: dois quadros comparativos com preço de varejo unitário e preço promocional a partir de 5 unidades.",
                Icone = "🛒",
                Fabrica = ModeloEtiquetaDesign.CriarSupermercadoAtacadoVarejo70x35
            },
            new()
            {
                Nome = "Moderna c/ Logo & QR Code Pix (50 × 40 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "50 × 40 mm",
                Descricao = "Layout premium com logotipo da loja, preço em tamanho grande, código de barras 1D e QR Code para pagamento Pix ou catálogo online.",
                Icone = "📱",
                Fabrica = ModeloEtiquetaDesign.CriarComLogoEQrCode50x40
            },
            new()
            {
                Nome = "Gôndola Mini / Farmácia (33 × 22 mm)",
                Categoria = "Varejo & Gôndola",
                Dimensoes = "33 × 22 mm",
                Descricao = "Formato compacto de 3 colunas padrão para farmácias, perfumarias e prateleiras com espaço reduzido.",
                Icone = "💊",
                Fabrica = ModeloEtiquetaDesign.CriarGondolaFarmacia33x22
            },
            new()
            {
                Nome = "Joias, Relógios & Óculos (35 × 15 mm)",
                Categoria = "Joias & Óculos",
                Dimensoes = "35 × 15 mm",
                Descricao = "Etiqueta adesiva ultra compacta para anéis, brincos, pulseiras, colares e hastes de óculos.",
                Icone = "💍",
                Fabrica = ModeloEtiquetaDesign.CriarCompactoJoias35x15
            },
            new()
            {
                Nome = "Óculos & Joias Borboleta Dupla (50 × 15 mm)",
                Categoria = "Joias & Óculos",
                Dimensoes = "50 × 15 mm",
                Descricao = "Formato borboleta dobrável com duas faces: lado esquerdo com loja e referência, lado direito com preço e código de barras.",
                Icone = "👓",
                Fabrica = ModeloEtiquetaDesign.CriarOculosBorboleta50x15
            },
            new()
            {
                Nome = "Caixa, Pacote & Identificação (100 × 50 mm)",
                Categoria = "Logística & Caixas",
                Dimensoes = "100 × 50 mm",
                Descricao = "Identificação de caixas de papelão, caixas coletivas e volumes para estoque ou expedição com QR Code e código largo.",
                Icone = "📦",
                Fabrica = ModeloEtiquetaDesign.CriarCaixaEnvioMedio100x50
            },
            new()
            {
                Nome = "Logística & Transporte Padrão (100 × 150 mm)",
                Categoria = "Logística & Caixas",
                Dimensoes = "100 × 150 mm",
                Descricao = "Etiqueta padrão para logística, transportadoras, pallets e envios do Mercado Livre / Correios com grande legibilidade.",
                Icone = "🚚",
                Fabrica = ModeloEtiquetaDesign.CriarLogistica100x150
            },
            new()
            {
                Nome = "Folha A4 Pimaco 6180 (63,5 × 25,4 mm - 30 Etiquetas)",
                Categoria = "Folhas A4 (Laser / Jato de Tinta)",
                Dimensoes = "63,5 × 25,4 mm",
                Descricao = "Etiqueta para folhas carta/A4 de papel comum ou adesivo Pimaco 6180 (30 etiquetas por folha). Não requer impressora térmica.",
                Icone = "📄",
                Fabrica = ModeloEtiquetaDesign.CriarFolhaA4Pimaco6180
            },
            new()
            {
                Nome = "Folha A4 Pimaco 6182 (101,6 × 38,1 mm - 14 Etiquetas)",
                Categoria = "Folhas A4 (Laser / Jato de Tinta)",
                Dimensoes = "101,6 × 38,1 mm",
                Descricao = "Etiqueta média para folha A4 Pimaco 6182 (14 etiquetas por folha). Ideal para pastas, caixas organizadoras e produtos maiores.",
                Icone = "📄",
                Fabrica = ModeloEtiquetaDesign.CriarFolhaA4Pimaco6182
            }
        };
    }
}
