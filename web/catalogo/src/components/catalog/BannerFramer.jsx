import { useEffect, useRef, useState } from "react";
import { HeroSlide } from "./vitrine/HeroCarousel";
import { colagemDoSlide, ENQUADRAMENTO_PADRAO, MAX_ZOOM } from "../../services/vitrineConfig";
import { cn } from "../../utils/cn";

const limitar = (v, min, max) => Math.min(max, Math.max(min, v));
const arredondar = (v) => Math.round(v * 100) / 100;

/**
 * Prévia do banner com ajuste direto do enquadramento: duplo clique entra no modo de ajuste,
 * arrastar move a imagem (cima/baixo e para os lados) e a roda do mouse ou o controle dão zoom.
 */
export default function BannerFramer({ slide, align, imageSide, produtos, onChange, comecarAjustando = false }) {
  const [ajustando, setAjustando] = useState(comecarAjustando);
  const caixaRef = useRef(null);
  const arrasto = useRef(null);
  const temImagem = Boolean(slide.image);

  // Esc sai do modo de ajuste
  useEffect(() => {
    if (!ajustando) return undefined;
    const aoTeclar = (e) => e.key === "Escape" && setAjustando(false);
    document.addEventListener("keydown", aoTeclar);
    return () => document.removeEventListener("keydown", aoTeclar);
  }, [ajustando]);

  // A roda do mouse dá zoom (listener nativo para poder impedir a rolagem da página)
  useEffect(() => {
    const el = caixaRef.current;
    if (!el || !ajustando || !temImagem) return undefined;
    const aoRolar = (e) => {
      e.preventDefault();
      onChange({ ...slide, zoom: arredondar(limitar(slide.zoom - e.deltaY * 0.0015, 1, MAX_ZOOM)) });
    };
    el.addEventListener("wheel", aoRolar, { passive: false });
    return () => el.removeEventListener("wheel", aoRolar);
  }, [ajustando, temImagem, slide, onChange]);

  const aoPressionar = (e) => {
    if (!ajustando || !temImagem) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    arrasto.current = { x: e.clientX, y: e.clientY, focusX: slide.focusX, focusY: slide.focusY };
  };

  const aoMover = (e) => {
    const inicio = arrasto.current;
    const caixa = caixaRef.current?.getBoundingClientRect();
    if (!inicio || !caixa) return;
    // Arrastar para baixo mostra mais a parte de cima da foto (o ponto focal sobe)
    const dx = ((e.clientX - inicio.x) / caixa.width) * 100;
    const dy = ((e.clientY - inicio.y) / caixa.height) * 100;
    onChange({
      ...slide,
      focusX: arredondar(limitar(inicio.focusX - dx * 1.5, 0, 100)),
      focusY: arredondar(limitar(inicio.focusY - dy * 1.5, 0, 100))
    });
  };

  const soltar = () => {
    arrasto.current = null;
  };

  return (
    <div className="space-y-3">
      <div
        ref={caixaRef}
        className={cn("relative overflow-hidden rounded-[2rem] border bg-neutral-100", ajustando && temImagem ? "border-sky-500 ring-2 ring-sky-500" : "border-slate-700")}
        onDoubleClick={() => temImagem && setAjustando((v) => !v)}
      >
        <div className="pointer-events-none relative select-none">
          <HeroSlide slide={slide} align={align} imageSide={imageSide} colagem={colagemDoSlide(produtos)} ativo={false} />
        </div>

        {temImagem && ajustando && (
          <div
            className="absolute inset-0 z-10 cursor-grab touch-none active:cursor-grabbing"
            onPointerDown={aoPressionar}
            onPointerMove={aoMover}
            onPointerUp={soltar}
            onPointerCancel={soltar}
            aria-label="Área de enquadramento: arraste para mover a imagem"
          >
            {/* Grade de terços para ajudar a alinhar */}
            <div className="pointer-events-none absolute inset-0 grid grid-cols-3 grid-rows-3">
              {Array.from({ length: 9 }, (_, i) => (
                <div key={i} className="border border-white/30" />
              ))}
            </div>
          </div>
        )}

        <span className="vt-glass absolute right-3 top-3 z-20 rounded-full px-3 py-1 text-xs text-neutral-900">
          {!temImagem ? "Prévia (colagem das novidades)" : ajustando ? "Ajustando enquadramento" : "Duplo clique para enquadrar"}
        </span>
      </div>

      {temImagem && ajustando && (
        <div className="flex flex-wrap items-center gap-4 rounded-xl border border-slate-800 bg-slate-950/60 px-4 py-3 text-sm text-slate-200">
          <label className="flex flex-1 items-center gap-3" htmlFor={`zoom-${slide.id}`}>
            <span className="shrink-0 text-slate-400">Zoom</span>
            <input
              id={`zoom-${slide.id}`}
              type="range"
              min={1}
              max={MAX_ZOOM}
              step={0.01}
              value={slide.zoom}
              onChange={(e) => onChange({ ...slide, zoom: arredondar(Number(e.target.value)) })}
              className="w-full"
            />
            <span className="w-12 shrink-0 text-right tabular-nums">{Math.round(slide.zoom * 100)}%</span>
          </label>
          <span className="text-xs text-slate-500">
            Posição {Math.round(slide.focusX)}% × {Math.round(slide.focusY)}%
          </span>
          <button type="button" className="rounded-full px-3 py-1.5 text-slate-300 hover:bg-slate-800" onClick={() => onChange({ ...slide, ...ENQUADRAMENTO_PADRAO })}>
            Centralizar
          </button>
          <button type="button" className="rounded-full bg-sky-500 px-4 py-1.5 font-semibold text-slate-950" onClick={() => setAjustando(false)}>
            Concluir
          </button>
          <p className="w-full text-xs text-slate-500">
            Arraste a imagem para cima/baixo (ou para os lados) e use a roda do mouse ou o controle para dar zoom. Esc para sair.
          </p>
        </div>
      )}
    </div>
  );
}
