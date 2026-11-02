import { getProductImageUrl } from "./imageUploadService";
import { precoVarejoEfetivo } from "./vitrineConfig";

/**
 * Regras puras da página individual do produto no catálogo online (sem Firebase, sem React).
 * Usa só os dados reais do cadastro: o estoque vem de `gradeTamanhos` (quantidade por tamanho)
 * ou de `estoqueTotal`. Cores não têm estoque próprio no cadastro; por isso aparecem como
 * informação, sem indicar disponibilidade por cor.
 */

const ORDEM_TAMANHOS = ["PP", "P", "M", "G", "GG", "XG", "XGG", "EG", "EGG", "G1", "G2", "G3", "G4", "ÚNICO", "UNICO", "U"];
export const LIMITE_ULTIMAS_UNIDADES = 3;

const comoNumero = (v) => {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
};

function chaveDeOrdem(nome) {
  const t = String(nome).trim().toUpperCase();
  const i = ORDEM_TAMANHOS.indexOf(t);
  if (i >= 0) return [0, i];
  if (/^\d+(?:[.,]\d+)?$/.test(t)) return [1, Number(t.replace(",", "."))];
  return [2, 0];
}

/** Tamanhos na ordem de uso (PP, P, M, G..., depois numerações), com a quantidade real em estoque. */
export function tamanhosDoProduto(produto) {
  const grade = produto?.gradeTamanhos;
  if (grade && typeof grade === "object" && Object.keys(grade).length > 0) {
    return Object.entries(grade)
      .map(([nome, qtd], posicao) => ({ nome, estoque: Math.max(0, comoNumero(qtd)), posicao }))
      .sort((a, b) => {
        const [ga, va] = chaveDeOrdem(a.nome);
        const [gb, vb] = chaveDeOrdem(b.nome);
        return ga - gb || va - vb || a.posicao - b.posicao;
      })
      .map(({ nome, estoque }) => ({ nome, estoque, disponivel: estoque > 0 }));
  }
  const total = comoNumero(produto?.estoqueTotal);
  if (total > 0) return [{ nome: "Único", estoque: total, disponivel: true }];
  // Sem grade e sem total cadastrados: não inventa tamanhos nem quantidade
  return [{ nome: "Único", estoque: null, disponivel: true }];
}

/** `null` = o cadastro não informa estoque (sem limite de quantidade, "consulte"). */
export function estoqueConhecido(produto) {
  const tem =
    (produto?.gradeTamanhos && Object.keys(produto.gradeTamanhos).length > 0) ||
    produto?.estoqueTotal !== undefined;
  return Boolean(tem);
}

/**
 * `semBaixa` = a loja ativou "Vender sem Baixa" (empresa.permitirVendaSemBaixa): o catálogo segue a loja e
 * não limita nem bloqueia por estoque (todo tamanho aparece disponível, sem teto de quantidade).
 */
export function resumoDeEstoque(produto, { semBaixa = false } = {}) {
  if (semBaixa) {
    const tamanhos = tamanhosDoProduto(produto).map((t) => ({ ...t, estoque: null, disponivel: true, semLimite: true }));
    return { conhecido: false, total: null, esgotado: false, semBaixa: true, tamanhos };
  }
  const tamanhos = tamanhosDoProduto(produto);
  const conhecido = estoqueConhecido(produto);
  if (!conhecido) return { conhecido: false, total: null, esgotado: false, tamanhos };
  const total = tamanhos.reduce((acc, t) => acc + (t.estoque || 0), 0);
  return { conhecido: true, total, esgotado: total <= 0, tamanhos };
}

/** Texto curto para o tamanho, baseado na quantidade real. */
export function rotuloDeEstoque(estoque, semBaixa = false) {
  if (semBaixa) return "Disponível";
  if (estoque === null || estoque === undefined) return "Consulte a disponibilidade";
  if (estoque <= 0) return "Indisponível";
  if (estoque <= LIMITE_ULTIMAS_UNIDADES) return estoque === 1 ? "Última unidade" : `Últimas ${estoque} unidades`;
  return "Disponível";
}

export function listaDeCoresDoProduto(produto) {
  if (Array.isArray(produto?.cores)) return produto.cores.map((c) => String(c).trim()).filter(Boolean);
  if (typeof produto?.cores === "string" && produto.cores.trim()) {
    return produto.cores.split(",").map((c) => c.trim()).filter(Boolean);
  }
  return [];
}

/** Maior quantidade que o cliente ainda pode colocar na sacola para este tamanho. */
export function maximoParaAdicionar(tamanho, quantidadeJaNaSacola = 0) {
  if (!tamanho) return 0;
  if (tamanho.estoque === null || tamanho.estoque === undefined) return 999;
  return Math.max(0, tamanho.estoque - quantidadeJaNaSacola);
}

export function percentualDesconto(cheio, efetivo) {
  const c = comoNumero(cheio);
  const e = comoNumero(efetivo);
  if (c <= 0 || e >= c) return 0;
  return Math.round(((c - e) / c) * 100);
}

