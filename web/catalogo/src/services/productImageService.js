// Imagens do editor (banners e logotipo). O arquivo é reduzido aqui e guardado pelo Nexus LS no computador;
// só vai ao Firebase Storage quando a vitrine é publicada.
import { chamar } from "../ponte";
import { compressImageFile } from "./imageUploadService";

export async function uploadProductImageFile({ file, maxDimension = 1200 }) {
  const { dataUrl } = await compressImageFile(file, maxDimension, maxDimension, 0.85);
  const res = await chamar("enviarImagem", { dataUrl });
  return { url: res.url, path: res.path };
}

export async function deleteProductImage(path) {
  if (!path) return;
  try {
    await chamar("apagarImagem", { path });
  } catch (err) {
    console.warn("[productImageService] Não foi possível apagar a imagem:", err);
  }
}

export function isStorageImage(pathOrUrl) {
  return typeof pathOrUrl === "string" && pathOrUrl.length > 0;
}
