import { useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "../../ui/Button";
import { PaginaPdf, RaizTema } from "./CatalogoPaginas";
import { useCortes } from "../../../hooks/useCortes";
import { medidasPdf, totalPaginasPdf } from "../../../services/vitrineConfig";

const PX_POR_MM = 96 / 25.4;

/**
 * Pré-visualização do catálogo em PDF: as mesmas páginas que serão impressas, uma por vez, com navegação
 * e aviso dos produtos cujo conteúdo não cabe no cartão (podem sair cortados).
 */
export default function PreviaPdf({ vitrine, produtos, empresa, atacadoMin }) {
  const caixaRef = useRef(null);
  const paginaRef = useRef(null);
  const [larguraCaixa, setLarguraCaixa] = useState(0);
  const [pagina, setPagina] = useState(0);
  const [cortados, setCortados] = useState([]);

  const medidas = useMemo(() => medidasPdf(vitrine.pdf, vitrine), [vitrine]);
  const total = totalPaginasPdf(vitrine.pdf, produtos.length);
  const atual = Math.min(pagina, total - 1);

  useEffect(() => {
    const el = caixaRef.current;
    if (!el) return undefined;
    const ro = new ResizeObserver(([e]) => setLarguraCaixa(e.contentRect.width));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  useCortes(paginaRef, [vitrine, produtos, atual], setCortados);

  const larguraPx = medidas.largura * PX_POR_MM;
  const alturaPx = medidas.altura * PX_POR_MM;
  const escala = larguraCaixa ? Math.min(1, (larguraCaixa - 2) / larguraPx) : 1;
  const nomesCortados = cortados.map((id) => produtos.find((p) => p.id === id)?.nome).filter(Boolean);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Button size="sm" variant="outline" disabled={atual <= 0} onClick={() => setPagina(atual - 1)} leftIcon={<ChevronLeft className="h-4 w-4" />}>
            Anterior
          </Button>
          <span className="text-xs text-slate-300" aria-live="polite">
            Página {atual + 1} de {total}
          </span>
          <Button size="sm" variant="outline" disabled={atual >= total - 1} onClick={() => setPagina(atual + 1)} rightIcon={<ChevronRight className="h-4 w-4" />}>
            Próxima
          </Button>
        </div>
        <span className="text-xs text-slate-500">
          {produtos.length} produto(s) • {vitrine.pdf.colunas}×{vitrine.pdf.linhas} por página
        </span>
      </div>

      {nomesCortados.length > 0 && (
        <p role="status" className="flex items-start gap-2 rounded-xl border border-amber-500/40 bg-amber-500/5 p-3 text-xs text-amber-300">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            {nomesCortados.length} produto(s) desta página podem sair cortados: {nomesCortados.slice(0, 3).join(", ")}
            {nomesCortados.length > 3 ? "..." : ""}. Reduza as informações exibidas, use menos colunas/linhas ou diminua o tamanho do texto.
          </span>
        </p>
      )}

      <div ref={caixaRef} className="overflow-hidden rounded-2xl border border-slate-800 bg-slate-800/40 p-0">
        <div style={{ height: alturaPx * escala, width: larguraPx * escala, margin: "0 auto" }}>
          <div style={{ width: larguraPx, height: alturaPx, transform: `scale(${escala})`, transformOrigin: "top left" }}>
            <RaizTema vitrine={vitrine}>
              <div ref={paginaRef}>
                <PaginaPdf vitrine={vitrine} produtos={produtos} empresa={empresa} indice={atual} atacadoMin={atacadoMin} />
              </div>
            </RaizTema>
          </div>
        </div>
      </div>
    </div>
  );
}
