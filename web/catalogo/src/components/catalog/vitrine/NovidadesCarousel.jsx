import { useRef } from "react";
import { ChevronLeft, ChevronRight, Sparkles } from "lucide-react";
import { produtosNovidades } from "../../../services/vitrineConfig";
import ProductCardGlass from "./ProductCardGlass";

/** Carrossel de novidades: peças em destaque e as cadastradas mais recentemente, com rolagem por setas ou toque. */
export default function NovidadesCarousel({ produtos, quantidade = 8, tipoTabela, atacadoMin, onAbrir, align = "esquerda", cfg }) {
  const trilho = useRef(null);
  const novos = produtosNovidades(produtos, quantidade);
  if (novos.length === 0) return null;

  const rolar = (sentido) => {
    const el = trilho.current;
    if (el) el.scrollBy({ left: sentido * Math.max(240, el.clientWidth * 0.8), behavior: "smooth" });
  };

  return (
    <section aria-label="Novidades">
      <div className={`mb-3 flex items-center gap-3 ${align === "centro" ? "justify-center" : "justify-between"}`}>
        <h2 className="vt-title flex items-center gap-2 text-lg tracking-tight sm:text-xl">
          <Sparkles className="vt-accent-text h-5 w-5" />
          Novidades
        </h2>
        <div className="flex gap-2">
          <button type="button" aria-label="Novidades anteriores" onClick={() => rolar(-1)} className="vt-glass vt-title flex h-9 w-9 items-center justify-center rounded-full">
            <ChevronLeft className="h-4 w-4" />
          </button>
          <button type="button" aria-label="Próximas novidades" onClick={() => rolar(1)} className="vt-glass vt-title flex h-9 w-9 items-center justify-center rounded-full">
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>
      <div ref={trilho} className="vt-trilho">
        {novos.map((p) => (
          <ProductCardGlass key={p.id} produto={p} tipoTabela={tipoTabela} atacadoMin={atacadoMin} onAbrir={onAbrir} cfg={cfg} className="w-[11.5rem] sm:w-[14rem]" />
        ))}
      </div>
    </section>
  );
}