/** Endereço próprio do produto (compartilhável). */
export function caminhoDoProduto(tenantId, produtoId) {
  return `/catalogo/${encodeURIComponent(tenantId)}/produto/${encodeURIComponent(produtoId)}`;
}

export function linkDoProduto(tenantId, produtoId, origem = "") {
  return `${origem}${caminhoDoProduto(tenantId, produtoId)}`;
}

/** Mensagem pré-escrita para o cliente chamar a loja pelo WhatsApp sobre esta peça. */
export function linkWhatsAppDoProduto({ whatsapp, loja, produto, tamanho, cor, link }) {
  const numero = String(whatsapp || "").replace(/\D/g, "");
  if (!numero) return "";
  const detalhes = [tamanho && `Tamanho: ${tamanho}`, cor && `Cor: ${cor}`].filter(Boolean).join(" | ");
  const texto = [
    `Olá${loja ? `, ${loja}` : ""}! Tenho interesse nesta peça:`,
    `*${produto?.nome || "Produto"}*${produto?.referencia ? ` (ref. ${produto.referencia})` : ""}`,
    detalhes,
    link
  ]
    .filter(Boolean)
    .join("\n");
  const ddi = numero.length <= 11 ? "55" : "";
  return `https://wa.me/${ddi}${numero}?text=${encodeURIComponent(texto)}`;
}

/**
 * Item da sacola (mesmo formato usado pelo catálogo, para a página e a vitrine compartilharem a sacola).
 */
export function montarItemSacola({ produto, tamanho, cor, tipoTabela = "varejo", quantidade = 1 }) {
  const precoVarejoCheio = comoNumero(produto.precoVarejo);
  const precoUnitario =
    tipoTabela === "atacado" ? comoNumero(produto.precoAtacado) || precoVarejoCheio : precoVarejoEfetivo(produto);
  return {
    key: `${produto.id}-${tamanho}-${cor}-${tipoTabela}`,
    id: produto.id,
    nome: produto.nome,
    referencia: produto.referencia || "",
    fotoUrl: getProductImageUrl(produto) || null,
    tamanho,
    cor,
    tipoVenda: tipoTabela,
    precoUnitario,
    precoVarejoReferencia: precoVarejoCheio,
    quantidade
  };
}

/** Soma o item a uma sacola, juntando com um igual (mesmo produto, tamanho, cor e tabela). */
export function somarNaSacola(sacola, item) {
  const lista = Array.isArray(sacola) ? sacola : [];
  const i = lista.findIndex((it) => it.key === item.key);
  if (i < 0) return [...lista, item];
  return lista.map((it, idx) => (idx === i ? { ...it, quantidade: it.quantidade + item.quantidade } : it));
}

export function quantidadeNaSacola(sacola, item) {
  return (Array.isArray(sacola) ? sacola : []).filter((it) => it.key === item.key).reduce((a, it) => a + it.quantidade, 0);
}

/* ---- Sacola guardada no navegador (por empresa), para a vitrine e a página do produto compartilharem ---- */

const chaveSacola = (tenantId) => `lifesurf_sacola_catalogo_${tenantId}`;

export function lerSacola(tenantId) {
  try {
    const lista = JSON.parse(localStorage.getItem(chaveSacola(tenantId)) || "[]");
    return (Array.isArray(lista) ? lista : []).filter(
      (it) => it && typeof it.key === "string" && Number(it.quantidade) > 0 && Number.isFinite(Number(it.precoUnitario))
    );
  } catch {
    return [];
  }
}

export function salvarSacola(tenantId, itens) {
  try {
    if (!itens || itens.length === 0) localStorage.removeItem(chaveSacola(tenantId));
    else localStorage.setItem(chaveSacola(tenantId), JSON.stringify(itens));
  } catch {
    /* navegador sem armazenamento: a sacola vale só enquanto a página estiver aberta */
  }
}

/* ---- Voltar ao catálogo sem perder filtros e posição ---- */

const chaveVoltar = (tenantId) => `lifesurf_catalogo_voltar_${tenantId}`;

export function guardarPontoDeRetorno(tenantId, { search = "", rolagem = 0 } = {}) {
  try {
    sessionStorage.setItem(chaveVoltar(tenantId), JSON.stringify({ search, rolagem, em: Date.now() }));
  } catch {
    /* ignora */
  }
}

export function lerPontoDeRetorno(tenantId) {
  try {
    const bruto = JSON.parse(sessionStorage.getItem(chaveVoltar(tenantId)) || "null");
    return bruto && typeof bruto === "object" ? bruto : null;
  } catch {
    return null;
  }
}

export function consumirPontoDeRetorno(tenantId) {
  const ponto = lerPontoDeRetorno(tenantId);
  try {
    sessionStorage.removeItem(chaveVoltar(tenantId));
  } catch {
    /* ignora */
  }
  return ponto;
}
