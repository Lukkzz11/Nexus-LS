// Ponte entre a página (React) e o programa (C#, WebView2).
// A página nunca fala com o Firebase: pede ao Nexus LS, que guarda no computador e publica só no clique.
// Fora do Nexus (npm run dev, navegador comum) usa uma versão de demonstração em memória, só para desenvolver.

const webview = typeof window !== "undefined" ? window.chrome?.webview : undefined;
export const dentroDoNexus = Boolean(webview);

const pendentes = new Map();
let proximoId = 1;

if (webview) {
  webview.addEventListener("message", (e) => {
    const m = e.data;
    if (!m || typeof m.id !== "number") return;
    const p = pendentes.get(m.id);
    if (!p) return;
    pendentes.delete(m.id);
    if (m.erro) p.rejeitar(new Error(m.erro));
    else p.resolver(m.resultado);
  });
}

/** Chama um método do programa e espera a resposta (as falhas voltam como Error com a mensagem em português). */
export function chamar(metodo, args = {}) {
  if (!webview) return demonstracao(metodo, args);
  return new Promise((resolver, rejeitar) => {
    const id = proximoId++;
    pendentes.set(id, { resolver, rejeitar });
    webview.postMessage({ id, metodo, args });
  });
}

/* ------------------------- demonstração (fora do Nexus) ------------------------- */

const CHAVE_DEMO = "nexus_catalogo_demo_vitrine";

function demonstracao(metodo, args) {
  switch (metodo) {
    case "carregar":
      return Promise.resolve({
        vitrine: lerDemo(),
        empresa: { nome: "Minha Loja", whatsapp: "", telefone: "", endereco: "", cidade: "", bairro: "", chavePix: "", mensagemCatalogo: "" },
        produtos: PRODUTOS_DEMO,
        publicadoEm: null
      });
    case "salvarVitrine":
    case "publicarVitrine":
      try { localStorage.setItem(CHAVE_DEMO, JSON.stringify(args.vitrine)); } catch { /* sem armazenamento */ }
      return Promise.resolve({ vitrine: args.vitrine, publicadoEm: new Date().toISOString() });
    case "enviarImagem":
      return Promise.resolve({ url: args.dataUrl, path: `demo/${Date.now()}.jpg` });
    default:
      return Promise.resolve(null);
  }
}

function lerDemo() {
  try { return JSON.parse(localStorage.getItem(CHAVE_DEMO) || "null"); } catch { return null; }
}

const PRODUTOS_DEMO = ["Camiseta Onda", "Bermuda Maré", "Boné Surf", "Regata Sol", "Moletom Brisa", "Chinelo Praia"].map((nome, i) => ({
  id: `demo-${i + 1}`,
  nome,
  categoria: i % 2 ? "Bermuda" : "Camiseta",
  referencia: `REF-${100 + i}`,
  precoVarejo: 59.9 + i * 10,
  precoAtacado: 39.9 + i * 8,
  precoPromocional: 0,
  descricao: "",
  fotoUrl: "",
  cores: ["Preto", "Branco"],
  ativo: true,
  ativoNoCatalogo: true,
  destaque: i === 0,
  gradeTamanhos: { P: 5, M: 8, G: 4 },
  createdAt: new Date(2026, 9, 1 + i).toISOString()
}));
