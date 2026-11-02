/**
 * TEMA DA VITRINE: cores, modelos, tipografia e variáveis CSS (módulo puro).
 *
 * Como funciona: cada elemento tem uma cor com valor padrão (do modelo escolhido). O usuário só
 * grava as cores que MUDOU (`tema.cores`); cor ausente = cor do modelo ("restaurar cor original").
 * A vitrine, a pré-visualização e o catálogo em PDF usam as mesmas variáveis `--vt-*`, por isso o que
 * aparece na tela é o que sai no PDF.
 */

/* ------------------------------------------------------------------ */
/* Cores editáveis                                                     */
/* ------------------------------------------------------------------ */

export const CHAVES_COR = [
  { id: "fundo", rotulo: "Fundo geral do catálogo", grupo: "Página" },
  { id: "cartao", rotulo: "Fundo dos produtos e cartões", grupo: "Página" },
  { id: "titulo", rotulo: "Títulos", grupo: "Textos" },
  { id: "subtitulo", rotulo: "Subtítulos", grupo: "Textos" },
  { id: "loja", rotulo: "Nome da loja", grupo: "Textos" },
  { id: "nomeProduto", rotulo: "Nome do produto", grupo: "Produtos" },
  { id: "descricao", rotulo: "Descrição do produto", grupo: "Produtos" },
  { id: "codigo", rotulo: "Código do produto", grupo: "Produtos" },
  { id: "preco", rotulo: "Preço normal", grupo: "Produtos" },
  { id: "promo", rotulo: "Preço promocional", grupo: "Produtos" },
  { id: "botao", rotulo: "Botões", grupo: "Botões e destaques" },
  { id: "botaoTexto", rotulo: "Texto dos botões", grupo: "Botões e destaques" },
  { id: "etiqueta", rotulo: "Etiquetas (Novo, Promoção)", grupo: "Botões e destaques" },
  { id: "etiquetaTexto", rotulo: "Texto das etiquetas", grupo: "Botões e destaques" },
  { id: "destaque", rotulo: "Destaques e detalhes", grupo: "Botões e destaques" },
  { id: "borda", rotulo: "Bordas", grupo: "Linhas e sombras" },
  { id: "divisoria", rotulo: "Divisórias", grupo: "Linhas e sombras" },
  { id: "sombra", rotulo: "Sombras", grupo: "Linhas e sombras" },
  { id: "cabecalho", rotulo: "Fundo do cabeçalho", grupo: "Cabeçalho e rodapé" },
  { id: "cabecalhoTexto", rotulo: "Texto do cabeçalho", grupo: "Cabeçalho e rodapé" },
  { id: "rodape", rotulo: "Fundo do rodapé", grupo: "Cabeçalho e rodapé" },
  { id: "rodapeTexto", rotulo: "Texto do rodapé", grupo: "Cabeçalho e rodapé" }
];

export const GRUPOS_COR = [...new Set(CHAVES_COR.map((c) => c.grupo))];

const IDS_COR = CHAVES_COR.map((c) => c.id);

