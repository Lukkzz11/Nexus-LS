import { useLayoutEffect, useRef } from "react";

/**
 * Detecta cartões cujo conteúdo não cabe (texto ou preço cortado) e quais produtos são. Mede o DOM depois de
 * pintar, então reflete a fonte e o tamanho reais.
 */
export function useCortes(ref, dependencias, aoMedir) {
  const ultimo = useRef("");
  useLayoutEffect(() => {
    const raiz = ref.current;
    if (!raiz) return undefined;
    const medir = () => {
      const cortados = [];
      raiz.querySelectorAll(".vt-pdf-celula").forEach((celula) => {
        const cartao = celula.firstElementChild;
        if (!cartao) return;
        const nome = cartao.querySelector("h3");
        const cortou = cartao.scrollHeight > celula.clientHeight + 1 || cartao.scrollWidth > celula.clientWidth + 1 || (nome && nome.scrollHeight > nome.clientHeight + 1);
        if (cortou) cortados.push(cartao.getAttribute("data-produto"));
      });
      const chave = cortados.join(",");
      if (chave !== ultimo.current) {
        ultimo.current = chave;
        aoMedir(cortados);
      }
    };
    const t = setTimeout(medir, 60);
    // Imagens e fontes mudam alturas depois do primeiro desenho
    const ro = new ResizeObserver(() => medir());
    ro.observe(raiz);
    return () => {
      clearTimeout(t);
      ro.disconnect();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, dependencias);
}
