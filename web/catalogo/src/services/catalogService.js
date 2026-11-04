// Dados do catálogo no Nexus LS: vêm do programa pela ponte (estoque local), nunca do Firebase.
import { chamar } from "../ponte";
import { getProductImageUrl } from "./imageUploadService";
import { carregarPublico } from "./firestorePublico";

let cache = null;

// A resposta da ponte chega só à página principal (não aos iframes). A prévia do catálogo roda num iframe do
// editor, então lê os dados que a página principal já carregou (mesma origem).
const dentroDeIframe = typeof window !== "undefined" && window.parent !== window;

// No site publicado (publico.html) os dados vêm do Firestore (leitura pública); dentro do Nexus, da ponte.
const modoPublico = () => typeof window !== "undefined" && window.__nexusPublico === true;

export async function carregarTudo(forcar = false) {
  if (dentroDeIframe) {
    try {
      if (window.parent.__nexusDados) return window.parent.__nexusDados;
    } catch {
      /* origem diferente: segue pela ponte */
    }
  }
  if (!cache || forcar) {
    cache = modoPublico() ? await carregarPublico() : await chamar("carregar");
    if (!dentroDeIframe) window.__nexusDados = cache;
  }
  return cache;
}

const visivelNaVitrine = (p) => p.ativo !== false && p.ativoNoCatalogo !== false && p.ocultoNoCatalogo !== true;

/** Produtos visíveis a partir de uma lista, com a foto já resolvida. */
export function produtosVisiveisDe(lista) {
  return (Array.isArray(lista) ? lista : [])
    .map((prod) => {
      const foto = getProductImageUrl(prod);
      return { ...prod, fotoUrl: foto, imageUrl: foto, imagem: foto };
    })
    .filter(visivelNaVitrine);
}

export async function fetchPublicCatalog() {
  const dados = await carregarTudo();
  return produtosVisiveisDe(dados.produtos);
}

export async function fetchCompanyPublicInfo(companyId) {
  const dados = await carregarTudo();
  return { id: companyId, ...dados.empresa, vitrine: dados.vitrine };
}

export async function fetchPublicProduct(companyId, productId) {
  const lista = await fetchPublicCatalog();
  return lista.find((p) => p.id === productId) ?? null;
}

// A prévia do Nexus não grava pedidos nem usa cache de visitante.
export function lerCachePublicoDoCatalogo() {
  return null;
}
export function lerEmpresaPublicaDoCache() {
  return null;
}
export function getVisibleLocalCatalogProducts() {
  return [];
}

/**
 * Pedido da sacola. No site publicado nada é gravado na nuvem (as regras só permitem leitura): o pedido segue para
 * o WhatsApp da loja, que é o passo seguinte da sacola. Na pré-visualização do Nexus não há pedido.
 */
export async function submitPublicOrder(companyId, payload) {
  if (!modoPublico()) throw new Error("Esta é a pré-visualização do Nexus LS: pedidos só são feitos no catálogo publicado.");
  return {
    ...payload,
    numeroPedido: String(Date.now()).slice(-6),
    cliente: { nome: payload.nome, telefone: payload.whatsapp, endereco: payload.endereco }
  };
}

/**
 * Muda como um produto aparece no catálogo (visível, destaque, promoção). Fica guardado no computador; vai à nuvem
 * quando a vitrine é publicada. Atualiza a lista na hora, inclusive na pré-visualização.
 */
export async function ajustarProdutoCatalogo(id, mudanca) {
  const dados = await carregarTudo();
  const atual = dados.produtos.find((p) => p.id === id);
  if (!atual) throw new Error("Produto não encontrado.");
  const novo = { ...atual, ...mudanca };
  const promo = Number(novo.precoPromocional) || 0;
  if (promo < 0) throw new Error("O preço promocional não pode ser negativo.");
  if (promo > 0 && promo >= (Number(novo.precoVarejo) || 0)) throw new Error("O preço promocional precisa ser menor que o preço de varejo.");
  await chamar("salvarProdutoCatalogo", {
    id,
    visivel: novo.ativoNoCatalogo !== false,
    destaque: Boolean(novo.destaque),
    precoPromocional: Number(novo.precoPromocional) || 0
  });
  cache = { ...dados, produtos: dados.produtos.map((p) => (p.id === id ? novo : p)) };
  window.__nexusDados = cache;
  window.dispatchEvent(new CustomEvent("nexus:produtos", { detail: cache.produtos }));
  return novo;
}
