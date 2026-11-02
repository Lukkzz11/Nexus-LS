import { useEffect, useMemo } from "react";
import { createPortal } from "react-dom";
import { PaginaPdf, RaizTema } from "./CatalogoPaginas";
import { FORMATOS_PDF, medidasPdf, totalPaginasPdf } from "../../../services/vitrineConfig";

const ESPERA_IMAGENS_MS = 15000;

/**
 * Monta todas as páginas do catálogo fora da tela e abre a impressão do navegador (escolha "Salvar como PDF").
 * É o mesmo componente da pré-visualização, então o PDF sai igual ao que aparece no editor, com texto
 * selecionável, fontes do sistema e as cores exatas (print-color-adjust: exact).
 */
export default function ImpressaoPdf({ vitrine, produtos, empresa, atacadoMin, nomeArquivo, onFim }) {
  const container = useMemo(() => {
    const el = document.createElement("div");
    el.id = "pdf-print-root";
    return el;
  }, []);
  const medidas = useMemo(() => medidasPdf(vitrine.pdf, vitrine), [vitrine]);
  const total = totalPaginasPdf(vitrine.pdf, produtos.length);

  useEffect(() => {
    document.body.appendChild(container);
    const estilo = document.createElement("style");
    estilo.id = "pdf-print-page";
    const formato = FORMATOS_PDF[vitrine.pdf.formato] || FORMATOS_PDF.a4;
    estilo.textContent = `@page { size: ${medidas.largura}mm ${medidas.altura}mm; margin: 0; } /* ${formato.rotulo} */`;
    document.head.appendChild(estilo);
    const tituloAnterior = document.title;
    document.title = nomeArquivo || "Catalogo";

    let cancelado = false;
    const limpar = () => {
      document.title = tituloAnterior;
      estilo.remove();
      container.remove();
    };
    const terminar = () => {
      if (cancelado) return;
      cancelado = true;
      window.removeEventListener("afterprint", terminar);
      limpar();
      onFim?.();
    };

    const imagens = () => [...container.querySelectorAll("img")];
    const esperar = async () => {
      const pendentes = imagens().filter((img) => !img.complete);
      await Promise.race([
        Promise.all(pendentes.map((img) => new Promise((ok) => { img.addEventListener("load", ok, { once: true }); img.addEventListener("error", ok, { once: true }); }))),
        new Promise((ok) => setTimeout(ok, ESPERA_IMAGENS_MS))
      ]);
      if (document.fonts?.ready) await document.fonts.ready;
      // Dois quadros para o navegador terminar o layout antes de abrir a impressão
      await new Promise((ok) => requestAnimationFrame(() => requestAnimationFrame(ok)));
      if (cancelado) return;
      window.addEventListener("afterprint", terminar);
      window.print();
    };
    esperar();

    return () => {
      cancelado = true;
      window.removeEventListener("afterprint", terminar);
      limpar();
    };
    // Monta uma vez por exportação
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return createPortal(
    <RaizTema vitrine={vitrine}>
      {Array.from({ length: total }, (_, i) => (
        <PaginaPdf key={i} vitrine={vitrine} produtos={produtos} empresa={empresa} indice={i} atacadoMin={atacadoMin} />
      ))}
    </RaizTema>,
    container
  );
}
