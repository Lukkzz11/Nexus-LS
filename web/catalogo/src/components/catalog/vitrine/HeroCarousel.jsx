import { useCallback, useEffect, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, ArrowRight } from "lucide-react";
import { cn } from "../../../utils/cn";
import { DEFAULT_PRODUCT_FALLBACK } from "../../../services/imageUploadService";
import { estiloEnquadramento, colagemDoSlide } from "../../../services/vitrineConfig";

function Foto({ src, className }) {
  return (
    <img
      src={src || DEFAULT_PRODUCT_FALLBACK}
      alt=""
      className={className}
      draggable={false}
      onError={(e) => {
        e.currentTarget.onerror = null;
        e.currentTarget.src = DEFAULT_PRODUCT_FALLBACK;
      }}
    />
  );
}

/** Um slide do banner (usado no carrossel da vitrine e na pré-visualização de enquadramento do editor). */
export function HeroSlide({ slide, align = "esquerda", imageSide = "direita", colagem = {}, onCta, ativo = true }) {
  const temImagem = Boolean(slide.image);
  const centro = align === "centro";
  const painelNaDireita = imageSide === "esquerda";

  return (
    <div className="relative min-h-[22rem] sm:min-h-[26rem] overflow-hidden rounded-[1.75rem]" style={{ background: "var(--vt-card, #e5e5e5)" }}>
      {temImagem ? (
        <>
          <img
            src={slide.image}
            alt=""
            draggable={false}
            className="absolute inset-0 h-full w-full select-none"
            style={estiloEnquadramento(slide)}
            onError={(e) => {
              e.currentTarget.style.display = "none";
            }}
          />
          <div
            className={cn(
              "absolute inset-0",
              centro
                ? "bg-neutral-950/25"
                : painelNaDireita
                  ? "bg-gradient-to-l from-neutral-950/50 via-neutral-950/10 to-transparent"
                  : "bg-gradient-to-r from-neutral-950/50 via-neutral-950/10 to-transparent"
            )}
          />
        </>
      ) : (
        <>
          <div
            className="absolute inset-0"
            style={{ background: "linear-gradient(135deg, color-mix(in srgb, var(--vt-card, #f5f5f5) 88%, var(--vt-accent, #888)), color-mix(in srgb, var(--vt-bg, #e5e5e5) 72%, var(--vt-accent, #888)))" }}
          />
          <div className="absolute -top-24 -right-16 h-72 w-72 rounded-full blur-3xl" style={{ background: "color-mix(in srgb, var(--vt-card, #fff) 70%, transparent)" }} />
          <div className="absolute -bottom-24 left-1/3 h-72 w-72 rounded-full blur-3xl" style={{ background: "color-mix(in srgb, var(--vt-accent, #888) 25%, transparent)" }} />
          {/* Colagem das novidades: duas peças em cartões de vidro */}
          <div
            className={cn(
              "absolute inset-y-6 hidden w-[46%] items-center justify-center gap-4 md:flex",
              painelNaDireita ? "left-6" : "right-6"
            )}
            aria-hidden="true"
          >
            <div className="vt-glass-strong w-1/2 -rotate-3 overflow-hidden rounded-2xl p-1.5">
              <Foto src={colagem.a} className="aspect-[3/4] w-full rounded-xl object-cover" />
            </div>
            <div className="vt-glass-strong mt-10 w-1/2 rotate-3 overflow-hidden rounded-2xl p-1.5">
              <Foto src={colagem.b} className="aspect-[3/4] w-full rounded-xl object-cover" />
            </div>
          </div>
        </>
      )}

      <div
        className={cn(
          "relative z-10 flex min-h-[22rem] items-center p-4 sm:min-h-[26rem] sm:p-8",
          centro ? "justify-center" : painelNaDireita ? "justify-end" : "justify-start"
        )}
      >
        <div className={cn("vt-glass-strong w-full max-w-md rounded-3xl p-5 sm:p-7", centro && "text-center")}>
          {slide.badge && (
            <span className="vt-tag inline-block rounded-full px-3 py-1 text-[11px] font-semibold tracking-wide">
              {slide.badge}
            </span>
          )}
          <h2 className="vt-title mt-3 text-2xl leading-tight tracking-tight sm:text-4xl">
            {slide.title}
            {slide.highlight && <span className="vt-accent-text block">{slide.highlight}</span>}
          </h2>
          {slide.subtitle && <p className="vt-sub mt-2 text-sm leading-relaxed">{slide.subtitle}</p>}
          <button
            type="button"
            tabIndex={ativo ? 0 : -1}
            onClick={() => onCta?.(slide)}
            className="vt-btn mt-5 inline-flex h-11 items-center gap-2 rounded-full px-5 text-xs font-semibold transition"
          >
            {slide.ctaLabel}
            <ArrowRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * Carrossel do banner: troca sozinho no intervalo configurado (padrão 40 s), pausa com o mouse em cima
 * ou com a aba escondida, e reinicia o ciclo quando a pessoa interage (setas, pontos ou deslizar).
 */
export default function HeroCarousel({ slides, intervaloSeg = 40, align, imageSide, produtos, onCta, onDoubleClick }) {
  const [indice, setIndice] = useState(0);
  const [pausado, setPausado] = useState(false);
  const toqueInicial = useRef(null);
  const total = slides.length;
  const atual = Math.min(indice, Math.max(0, total - 1));

  const ir = useCallback((i) => setIndice(((i % total) + total) % total), [total]);

  // Reinicia o relógio a cada troca (inclusive as manuais)
  useEffect(() => {
    if (total < 2 || pausado) return undefined;
    const t = setTimeout(() => setIndice((i) => (i + 1) % total), Math.max(5, intervaloSeg) * 1000);
    return () => clearTimeout(t);
  }, [atual, total, pausado, intervaloSeg]);

  useEffect(() => {
    const aoMudarAba = () => setPausado(document.hidden);
    document.addEventListener("visibilitychange", aoMudarAba);
    return () => document.removeEventListener("visibilitychange", aoMudarAba);
  }, []);

  if (total === 0) return null;
  const colagem = colagemDoSlide(produtos);

  return (
    <section
      aria-roledescription="carrossel"
      aria-label="Novidades"
      className="relative"
      onMouseEnter={() => setPausado(true)}
      onMouseLeave={() => setPausado(false)}
      onDoubleClick={() => onDoubleClick?.(slides[atual]?.id)}
      onTouchStart={(e) => (toqueInicial.current = e.touches[0].clientX)}
      onTouchEnd={(e) => {
        if (toqueInicial.current == null) return;
        const dx = e.changedTouches[0].clientX - toqueInicial.current;
        toqueInicial.current = null;
        if (Math.abs(dx) > 50) ir(atual + (dx < 0 ? 1 : -1));
      }}
    >
      <div className="grid">
        {slides.map((s, i) => (
          <div
            key={s.id}
            role="group"
            aria-roledescription="slide"
            aria-label={`${i + 1} de ${total}`}
            aria-hidden={i !== atual}
            className={cn(
              "col-start-1 row-start-1 transition-opacity duration-700",
              i === atual ? "opacity-100" : "pointer-events-none opacity-0"
            )}
          >
            <HeroSlide slide={s} align={align} imageSide={imageSide} colagem={colagem} onCta={onCta} ativo={i === atual} />
          </div>
        ))}
      </div>

      {total > 1 && (
        <>
          <button
            type="button"
            aria-label="Slide anterior"
            onClick={() => ir(atual - 1)}
            className="vt-glass vt-title absolute left-3 top-1/2 z-20 hidden h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full transition-colors sm:flex"
          >
            <ChevronLeft className="h-5 w-5" />
          </button>
          <button
            type="button"
            aria-label="Próximo slide"
            onClick={() => ir(atual + 1)}
            className="vt-glass vt-title absolute right-3 top-1/2 z-20 hidden h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full transition-colors sm:flex"
          >
            <ChevronRight className="h-5 w-5" />
          </button>
          <div className="vt-glass absolute bottom-3 left-1/2 z-20 flex -translate-x-1/2 items-center gap-1.5 rounded-full px-3 py-2">
            {slides.map((s, i) => (
              <button
                key={s.id}
                type="button"
                aria-label={`Ir para o slide ${i + 1}`}
                aria-current={i === atual}
                onClick={() => ir(i)}
                className={cn("h-2 rounded-full transition-all", i === atual ? "vt-accent w-6" : "w-2 bg-neutral-400 hover:bg-neutral-600")}
              />
            ))}
          </div>
        </>
      )}
    </section>
  );
}
