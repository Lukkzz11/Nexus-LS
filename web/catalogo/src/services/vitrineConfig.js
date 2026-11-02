import { MODELOS, IDS_MODELO, IDS_FONTE, PROPORCOES, CHAVES_COR, normalizarHex } from "./temaVitrine";
import { chamar } from "../ponte";
import { getProductImageUrl } from "./imageUploadService";

/**
 * CONFIGURAÇÃO VISUAL DA VITRINE (catálogo online) — editada em Gerenciar Catálogo › Editor visual.
 *
 * Fica no próprio documento da empresa (empresas/{id}.vitrine), que a vitrine pública já lê: nenhuma
 * coleção nova e nenhuma regra nova. Fotos extras de cada produto ficam em produtos/{id}.fotosExtras.
 * Toda entrada externa passa por sanitizeVitrine(); sem configuração salva vale o padrão.
 */

export const BLOCK_IDS = ["banner", "beneficios", "novidades", "busca", "vitrine"];

export const BLOCK_LABELS = {
  banner: "Banner com as novidades",
  beneficios: "Benefícios",
  novidades: "Carrossel de novidades",
  busca: "Busca e categorias",
  vitrine: "Vitrine de produtos"
};

export const WIDTHS = ["estreito", "padrao", "largo"];
export const WIDTH_LABELS = { estreito: "Estreito", padrao: "Padrão", largo: "Largo" };
export const WIDTH_CLASSES = { estreito: "max-w-4xl", padrao: "max-w-7xl", largo: "max-w-[96rem]" };

export const SPACINGS = ["compacto", "normal", "amplo"];
export const SPACING_LABELS = { compacto: "Compacto", normal: "Normal", amplo: "Amplo" };
export const SPACING_CLASSES = { compacto: "mt-3", normal: "mt-8", amplo: "mt-16" };

export const ALIGNS = ["esquerda", "centro"];
export const ALIGN_LABELS = { esquerda: "Esquerda", centro: "Centro" };

export const IMAGE_SIDES = ["direita", "esquerda"];
export const IMAGE_SIDE_LABELS = { direita: "Direita", esquerda: "Esquerda" };

export const DESTINOS_BOTAO = ["vitrine", "novidades", "atacado", "categoria"];
export const DESTINO_LABELS = {
  vitrine: "Ver a vitrine",
  novidades: "Ver as novidades",
  atacado: "Abrir o atacado",
  categoria: "Uma categoria"
};

export const MAX_SLIDES = 6;
export const MAX_FOTOS_PRODUTO = 4; // principal + até 3 extras
export const ATACADO_MIN_PADRAO = 10;
export const INTERVALO_PADRAO_SEG = 40;
export const ENQUADRAMENTO_PADRAO = { focusX: 50, focusY: 50, zoom: 1 };
export const MAX_ZOOM = 3;

const BLOCO_PADRAO = { visible: true, width: "padrao", spacing: "normal", align: "esquerda", imageSide: "direita" };

export function criarVitrinePadrao() {
  return {
    layout: {
      blocks: [
        { ...BLOCO_PADRAO, id: "banner", spacing: "compacto" },
        { ...BLOCO_PADRAO, id: "beneficios", spacing: "compacto" },
        { ...BLOCO_PADRAO, id: "novidades" },
        { ...BLOCO_PADRAO, id: "busca", align: "centro", width: "estreito" },
        { ...BLOCO_PADRAO, id: "vitrine", spacing: "compacto" }
      ]
    },
    banners: [
      {
        id: "novidades",
        badge: "Acabou de chegar",
        title: "Novidades da",
        highlight: "coleção",
        subtitle: "Peças novas com fabricação própria e pronta-entrega.",
        ctaLabel: "Ver novidades",
        ctaDestino: "novidades",
        ctaCategoria: "",
        image: "",
        imagePath: "",
        ...ENQUADRAMENTO_PADRAO
      },
      {
        id: "atacado",
        badge: "Para lojistas",
        title: "Atacado a partir de",
        highlight: `${ATACADO_MIN_PADRAO} peças`,
        subtitle: "Pedido de atacado separado do varejo, com o valor de fábrica.",
        ctaLabel: "Abrir o atacado",
        ctaDestino: "atacado",
        ctaCategoria: "",
        image: "",
        imagePath: "",
        ...ENQUADRAMENTO_PADRAO
      }
    ],
    estilo: { vidro: true },
    atacado: { minPecas: ATACADO_MIN_PADRAO },
    carrossel: { intervaloSeg: INTERVALO_PADRAO_SEG, novidadesQtd: 8 },
    ...criarPersonalizacaoPadrao()
  };
}

