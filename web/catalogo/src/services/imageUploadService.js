// Imagens: foto do produto e redução de arquivos antes de mandar ao Nexus LS (sem Firebase aqui).

export const DEFAULT_PRODUCT_FALLBACK =
  "https://images.unsplash.com/photo-1521572267360-ee0c2909d518?w=800&auto=format&fit=crop&q=80";

/**
 * Retorna a URL da imagem de um produto inspecionando todos os possíveis aliases
 */
export function getProductImageUrl(product) {
  if (!product) return "";
  const candidates = [
    product.fotoUrl,
    product.imageUrl,
    product.imagem,
    product.imagemUrl,
    product.foto,
    product.image,
    product.url,
    product.foto_url
  ];

  for (const candidate of candidates) {
    if (typeof candidate === "string" && candidate.trim().length > 0) {
      return candidate.trim();
    }
  }

  return "";
}

/**
 * Otimiza e redimensiona um arquivo de imagem utilizando HTML5 Canvas no navegador
 * Converte imagens pesadas de smartphones (5MB-10MB) para ~80KB-140KB de alta nitidez
 * @param {File|Blob} file - Arquivo original
 * @param {number} maxWidth - Largura máxima (padrão: 960px)
 * @param {number} maxHeight - Altura máxima (padrão: 960px)
 * @param {number} quality - Qualidade de compressão (0.1 a 1.0)
 * @returns {Promise<{ blob: Blob, dataUrl: string }>}
 */
export async function compressImageFile(file, maxWidth = 960, maxHeight = 960, quality = 0.85) {
  return new Promise((resolve, reject) => {
    if (!file || !(file instanceof Blob)) {
      reject(new Error("Arquivo de imagem inválido para compressão"));
      return;
    }

    const reader = new FileReader();
    reader.onerror = () => reject(new Error("Erro ao ler arquivo"));
    reader.onload = (e) => {
      const img = new Image();
      img.onerror = () => reject(new Error("Formato de imagem não suportado"));
      img.onload = () => {
        let width = img.width;
        let height = img.height;

        // Mantém a proporção exata sem distorção
        if (width > height) {
          if (width > maxWidth) {
            height = Math.round((height * maxWidth) / width);
            width = maxWidth;
          }
        } else {
          if (height > maxHeight) {
            width = Math.round((width * maxHeight) / height);
            height = maxHeight;
          }
        }

        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;

        const ctx = canvas.getContext("2d");
        // Suavização bilinear para preservar nitidez das estampas
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = "high";
        ctx.drawImage(img, 0, 0, width, height);

        // Gera DataURL
        const mimeType = file.type === "image/png" ? "image/png" : "image/jpeg";
        const dataUrl = canvas.toDataURL(mimeType, quality);

        // Gera Blob
        canvas.toBlob(
          (blob) => {
            if (blob) {
              resolve({ blob, dataUrl });
            } else {
              // Fallback para conversão manual do dataURL
              resolve({ blob: file, dataUrl });
            }
          },
          mimeType,
          quality
        );
      };
      img.src = e.target.result;
    };
    reader.readAsDataURL(file);
  });
}