/** Aceita #RGB ou #RRGGBB (com ou sem #) e devolve #rrggbb minúsculo; senão, null. */
export function normalizarHex(valor) {
  const s = String(valor ?? "").trim().replace(/^#/, "");
  if (/^[0-9a-fA-F]{3}$/.test(s)) return `#${s[0]}${s[0]}${s[1]}${s[1]}${s[2]}${s[2]}`.toLowerCase();
  if (/^[0-9a-fA-F]{6}$/.test(s)) return `#${s}`.toLowerCase();
  return null;
}

export function hexParaRgb(hex) {
  const h = normalizarHex(hex);
  if (!h) return null;
  return { r: parseInt(h.slice(1, 3), 16), g: parseInt(h.slice(3, 5), 16), b: parseInt(h.slice(5, 7), 16) };
}

export function rgbParaHex({ r, g, b }) {
  const c = (n) => Math.max(0, Math.min(255, Math.round(Number(n) || 0))).toString(16).padStart(2, "0");
  return `#${c(r)}${c(g)}${c(b)}`;
}

/** Contraste WCAG entre duas cores (1 a 21). Texto normal precisa de pelo menos 4,5. */
export function contraste(hexA, hexB) {
  const a = hexParaRgb(hexA);
  const b = hexParaRgb(hexB);
  if (!a || !b) return null;
  const lum = ({ r, g, b: azul }) => {
    const f = (v) => {
      const s = v / 255;
      return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(azul);
  };
  const [l1, l2] = [lum(a), lum(b)].sort((x, y) => y - x);
  return Math.round(((l1 + 0.05) / (l2 + 0.05)) * 100) / 100;
}

/** Preto ou branco, o que tiver mais contraste com o fundo informado. */
export function textoLegivelSobre(fundoHex) {
  const preto = contraste("#111111", fundoHex) ?? 0;
  const branco = contraste("#ffffff", fundoHex) ?? 0;
  return preto >= branco ? "#111111" : "#ffffff";
}

/* ------------------------------------------------------------------ */
/* Modelos (pontos de partida)                                         */
/* ------------------------------------------------------------------ */

const BASE_CLARA = {
  fundo: "#fafafa", cartao: "#ffffff", titulo: "#171717", subtitulo: "#525252", loja: "#171717",
  nomeProduto: "#171717", descricao: "#525252", codigo: "#737373", preco: "#171717", promo: "#dc2626",
  botao: "#171717", botaoTexto: "#ffffff", etiqueta: "#171717", etiquetaTexto: "#ffffff", destaque: "#171717",
  borda: "#e5e5e5", divisoria: "#e5e5e5", sombra: "#171717",
  cabecalho: "#ffffff", cabecalhoTexto: "#171717", rodape: "#ffffff", rodapeTexto: "#737373"
};

/** `look`: parte visual do layout que o modelo define (pode ser trocada depois, item por item). */
export const MODELOS = {
  padrao: {
    rotulo: "Atual (vidro)",
    descricao: "O visual que o catálogo já tem: cartões de vidro sobre fundo cinza suave.",
    cores: BASE_CLARA,
    look: { vidro: true, fonte: "sistema", colunasPc: 4, colunasCelular: 2, espacamento: 20, raio: 16, borda: "solida", bordaLargura: 1, proporcao: "3/4", alinhamento: "esquerda", sombraForca: 40 },
    fundoImagem: true
  },
  minimalista: {
    rotulo: "Minimalista",
    descricao: "Branco, linhas finas e muito respiro. Deixa a foto aparecer.",
    cores: { ...BASE_CLARA, fundo: "#ffffff", cartao: "#ffffff", borda: "#ececec", divisoria: "#ececec", subtitulo: "#6b6b6b", descricao: "#6b6b6b" },
    look: { vidro: false, fonte: "moderna", colunasPc: 4, colunasCelular: 2, espacamento: 28, raio: 2, borda: "solida", bordaLargura: 1, proporcao: "4/5", alinhamento: "esquerda", sombraForca: 0 }
  },
  elegante: {
    rotulo: "Elegante",
    descricao: "Tons de areia, letra com serifa e acabamento discreto em dourado.",
    cores: {
      ...BASE_CLARA, fundo: "#f6f2ea", cartao: "#fffdf8", titulo: "#2b2118", subtitulo: "#6f6253", loja: "#2b2118",
      nomeProduto: "#2b2118", descricao: "#6f6253", codigo: "#6f6253", preco: "#7a5a1f", promo: "#9b2c2c",
      botao: "#2b2118", botaoTexto: "#fffdf8", etiqueta: "#7a5a1f", etiquetaTexto: "#fffdf8", destaque: "#b08d4a",
      borda: "#d9ccb4", divisoria: "#d9ccb4", sombra: "#2b2118", cabecalho: "#fffdf8", cabecalhoTexto: "#2b2118",
      rodape: "#2b2118", rodapeTexto: "#e9dfcc"
    },
    look: { vidro: false, fonte: "elegante", colunasPc: 3, colunasCelular: 2, espacamento: 32, raio: 4, borda: "solida", bordaLargura: 1, proporcao: "3/4", alinhamento: "centro", sombraForca: 15 }
  },
  comercial: {
    rotulo: "Comercial",
    descricao: "Direto ao ponto: mais produtos por linha, preço em evidência e botão azul.",
    cores: {
      ...BASE_CLARA, fundo: "#f1f4f8", cartao: "#ffffff", titulo: "#0f172a", subtitulo: "#475569", loja: "#0f172a",
      nomeProduto: "#0f172a", descricao: "#475569", codigo: "#64748b", preco: "#0f172a", promo: "#dc2626",
      botao: "#1d4ed8", botaoTexto: "#ffffff", etiqueta: "#facc15", etiquetaTexto: "#1f2937", destaque: "#1d4ed8",
      borda: "#d5dce6", divisoria: "#d5dce6", sombra: "#0f172a", cabecalho: "#ffffff", cabecalhoTexto: "#0f172a",
      rodape: "#0f172a", rodapeTexto: "#cbd5e1"
    },
    look: { vidro: false, fonte: "moderna", colunasPc: 5, colunasCelular: 2, espacamento: 14, raio: 8, borda: "solida", bordaLargura: 1, proporcao: "1/1", alinhamento: "esquerda", sombraForca: 25 }
  },
  moderno: {
    rotulo: "Moderno",
    descricao: "Fundo escuro, cartões em grafite e destaque em verde-água.",
    cores: {
      ...BASE_CLARA, fundo: "#0d1117", cartao: "#161b22", titulo: "#f0f6fc", subtitulo: "#9aa7b4", loja: "#f0f6fc",
      nomeProduto: "#f0f6fc", descricao: "#9aa7b4", codigo: "#7d8a98", preco: "#f0f6fc", promo: "#ff7b72",
      botao: "#2dd4bf", botaoTexto: "#06201c", etiqueta: "#2dd4bf", etiquetaTexto: "#06201c", destaque: "#2dd4bf",
      borda: "#2b3340", divisoria: "#2b3340", sombra: "#000000", cabecalho: "#0d1117", cabecalhoTexto: "#f0f6fc",
      rodape: "#0d1117", rodapeTexto: "#9aa7b4"
    },
    look: { vidro: false, fonte: "moderna", colunasPc: 4, colunasCelular: 2, espacamento: 20, raio: 20, borda: "solida", bordaLargura: 1, proporcao: "3/4", alinhamento: "esquerda", sombraForca: 55 }
  },
  promocional: {
    rotulo: "Promocional",
    descricao: "Quente e chamativo: laranja, preço promocional grande e etiquetas amarelas.",
    cores: {
      ...BASE_CLARA, fundo: "#fff7ed", cartao: "#ffffff", titulo: "#7c2d12", subtitulo: "#9a3412", loja: "#7c2d12",
      nomeProduto: "#1c1917", descricao: "#57534e", codigo: "#78716c", preco: "#1c1917", promo: "#dc2626",
      botao: "#c2410c", botaoTexto: "#ffffff", etiqueta: "#fde047", etiquetaTexto: "#713f12", destaque: "#c2410c",
      borda: "#fed7aa", divisoria: "#fed7aa", sombra: "#7c2d12", cabecalho: "#c2410c", cabecalhoTexto: "#ffffff",
      rodape: "#7c2d12", rodapeTexto: "#ffedd5"
    },
    look: { vidro: false, fonte: "arredondada", colunasPc: 4, colunasCelular: 2, espacamento: 16, raio: 14, borda: "solida", bordaLargura: 2, proporcao: "1/1", alinhamento: "centro", sombraForca: 30 }
  }
};

export const IDS_MODELO = Object.keys(MODELOS);

/** Cor final de um elemento: a escolhida pelo usuário ou a do modelo. */
export function corEfetiva(tema, id) {
  return tema?.cores?.[id] || MODELOS[tema?.modelo]?.cores?.[id] || MODELOS.padrao.cores[id];
}

export function coresEfetivas(tema) {
  return Object.fromEntries(IDS_COR.map((id) => [id, corEfetiva(tema, id)]));
}

/* ------------------------------------------------------------------ */
/* Tipografia                                                          */
/* ------------------------------------------------------------------ */

export const FONTES = {
  sistema: { rotulo: "Padrão do sistema", pilha: "inherit" },
  moderna: { rotulo: "Moderna (sem serifa)", pilha: "'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif" },
  classica: { rotulo: "Clássica (serifada)", pilha: "Georgia, 'Times New Roman', Times, serif" },
  elegante: { rotulo: "Elegante (serifada fina)", pilha: "'Palatino Linotype', Palatino, 'Book Antiqua', Georgia, serif" },
  arredondada: { rotulo: "Arredondada", pilha: "'Trebuchet MS', Verdana, 'Segoe UI', sans-serif" },
  condensada: { rotulo: "Condensada", pilha: "'Arial Narrow', 'Roboto Condensed', Arial, sans-serif" }
};

export const IDS_FONTE = Object.keys(FONTES);

export const PROPORCOES = { "3/4": "Retrato (3:4)", "4/5": "Retrato curto (4:5)", "1/1": "Quadrada (1:1)", "2/3": "Retrato alto (2:3)", "4/3": "Paisagem (4:3)" };

/* ------------------------------------------------------------------ */
/* Variáveis CSS                                                       */
/* ------------------------------------------------------------------ */

/**
 * Variáveis `--vt-*` para o elemento raiz da vitrine, da pré-visualização do PDF e da impressão.
 * `produtos`: configuração dos cartões; `tipografia`: { fonte, pesoTitulo, tamanho }.
 */
export function variaveisCss(vitrine) {
  const tema = vitrine.tema;
  const c = coresEfetivas(tema);
  const p = vitrine.produtos;
  const t = tema.tipografia;
  const modelo = MODELOS[tema.modelo] || MODELOS.padrao;
  const semFundoPersonalizado = !tema.cores.fundo && modelo.fundoImagem && vitrine.estilo.vidro;
  const sombraA = Math.round(p.sombraForca) / 100;
  const largBorda = p.borda === "nenhuma" ? 0 : p.bordaLargura;
  return {
    "--vt-bg": c.fundo,
    "--vt-bg-img": semFundoPersonalizado
      ? "radial-gradient(60rem 30rem at 12% -8%, rgba(163,163,163,.28), transparent 60%), radial-gradient(48rem 28rem at 92% 6%, rgba(212,212,212,.55), transparent 60%), radial-gradient(40rem 26rem at 50% 108%, rgba(115,115,115,.18), transparent 60%)"
      : "none",
    "--vt-card": c.cartao,
    "--vt-title": c.titulo,
    "--vt-sub": c.subtitulo,
    "--vt-store": c.loja,
    "--vt-pname": c.nomeProduto,
    "--vt-pdesc": c.descricao,
    "--vt-pcode": c.codigo,
    "--vt-price": c.preco,
    "--vt-promo": c.promo,
    "--vt-btn": c.botao,
    "--vt-btn-text": c.botaoTexto,
    "--vt-tag": c.etiqueta,
    "--vt-tag-text": c.etiquetaTexto,
    "--vt-accent": c.destaque,
    "--vt-border": c.borda,
    "--vt-divider": c.divisoria,
    "--vt-shadow": `${c.sombra}`,
    "--vt-shadow-rgb": (() => {
      const rgb = hexParaRgb(c.sombra) || { r: 23, g: 23, b: 23 };
      return `${rgb.r}, ${rgb.g}, ${rgb.b}`;
    })(),
    "--vt-shadow-a": String(sombraA),
    "--vt-header": c.cabecalho,
    "--vt-header-text": c.cabecalhoTexto,
    "--vt-footer": c.rodape,
    "--vt-footer-text": c.rodapeTexto,
    "--vt-font": (FONTES[t.fonte] || FONTES.sistema).pilha,
    "--vt-title-weight": String(t.pesoTitulo),
    "--vt-fs": String(t.tamanho / 100),
    "--vt-cols": String(p.colunasPc),
    "--vt-cols-m": String(p.colunasCelular),
    "--vt-cols-t": String(Math.min(p.colunasPc, 3)),
    "--vt-gap": `${p.espacamento}px`,
    "--vt-radius": `${p.raio}px`,
    "--vt-border-w": `${largBorda}px`,
    "--vt-border-style": p.borda === "tracejada" ? "dashed" : "solid",
    "--vt-aspect": p.proporcao,
    "--vt-align": p.alinhamento === "centro" ? "center" : "left",
    "--vt-margin": `${vitrine.margens.externa}px`,
    "--vt-padding": `${vitrine.margens.interna}px`
  };
}

/* ------------------------------------------------------------------ */
/* Verificação de contraste (texto sobre fundo)                        */
/* ------------------------------------------------------------------ */

const PARES_CONTRASTE = [
  ["titulo", "fundo", "Títulos sobre o fundo"],
  ["subtitulo", "fundo", "Subtítulos sobre o fundo"],
  ["nomeProduto", "cartao", "Nome do produto sobre o cartão"],
  ["descricao", "cartao", "Descrição sobre o cartão"],
  ["codigo", "cartao", "Código sobre o cartão"],
  ["preco", "cartao", "Preço sobre o cartão"],
  ["promo", "cartao", "Preço promocional sobre o cartão"],
  ["botaoTexto", "botao", "Texto do botão sobre o botão"],
  ["etiquetaTexto", "etiqueta", "Texto da etiqueta sobre a etiqueta"],
  ["cabecalhoTexto", "cabecalho", "Texto do cabeçalho sobre o cabeçalho"],
  ["rodapeTexto", "rodape", "Texto do rodapé sobre o rodapé"]
];

/** Pares com contraste abaixo de 4,5:1 (texto pequeno). `corrigir` é a cor de texto sugerida. */
export function avisosDeContraste(tema) {
  const c = coresEfetivas(tema);
  const avisos = [];
  PARES_CONTRASTE.forEach(([texto, fundo, rotulo]) => {
    const razao = contraste(c[texto], c[fundo]);
    if (razao !== null && razao < 4.5) {
      avisos.push({ texto, fundo, rotulo, razao, corrigir: textoLegivelSobre(c[fundo]) });
    }
  });
  return avisos;
}

/* ------------------------------------------------------------------ */
/* Paletas e combinações                                               */
/* ------------------------------------------------------------------ */

/** Mistura duas cores (t = 0 devolve a primeira, 1 a segunda). */
export function misturar(hexA, hexB, t) {
  const a = hexParaRgb(hexA);
  const b = hexParaRgb(hexB);
  if (!a || !b) return normalizarHex(hexA) || "#000000";
  return rgbParaHex({ r: a.r + (b.r - a.r) * t, g: a.g + (b.g - a.g) * t, b: a.b + (b.b - a.b) * t });
}

/** Cores para escolher com um clique (por família). */
export const PALETAS = [
  { nome: "Neutros", cores: ["#111111", "#404040", "#737373", "#d4d4d4", "#f5f5f5", "#ffffff"] },
  { nome: "Oceano", cores: ["#0c4a6e", "#0369a1", "#0ea5e9", "#7dd3fc", "#e0f2fe", "#f8fafc"] },
  { nome: "Terra", cores: ["#3f2d20", "#7a5a1f", "#b08d4a", "#d9ccb4", "#f6f2ea", "#fffdf8"] },
  { nome: "Floresta", cores: ["#14532d", "#166534", "#22c55e", "#bbf7d0", "#f0fdf4", "#ffffff"] },
  { nome: "Vinho", cores: ["#4c0519", "#9f1239", "#e11d48", "#fecdd3", "#fff1f2", "#ffffff"] },
  { nome: "Laranja", cores: ["#7c2d12", "#c2410c", "#ea580c", "#fdba74", "#ffedd5", "#fff7ed"] },
  { nome: "Noite", cores: ["#0d1117", "#161b22", "#2b3340", "#9aa7b4", "#f0f6fc", "#2dd4bf"] }
];

/**
 * Gera um conjunto completo e coerente de cores a partir de três escolhas (fundo, texto e destaque),
 * já com texto legível sobre botões, etiquetas, cabeçalho e rodapé.
 */
export function esquemaDeCores({ fundo, texto, destaque }) {
  const bg = normalizarHex(fundo) || "#ffffff";
  const tx = normalizarHex(texto) || "#171717";
  const ac = normalizarHex(destaque) || tx;
  const escuro = textoLegivelSobre(bg) === "#ffffff";
  const cartao = escuro ? misturar(bg, "#ffffff", 0.06) : misturar(bg, "#ffffff", 0.75);
  // Textos de apoio ficam entre o texto e o fundo, mas nunca abaixo do contraste mínimo sobre fundo e cartão
  const apoio = (t) => {
    let passo = t;
    let cor = misturar(tx, bg, passo);
    while (passo > 0 && Math.min(contraste(cor, bg), contraste(cor, cartao)) < 4.5) {
      passo = Math.max(0, passo - 0.05);
      cor = misturar(tx, bg, passo);
    }
    return cor;
  };
  const suave = apoio(0.35);
  const linha = misturar(bg, tx, 0.16);
  return {
    fundo: bg, cartao, titulo: tx, subtitulo: suave, loja: tx, nomeProduto: tx, descricao: suave,
    codigo: apoio(0.4), preco: tx, promo: escuro ? "#ff7b72" : "#dc2626",
    botao: ac, botaoTexto: textoLegivelSobre(ac), etiqueta: ac, etiquetaTexto: textoLegivelSobre(ac), destaque: ac,
    borda: linha, divisoria: linha, sombra: escuro ? "#000000" : tx,
    cabecalho: cartao, cabecalhoTexto: tx, rodape: cartao, rodapeTexto: suave
  };
}

export const COMBINACOES = [
  { nome: "Preto e branco", fundo: "#ffffff", texto: "#111111", destaque: "#111111" },
  { nome: "Azul marinho", fundo: "#f4f7fb", texto: "#0f2a4a", destaque: "#1d4ed8" },
  { nome: "Verde praia", fundo: "#f2faf6", texto: "#12372a", destaque: "#0f9d6e" },
  { nome: "Coral", fundo: "#fff6f3", texto: "#4a1d12", destaque: "#e4572e" },
  { nome: "Vinho e creme", fundo: "#faf5ef", texto: "#3b0f1c", destaque: "#9f1239" },
  { nome: "Grafite e verde-água", fundo: "#0d1117", texto: "#f0f6fc", destaque: "#2dd4bf" }
];