export const INFO_PRODUTO = ["categoria", "nome", "descricao", "cores", "tamanhos", "preco"];
export const INFO_PRODUTO_LABELS = {
  categoria: "Categoria e código",
  nome: "Nome",
  descricao: "Descrição",
  cores: "Cores disponíveis",
  tamanhos: "Tamanhos disponíveis",
  preco: "Preços"
};
export const MOSTRAR_PRODUTO = ["foto", "categoria", "codigo", "nome", "descricao", "cores", "tamanhos", "preco", "botao"];
export const MOSTRAR_PRODUTO_LABELS = {
  foto: "Foto",
  categoria: "Categoria",
  codigo: "Código do produto",
  nome: "Nome",
  descricao: "Descrição",
  cores: "Cores disponíveis",
  tamanhos: "Tamanhos disponíveis",
  preco: "Preço (e preço promocional)",
  botao: "Botão Comprar"
};
export const ORDENS_PRODUTO = ["padrao", "nome", "menorPreco", "maiorPreco", "recentes", "manual"];
export const ORDEM_LABELS = {
  padrao: "Como está no estoque",
  nome: "Nome (A a Z)",
  menorPreco: "Menor preço primeiro",
  maiorPreco: "Maior preço primeiro",
  recentes: "Mais recentes primeiro",
  manual: "Ordem manual"
};
export const MODOS_SELECAO = ["todos", "categorias", "produtos"];
export const FORMATOS_PDF = { a4: { rotulo: "A4", mm: [210, 297] }, a5: { rotulo: "A5", mm: [148, 210] }, carta: { rotulo: "Carta", mm: [216, 279] } };
export const MAX_MODELOS_SALVOS = 5;

/** Personalização (cores, tipografia, cartões, seleção, identidade da loja e PDF). Tudo opcional no Firestore. */
export function criarPersonalizacaoPadrao() {
  const look = MODELOS.padrao.look;
  return {
    tema: { modelo: "padrao", cores: {}, tipografia: { fonte: look.fonte, pesoTitulo: 800, tamanho: 100 } },
    produtos: {
      colunasPc: look.colunasPc,
      colunasCelular: look.colunasCelular,
      espacamento: look.espacamento,
      raio: look.raio,
      borda: look.borda,
      bordaLargura: look.bordaLargura,
      proporcao: look.proporcao,
      alinhamento: look.alinhamento,
      sombraForca: look.sombraForca,
      porPagina: 0,
      precoPosicao: "abaixo",
      destacarPromocao: true,
      mostrar: { foto: true, categoria: true, codigo: true, nome: true, descricao: false, cores: true, tamanhos: true, preco: true, botao: true },
      ordemInfo: [...INFO_PRODUTO]
    },
    margens: { externa: 24, interna: 16 },
    selecao: { modo: "todos", categorias: [], produtos: [], ordem: "padrao", ordemManual: [] },
    loja: {
      nome: "", apresentacao: "", logoUrl: "", logoPath: "", logoAltura: 32, mostrarLogo: true, mostrarNome: false,
      alinhamento: "esquerda", telefone: "", whatsapp: "", instagram: "", facebook: "", endereco: "", email: "",
      mostrarTelefone: true, mostrarWhatsapp: true, mostrarRedes: true, mostrarEndereco: true, mostrarEmail: false
    },
    rodape: { texto: "", mostrarContato: true, mostrarCopyright: true },
    textos: {
      anuncio: "FRETE GRÁTIS para retirada no balcão da confecção • Peças com pronta-entrega.",
      eyebrow: "Coleção 2026 • Fabricação Própria",
      titulo: "Moda & Coleção Surfwear"
    },
    pdf: { formato: "a4", orientacao: "retrato", margemMm: 10, colunas: 3, linhas: 3, capa: true, cabecalhoPagina: true, rodapePagina: true, numeracao: true, tabela: "varejo" },
    modelosSalvos: []
  };
}

export const VITRINE_PADRAO = criarVitrinePadrao();

function escolher(opcoes, valor, padrao) {
  return opcoes.includes(valor) ? valor : padrao;
}

