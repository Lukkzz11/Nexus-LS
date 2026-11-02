// Dados do catálogo no Nexus LS: vêm do programa pela ponte (estoque local), nunca do Firebase.
import { chamar } from "../ponte";
import { getProductImageUrl } from "./imageUploadService";

let cache = null;

// A resposta da ponte chega só à página principal (não aos iframes). A prévia do catálogo roda num iframe do
// editor, então lê os dados que a página principal já carregou (mesma origem).
const dentroDeIframe = typeof window !== "undefined" && window.parent !== window;

export async function carregarTudo(forcar = false) {
  if (dentroDeIframe) {
    try {
      if (window.parent.__nexusDados) return window.parent.__nexusDados;
    } catch {
      /* origem diferente: segue pela ponte */
    }
  }
  if (!cache || forcar) {
    cache = await chamar("carregar");
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
export async function submitPublicOrder() {
  throw new Error("Esta é a pré-visualização do Nexus LS: pedidos só são feitos no catálogo publicado.");
}
