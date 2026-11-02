/** Mapeia nomes de cores para classes visuais sutis (amostras na vitrine). */
export function getColorSwatchClass(corNome = "") {
  const norm = String(corNome).toLowerCase();
  if (norm.includes("preto")) return "bg-neutral-900 border-neutral-700";
  if (norm.includes("branco") || norm.includes("off-white")) return "bg-white border-neutral-300";
  if (norm.includes("marinho")) return "bg-blue-950 border-blue-900";
  if (norm.includes("royal") || norm.includes("azul")) return "bg-sky-600 border-sky-500";
  if (norm.includes("verde") || norm.includes("militar")) return "bg-emerald-800 border-emerald-700";
  if (norm.includes("areia") || norm.includes("bege")) return "bg-amber-100 border-amber-300";
  if (norm.includes("cinza") || norm.includes("chumbo")) return "bg-neutral-500 border-neutral-400";
  if (norm.includes("bordô") || norm.includes("vermelho")) return "bg-rose-900 border-rose-800";
  if (norm.includes("amarelo")) return "bg-amber-300 border-amber-400";
  if (norm.includes("turquesa")) return "bg-teal-500 border-teal-400";
  return "bg-neutral-300 border-neutral-400";
}

/** Lista de cores do produto (aceita array ou texto separado por vírgula). */
export function listaDeCores(produto) {
  if (Array.isArray(produto?.cores)) return produto.cores.filter(Boolean);
  if (typeof produto?.cores === "string" && produto.cores.trim()) {
    return produto.cores.split(",").map((c) => c.trim()).filter(Boolean);
  }
  return [];
}