function numero(valor, min, max, padrao, casas = 2) {
  const n = typeof valor === "number" ? valor : Number(valor);
  if (!Number.isFinite(n)) return padrao;
  const f = 10 ** casas;
  return Math.round(Math.min(max, Math.max(min, n)) * f) / f;
}

export function limparTexto(valor, max) {
  return String(valor ?? "")
    .replace(/\p{Cc}/gu, " ")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, max);
}

/** Só aceita imagem do Storage deste projeto ou https; bloqueia javascript:, data: gigante etc. */
function urlImagemSegura(valor) {
  const url = String(valor ?? "").trim();
  if (!url) return "";
  return /^https:\/\/[^\s"'<>]{1,1500}$/.test(url) ? url : "";
}

export function sanitizarBanner(raw, indice = 0) {
  const base = criarVitrinePadrao().banners[0];
  const r = raw && typeof raw === "object" ? raw : {};
  const id = typeof r.id === "string" && /^[a-z0-9-]{1,40}$/.test(r.id) ? r.id : `slide-${indice + 1}`;
  return {
    id,
    badge: limparTexto(r.badge, 40),
    title: limparTexto(r.title, 60) || base.title,
    highlight: limparTexto(r.highlight, 30),
    subtitle: limparTexto(r.subtitle, 160),
    ctaLabel: limparTexto(r.ctaLabel, 30) || base.ctaLabel,
    ctaDestino: escolher(DESTINOS_BOTAO, r.ctaDestino, "vitrine"),
    ctaCategoria: limparTexto(r.ctaCategoria, 40),
    image: urlImagemSegura(r.image),
    imagePath: typeof r.imagePath === "string" ? r.imagePath.slice(0, 300) : "",
    focusX: numero(r.focusX, 0, 100, ENQUADRAMENTO_PADRAO.focusX),
    focusY: numero(r.focusY, 0, 100, ENQUADRAMENTO_PADRAO.focusY),
    zoom: numero(r.zoom, 1, MAX_ZOOM, ENQUADRAMENTO_PADRAO.zoom)
  };
}

/**
 * Normaliza a configuração vinda do Firestore ou do iframe de pré-visualização: ignora blocos
 * desconhecidos/repetidos, troca valores inválidos pelo padrão e acrescenta os blocos que faltam.
 */
export function sanitizarVitrine(raw) {
  const padrao = criarVitrinePadrao();
  const r = raw && typeof raw === "object" ? raw : {};

  const vistos = new Set();
  const blocks = [];
  const entrada = Array.isArray(r.layout?.blocks) ? r.layout.blocks : [];
  for (const item of entrada) {
    if (!item || typeof item !== "object" || !BLOCK_IDS.includes(item.id) || vistos.has(item.id)) continue;
    vistos.add(item.id);
    const base = padrao.layout.blocks.find((b) => b.id === item.id);
    blocks.push({
      id: item.id,
      visible: typeof item.visible === "boolean" ? item.visible : base.visible,
      width: escolher(WIDTHS, item.width, base.width),
      spacing: escolher(SPACINGS, item.spacing, base.spacing),
      align: escolher(ALIGNS, item.align, base.align),
      imageSide: escolher(IMAGE_SIDES, item.imageSide, base.imageSide)
    });
  }
  for (const base of padrao.layout.blocks) if (!vistos.has(base.id)) blocks.push({ ...base });

  let banners = padrao.banners;
  if (Array.isArray(r.banners)) {
    const ids = new Set();
    banners = r.banners
      .slice(0, MAX_SLIDES)
      .map((b, i) => sanitizarBanner(b, i))
      .filter((b) => (ids.has(b.id) ? false : (ids.add(b.id), true)));
  }

  return {
    layout: { blocks },
    banners,
    estilo: { vidro: typeof r.estilo?.vidro === "boolean" ? r.estilo.vidro : padrao.estilo.vidro },
    atacado: { minPecas: Math.round(numero(r.atacado?.minPecas, 1, 1000, ATACADO_MIN_PADRAO, 0)) },
    carrossel: {
      intervaloSeg: Math.round(numero(r.carrossel?.intervaloSeg, 5, 120, INTERVALO_PADRAO_SEG, 0)),
      novidadesQtd: Math.round(numero(r.carrossel?.novidadesQtd, 3, 16, 8, 0))
    },
    ...sanitizarPersonalizacao(r, padrao)
  };
}

const bool = (v, padrao) => (typeof v === "boolean" ? v : padrao);
const inteiro = (v, min, max, padrao) => Math.round(numero(v, min, max, padrao, 0));
const listaIds = (v, max) =>
  Array.isArray(v) ? [...new Set(v.filter((x) => typeof x === "string" && x.length > 0 && x.length <= 120))].slice(0, max) : [];

/** Só guarda cores válidas (#rrggbb) de elementos conhecidos; o resto cai no padrão do modelo. */
export function sanitizarCores(raw) {
  const saida = {};
  if (!raw || typeof raw !== "object") return saida;
  CHAVES_COR.forEach(({ id }) => {
    const hex = normalizarHex(raw[id]);
    if (hex) saida[id] = hex;
  });
  return saida;
}

function sanitizarPersonalizacao(r, padrao) {
  const t = r.tema && typeof r.tema === "object" ? r.tema : {};
  const tip = t.tipografia && typeof t.tipografia === "object" ? t.tipografia : {};
  const p = r.produtos && typeof r.produtos === "object" ? r.produtos : {};
  const pm = p.mostrar && typeof p.mostrar === "object" ? p.mostrar : {};
  const base = padrao.produtos;

  // Ordem das informações: só ids conhecidos, sem repetir, e acrescenta os que faltarem no fim
  const ordemEntrada = Array.isArray(p.ordemInfo) ? p.ordemInfo.filter((x) => INFO_PRODUTO.includes(x)) : [];
  const ordemInfo = [...new Set([...ordemEntrada, ...INFO_PRODUTO])];

  const mostrar = {};
  MOSTRAR_PRODUTO.forEach((k) => {
    mostrar[k] = bool(pm[k], base.mostrar[k]);
  });

  const lj = r.loja && typeof r.loja === "object" ? r.loja : {};
  const bl = padrao.loja;
  const ft = r.rodape && typeof r.rodape === "object" ? r.rodape : {};
  const tx = r.textos && typeof r.textos === "object" ? r.textos : {};
  const sel = r.selecao && typeof r.selecao === "object" ? r.selecao : {};
  const pd = r.pdf && typeof r.pdf === "object" ? r.pdf : {};
  const mg = r.margens && typeof r.margens === "object" ? r.margens : {};

  return {
    tema: {
      modelo: escolher(IDS_MODELO, t.modelo, "padrao"),
      cores: sanitizarCores(t.cores),
      tipografia: {
        fonte: escolher(IDS_FONTE, tip.fonte, MODELOS[escolher(IDS_MODELO, t.modelo, "padrao")].look.fonte),
        pesoTitulo: escolher([400, 500, 600, 700, 800], Number(tip.pesoTitulo), 800),
        tamanho: inteiro(tip.tamanho, 85, 130, 100)
      }
    },
    produtos: {
      colunasPc: inteiro(p.colunasPc, 2, 6, base.colunasPc),
      colunasCelular: inteiro(p.colunasCelular, 1, 2, base.colunasCelular),
      espacamento: inteiro(p.espacamento, 4, 48, base.espacamento),
      raio: inteiro(p.raio, 0, 32, base.raio),
      borda: escolher(["nenhuma", "solida", "tracejada"], p.borda, base.borda),
      bordaLargura: inteiro(p.bordaLargura, 1, 4, base.bordaLargura),
      proporcao: escolher(Object.keys(PROPORCOES), p.proporcao, base.proporcao),
      alinhamento: escolher(ALIGNS, p.alinhamento, base.alinhamento),
      sombraForca: inteiro(p.sombraForca, 0, 100, base.sombraForca),
      porPagina: p.porPagina === 0 || p.porPagina === "0" ? 0 : inteiro(p.porPagina, 4, 60, 0),
      precoPosicao: escolher(["abaixo", "foto"], p.precoPosicao, base.precoPosicao),
      destacarPromocao: bool(p.destacarPromocao, base.destacarPromocao),
      mostrar,
      ordemInfo
    },
    margens: { externa: inteiro(mg.externa, 0, 64, 24), interna: inteiro(mg.interna, 0, 48, 16) },
    selecao: {
      modo: escolher(MODOS_SELECAO, sel.modo, "todos"),
      categorias: listaIds(sel.categorias, 100),
      produtos: listaIds(sel.produtos, 2000),
      ordem: escolher(ORDENS_PRODUTO, sel.ordem, "padrao"),
      ordemManual: listaIds(sel.ordemManual, 2000)
    },
    loja: {
      nome: limparTexto(lj.nome, 80),
      apresentacao: limparTexto(lj.apresentacao, 300),
      logoUrl: urlImagemSegura(lj.logoUrl),
      logoPath: typeof lj.logoPath === "string" ? lj.logoPath.slice(0, 300) : "",
      logoAltura: inteiro(lj.logoAltura, 20, 96, bl.logoAltura),
      mostrarLogo: bool(lj.mostrarLogo, bl.mostrarLogo),
      mostrarNome: bool(lj.mostrarNome, bl.mostrarNome),
      alinhamento: escolher(ALIGNS, lj.alinhamento, bl.alinhamento),
      telefone: limparTexto(lj.telefone, 30),
      whatsapp: limparTexto(lj.whatsapp, 30),
      instagram: limparTexto(lj.instagram, 60),
      facebook: limparTexto(lj.facebook, 60),
      endereco: limparTexto(lj.endereco, 160),
      email: limparTexto(lj.email, 80),
      mostrarTelefone: bool(lj.mostrarTelefone, bl.mostrarTelefone),
      mostrarWhatsapp: bool(lj.mostrarWhatsapp, bl.mostrarWhatsapp),
      mostrarRedes: bool(lj.mostrarRedes, bl.mostrarRedes),
      mostrarEndereco: bool(lj.mostrarEndereco, bl.mostrarEndereco),
      mostrarEmail: bool(lj.mostrarEmail, bl.mostrarEmail)
    },
    rodape: {
      texto: limparTexto(ft.texto, 300),
      mostrarContato: bool(ft.mostrarContato, padrao.rodape.mostrarContato),
      mostrarCopyright: bool(ft.mostrarCopyright, padrao.rodape.mostrarCopyright)
    },
    textos: {
      anuncio: typeof tx.anuncio === "string" ? limparTexto(tx.anuncio, 160) : padrao.textos.anuncio,
      eyebrow: typeof tx.eyebrow === "string" ? limparTexto(tx.eyebrow, 60) : padrao.textos.eyebrow,
      titulo: typeof tx.titulo === "string" ? limparTexto(tx.titulo, 80) : padrao.textos.titulo
    },
    pdf: {
      formato: escolher(Object.keys(FORMATOS_PDF), pd.formato, "a4"),
      orientacao: escolher(["retrato", "paisagem"], pd.orientacao, "retrato"),
      margemMm: inteiro(pd.margemMm, 5, 25, 10),
      colunas: inteiro(pd.colunas, 1, 5, 3),
      linhas: inteiro(pd.linhas, 1, 6, 3),
      capa: bool(pd.capa, true),
      cabecalhoPagina: bool(pd.cabecalhoPagina, true),
      rodapePagina: bool(pd.rodapePagina, true),
      numeracao: bool(pd.numeracao, true),
      tabela: escolher(["varejo", "atacado"], pd.tabela, "varejo")
    },
    modelosSalvos: sanitizarModelosSalvos(r.modelosSalvos)
  };
}

/** Parte visual de uma configuração: o que "Meus modelos" guarda e o que um modelo predefinido define. */
export function extrairLook(v) {
  const p = v.produtos;
  return {
    vidro: v.estilo.vidro,
    tema: { modelo: v.tema.modelo, cores: { ...v.tema.cores }, tipografia: { ...v.tema.tipografia } },
    produtos: {
      colunasPc: p.colunasPc, colunasCelular: p.colunasCelular, espacamento: p.espacamento, raio: p.raio, borda: p.borda,
      bordaLargura: p.bordaLargura, proporcao: p.proporcao, alinhamento: p.alinhamento, sombraForca: p.sombraForca,
      precoPosicao: p.precoPosicao, destacarPromocao: p.destacarPromocao, mostrar: { ...p.mostrar }, ordemInfo: [...p.ordemInfo]
    },
    margens: { ...v.margens }
  };
}

function sanitizarModelosSalvos(raw) {
  if (!Array.isArray(raw)) return [];
  const ids = new Set();
  const saida = [];
  for (const m of raw.slice(0, MAX_MODELOS_SALVOS)) {
    if (!m || typeof m !== "object") continue;
    const id = typeof m.id === "string" && /^[a-z0-9-]{1,40}$/.test(m.id) ? m.id : null;
    const nome = limparTexto(m.nome, 40);
    if (!id || !nome || ids.has(id)) continue;
    ids.add(id);
    // Reaproveita a sanitização completa para garantir valores válidos
    const v = sanitizarVitrine({ estilo: { vidro: m.vidro }, tema: m.tema, produtos: m.produtos, margens: m.margens });
    saida.push({ id, nome, vidro: v.estilo.vidro, tema: v.tema, produtos: extrairLook(v).produtos, margens: v.margens });
  }
  return saida;
}

/** Aplica um modelo predefinido: troca o visual (cores, fonte, cartões) e mantém textos, banners, seleção e PDF. */
export function aplicarModelo(vitrine, idModelo) {
  const m = MODELOS[idModelo];
  if (!m) return vitrine;
  const l = m.look;
  return {
    ...vitrine,
    estilo: { ...vitrine.estilo, vidro: l.vidro },
    tema: { modelo: idModelo, cores: {}, tipografia: { fonte: l.fonte, pesoTitulo: vitrine.tema.tipografia.pesoTitulo, tamanho: vitrine.tema.tipografia.tamanho } },
    produtos: {
      ...vitrine.produtos,
      colunasPc: l.colunasPc, colunasCelular: l.colunasCelular, espacamento: l.espacamento, raio: l.raio, borda: l.borda,
      bordaLargura: l.bordaLargura, proporcao: l.proporcao, alinhamento: l.alinhamento, sombraForca: l.sombraForca
    }
  };
}

/** Aplica um modelo salvo pelo usuário (mesmas regras de aplicarModelo). */
export function aplicarModeloSalvo(vitrine, salvo) {
  return {
    ...vitrine,
    estilo: { ...vitrine.estilo, vidro: salvo.vidro },
    tema: { modelo: salvo.tema.modelo, cores: { ...salvo.tema.cores }, tipografia: { ...salvo.tema.tipografia } },
    produtos: { ...vitrine.produtos, ...salvo.produtos, mostrar: { ...salvo.produtos.mostrar }, ordemInfo: [...salvo.produtos.ordemInfo] },
    margens: { ...salvo.margens }
  };
}

/** Erros que impedem salvar (a sanitização conserta o resto em silêncio). */
export function validarVitrine(vitrine) {
  if (!vitrine.banners.length) return "O carrossel precisa de pelo menos 1 slide.";
  for (const [i, b] of vitrine.banners.entries()) {
    if (limparTexto(b.title, 60).length < 2) return `Slide ${i + 1}: o título precisa ter pelo menos 2 caracteres.`;
    if (limparTexto(b.ctaLabel, 30).length < 2) return `Slide ${i + 1}: informe o texto do botão.`;
    if (b.ctaDestino === "categoria" && !limparTexto(b.ctaCategoria, 40)) return `Slide ${i + 1}: escolha a categoria do botão.`;
  }
  return null;
}

/** Publica a configuração: o Nexus LS guarda no computador, envia as imagens e grava no catálogo online. Falhas são propagadas. */
export async function salvarVitrine(tenantId, vitrine) {
  const limpa = sanitizarVitrine(vitrine);
  const erro = validarVitrine(limpa);
  if (erro) throw new Error(erro);
  const res = await chamar("publicarVitrine", { vitrine: limpa });
  return sanitizarVitrine(res?.vitrine ?? limpa);
}

/* ------------------------------------------------------------------ */
/* Produtos: fotos e novidades                                         */
/* ------------------------------------------------------------------ */

/** Foto principal + fotos extras (sem repetir), no máximo 4. A principal é a mesma do PDV/estoque. */
export function fotosDoProduto(produto) {
  const principal = getProductImageUrl(produto) || "";
  const extras = Array.isArray(produto?.fotosExtras)
    ? produto.fotosExtras.map((f) => (typeof f === "string" ? f : f?.url)).filter(Boolean)
    : [];
  return [...new Set([principal, ...extras].filter(Boolean))].slice(0, MAX_FOTOS_PRODUTO);
}

function criadoEmMs(produto) {
  const t = produto?.criadoEm;
  if (!t) return 0;
  if (typeof t.toMillis === "function") return t.toMillis();
  if (typeof t.seconds === "number") return t.seconds * 1000;
  const d = new Date(t).getTime();
  return Number.isFinite(d) ? d : 0;
}

/** Novidades: peças em destaque primeiro, depois as cadastradas mais recentemente. */
export function produtosNovidades(produtos, quantidade = 8) {
  return [...produtos]
    .sort((a, b) => Number(Boolean(b.destaque)) - Number(Boolean(a.destaque)) || criadoEmMs(b) - criadoEmMs(a))
    .slice(0, quantidade);
}

/** Enquadramento da imagem do slide: ponto focal em % e zoom (ajustados no Editor visual). */
export function estiloEnquadramento(slide) {
  return {
    objectFit: "cover",
    objectPosition: `${slide.focusX}% ${slide.focusY}%`,
    transform: `scale(${slide.zoom})`,
    transformOrigin: `${slide.focusX}% ${slide.focusY}%`
  };
}

/** Duas fotos de produtos novos para a colagem do slide que não tem imagem própria. */
export function colagemDoSlide(produtos) {
  const novos = produtosNovidades(produtos, 4);
  const a = novos[0] && fotosDoProduto(novos[0])[0];
  const b = novos[1] ? fotosDoProduto(novos[1])[1] || fotosDoProduto(novos[1])[0] : undefined;
  return { a, b };
}

/** Preço de atacado só vale se existir e for menor que o varejo. */
export function temPrecoAtacado(produto) {
  const v = Number(produto?.precoVarejo) || 0;
  const a = Number(produto?.precoAtacado) || 0;
  return a > 0 && a < v;
}

/* ------------------------------------------------------------------ */
/* Preços e seleção de produtos do catálogo                            */
/* ------------------------------------------------------------------ */

/** Preço promocional válido: maior que zero e menor que o preço de varejo. */
export function precoPromocional(produto) {
  const promo = Number(produto?.precoPromocional) || 0;
  const varejo = Number(produto?.precoVarejo) || 0;
  return promo > 0 && promo < varejo ? promo : 0;
}

/** Preço de varejo que o cliente paga (promocional quando existir). */
export function precoVarejoEfetivo(produto) {
  return precoPromocional(produto) || Number(produto?.precoVarejo) || 0;
}

/**
 * Aplica a escolha do editor: quais produtos entram (todos, categorias ou produtos específicos) e a ordem.
 * Só usa produtos reais recebidos; ids que não existem mais são ignorados.
 */
export function selecionarProdutos(produtos = [], selecao = {}) {
  let lista = produtos;
  if (selecao.modo === "categorias" && selecao.categorias?.length) {
    const cats = new Set(selecao.categorias);
    lista = produtos.filter((p) => cats.has((p.categoria || "").trim()));
  } else if (selecao.modo === "produtos" && selecao.produtos?.length) {
    const ids = new Set(selecao.produtos);
    lista = produtos.filter((p) => ids.has(p.id));
  }
  const preco = (p) => precoVarejoEfetivo(p);
  const ordenar = (cmp) => [...lista].sort(cmp);
  switch (selecao.ordem) {
    case "nome":
      return ordenar((a, b) => String(a.nome || "").localeCompare(String(b.nome || ""), "pt-BR"));
    case "menorPreco":
      return ordenar((a, b) => preco(a) - preco(b));
    case "maiorPreco":
      return ordenar((a, b) => preco(b) - preco(a));
    case "recentes":
      return ordenar((a, b) => (Number(b.criadoEm?.seconds) || 0) - (Number(a.criadoEm?.seconds) || 0));
    case "manual": {
      const pos = new Map((selecao.ordemManual || []).map((id, i) => [id, i]));
      return ordenar((a, b) => (pos.has(a.id) ? pos.get(a.id) : 1e9) - (pos.has(b.id) ? pos.get(b.id) : 1e9));
    }
    default:
      return lista;
  }
}

/** Preço que o cliente vê e paga, na tabela escolhida (varejo com promoção, ou atacado). */
export function precosDoCartao(produto, tipoTabela) {
  const varejo = Number(produto.precoVarejo) || 0;
  const atacadoValor = Number(produto.precoAtacado) || varejo;
  const promo = precoPromocional(produto);
  const atacado = tipoTabela === "atacado";
  return {
    varejo,
    atacadoValor,
    promo,
    atacado,
    comAtacado: temPrecoAtacado(produto),
    // No atacado vale o preço de atacado; no varejo, o promocional (se houver)
    final: atacado ? atacadoValor : promo || varejo,
    riscado: atacado ? (temPrecoAtacado(produto) ? varejo : 0) : promo ? varejo : 0,
    descontoPromo: !atacado && promo ? Math.round(((varejo - promo) / varejo) * 100) : 0,
    descontoAtacado: temPrecoAtacado(produto) ? Math.round(((varejo - atacadoValor) / varejo) * 100) : 0
  };
}

/* ------------------------------------------------------------------ */
/* Contatos da loja e medidas do catálogo em PDF                       */
/* ------------------------------------------------------------------ */

/** Dados da loja para cabeçalho, rodapé e PDF: o que foi personalizado no editor ou o cadastro da empresa. */
export function contatoDaLoja(vitrine, empresa = {}) {
  const l = vitrine.loja;
  const e = empresa || {};
  return {
    nome: l.nome || e.nome || "",
    telefone: l.telefone || e.telefone || "",
    whatsapp: l.whatsapp || e.whatsapp || "",
    instagram: l.instagram || e.instagram || "",
    facebook: l.facebook || e.facebook || "",
    email: l.email || e.email || "",
    endereco: l.endereco || [e.endereco, e.bairro, e.cidade].filter(Boolean).join(", "),
    apresentacao: l.apresentacao || e.mensagemCatalogo || ""
  };
}

/** Linha de contatos respeitando o que o editor mandou mostrar. */
export function linhaDeContatos(vitrine, empresa) {
  const l = vitrine.loja;
  const c = contatoDaLoja(vitrine, empresa);
  return [
    l.mostrarEndereco && c.endereco,
    l.mostrarTelefone && c.telefone && `Tel: ${c.telefone}`,
    l.mostrarWhatsapp && c.whatsapp && `WhatsApp: ${c.whatsapp}`,
    l.mostrarEmail && c.email,
    l.mostrarRedes && c.instagram && `Instagram: ${c.instagram}`,
    l.mostrarRedes && c.facebook && `Facebook: ${c.facebook}`
  ]
    .filter(Boolean)
    .join(" • ");
}

const ALTURA_FAIXA_MM = 9;

/** Medidas em milímetros da página e da grade de produtos do catálogo em PDF. */
export function medidasPdf(pdf, vitrine) {
  const [a, b] = FORMATOS_PDF[pdf.formato]?.mm || FORMATOS_PDF.a4.mm;
  const paisagem = pdf.orientacao === "paisagem";
  const largura = paisagem ? b : a;
  const altura = paisagem ? a : b;
  const margem = pdf.margemMm;
  const cabecalho = pdf.cabecalhoPagina ? ALTURA_FAIXA_MM : 0;
  const rodape = pdf.rodapePagina ? ALTURA_FAIXA_MM : 0;
  const larguraUtil = largura - margem * 2;
  const alturaGrade = altura - margem * 2 - cabecalho - rodape;
  const gap = Math.max(2, Math.round((vitrine.produtos.espacamento / 96) * 25.4 * 10) / 10);
  const celulaL = (larguraUtil - gap * (pdf.colunas - 1)) / pdf.colunas;
  const celulaA = (alturaGrade - gap * (pdf.linhas - 1)) / pdf.linhas;
  // Altura que as informações ocupam abaixo da foto (estimativa por linha de texto visível)
  const m = vitrine.produtos.mostrar;
  const infoMm =
    6 +
    ((m.categoria || m.codigo) ? 4.5 : 0) +
    (m.nome ? 9 : 0) +
    (m.descricao ? 8 : 0) +
    (m.cores ? 4.5 : 0) +
    (m.tamanhos ? 5 : 0) +
    (m.preco ? 9 : 0);
  const alturaFoto = m.foto ? Math.max(8, celulaA - infoMm) : 0;
  return {
    largura, altura, margem, cabecalho, rodape, larguraUtil, alturaGrade, gap, celulaL, celulaA, infoMm, alturaFoto,
    proporcaoFoto: alturaFoto ? celulaL / alturaFoto : 1,
    porPagina: pdf.colunas * pdf.linhas
  };
}

/** Quantidade de páginas: capa (se ligada) + páginas de produtos (mínimo 1). */
export function totalPaginasPdf(pdf, totalProdutos) {
  const porPagina = pdf.colunas * pdf.linhas;
  return (pdf.capa ? 1 : 0) + Math.max(1, Math.ceil(totalProdutos / porPagina));
}
