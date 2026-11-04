// Leitura pública do catálogo no Firestore (REST, sem login e sem SDK): é o que o site publicado usa.
// As regras do Firestore liberam só a LEITURA de "produtos" e "meta" a qualquer visitante.
// O projeto vem de config.json (ao lado da página) ou de ?projeto=ID na barra de endereço.

async function lerProjeto() {
  const doEndereco = new URLSearchParams(window.location.search).get("projeto");
  if (doEndereco) return doEndereco;
  try {
    const r = await fetch("./config.json", { cache: "no-store" });
    if (r.ok) return String((await r.json()).projectId || "").trim();
  } catch {
    /* sem config.json */
  }
  return "";
}

/** Converte um valor da API REST do Firestore (stringValue, integerValue, mapValue...) em valor comum. */
export function decodificar(v) {
  if (!v || typeof v !== "object") return null;
  if ("stringValue" in v) return v.stringValue;
  if ("integerValue" in v) return Number(v.integerValue);
  if ("doubleValue" in v) return Number(v.doubleValue);
  if ("booleanValue" in v) return v.booleanValue;
  if ("timestampValue" in v) return v.timestampValue;
  if ("nullValue" in v) return null;
  if ("arrayValue" in v) return (v.arrayValue.values || []).map(decodificar);
  if ("mapValue" in v) return decodificarCampos(v.mapValue.fields || {});
  return null;
}

export function decodificarCampos(campos) {
  return Object.fromEntries(Object.entries(campos).map(([k, v]) => [k, decodificar(v)]));
}

/** Documento do Firestore -> produto no formato do catálogo (preços em reais; o atacado nunca sai do computador do lojista). */
export function produtoDoFirestore(doc) {
  const c = decodificarCampos(doc.fields || {});
  const id = String(doc.name || "").split("/").pop();
  const grade = {};
  for (const t of c.tamanhos || []) if (t?.tamanho && t.tamanho !== "Sem tamanho") grade[t.tamanho] = Number(t.estoque) || 0;
  return {
    id,
    nome: c.nome || "",
    categoria: c.categoria || "",
    referencia: c.referencia || "",
    precoVarejo: (Number(c.precoVendaCentavos) || 0) / 100,
    precoAtacado: 0,
    precoPromocional: (Number(c.precoPromocionalCentavos) || 0) / 100,
    descricao: c.descricao || "",
    fotoUrl: c.fotoUrl || "",
    cores: [],
    ativo: c.ativo !== false,
    ativoNoCatalogo: c.ativoNoCatalogo !== false,
    destaque: c.destaque === true,
    gradeTamanhos: grade,
    createdAt: c.sincronizadoEm || ""
  };
}

async function obter(url) {
  const r = await fetch(url);
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`O catálogo não respondeu (erro ${r.status}).`);
  return r.json();
}

/** Vitrine publicada (campo v2 de meta/vitrine) + produtos. */
export async function carregarPublico() {
  const projeto = await lerProjeto();
  if (!projeto) throw new Error("Catálogo sem projeto configurado: preencha o projectId em config.json.");
  const base = `https://firestore.googleapis.com/v1/projects/${encodeURIComponent(projeto)}/databases/(default)/documents`;

  const meta = await obter(`${base}/meta/vitrine`);
  const textoV2 = meta?.fields?.v2?.stringValue;
  let vitrine = null;
  try {
    vitrine = textoV2 ? JSON.parse(textoV2) : null;
  } catch {
    vitrine = null;
  }

  const produtos = [];
  let pagina = "";
  do {
    const r = await obter(`${base}/produtos?pageSize=300${pagina ? `&pageToken=${encodeURIComponent(pagina)}` : ""}`);
    for (const d of r?.documents || []) produtos.push(produtoDoFirestore(d));
    pagina = r?.nextPageToken || "";
  } while (pagina);

  const loja = vitrine?.loja || {};
  const empresa = {
    nome: loja.nome || "",
    telefone: loja.telefone || "",
    whatsapp: loja.whatsapp || "",
    endereco: loja.endereco || "",
    cidade: "",
    bairro: "",
    chavePix: "",
    mensagemCatalogo: ""
  };
  return { vitrine, empresa, produtos };
}
