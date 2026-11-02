import { ChevronDown } from "lucide-react";
import { cn } from "../../../utils/cn";

/** Escolha entre poucas opções, em formato de pílula. */
export function Segmentado({ rotulo, opcoes, rotulos, valor, onChange }) {
  return (
    <div>
      <p className="mb-1.5 text-xs text-slate-400">{rotulo}</p>
      <div className="flex rounded-full border border-slate-700 bg-slate-950/60 p-1" role="radiogroup" aria-label={rotulo}>
        {opcoes.map((o) => (
          <button
            key={String(o)}
            type="button"
            role="radio"
            aria-checked={valor === o}
            onClick={() => onChange(o)}
            className={cn(
              "flex-1 rounded-full px-3 py-1.5 text-xs transition",
              valor === o ? "bg-sky-500 font-semibold text-slate-950" : "text-slate-300 hover:text-white"
            )}
          >
            {rotulos[o]}
          </button>
        ))}
      </div>
    </div>
  );
}

/** Controle deslizante com o valor ao lado. `chave` agrupa o arrastar num único passo do desfazer. */
export function Deslizante({ rotulo, valor, min, max, passo = 1, unidade = "", onChange }) {
  return (
    <label className="block">
      <span className="mb-1 flex items-center justify-between text-xs text-slate-400">
        <span>{rotulo}</span>
        <span className="font-mono text-slate-200">
          {valor}
          {unidade}
        </span>
      </span>
      <input
        type="range"
        min={min}
        max={max}
        step={passo}
        value={valor}
        aria-label={rotulo}
        onChange={(e) => onChange(Number(e.target.value))}
        className="h-2 w-full cursor-pointer accent-sky-500"
      />
    </label>
  );
}

/** Liga/desliga uma opção. */
export function Alternar({ rotulo, ajuda, valor, onChange, disabled = false }) {
  return (
    <label className={cn("flex items-start gap-3 rounded-xl border border-slate-800 bg-slate-950/40 p-2.5", disabled ? "opacity-50" : "cursor-pointer")}>
      <input type="checkbox" className="mt-0.5 h-4 w-4 shrink-0" checked={valor} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      <span className="min-w-0">
        <span className="block text-sm text-slate-100">{rotulo}</span>
        {ajuda && <span className="block text-xs text-slate-400">{ajuda}</span>}
      </span>
    </label>
  );
}

/** Seção que abre e fecha (opções avançadas ficam recolhidas). */
export function Secao({ titulo, children, aberta = false, resumo }) {
  return (
    <details open={aberta} className="group rounded-2xl border border-slate-800 bg-slate-900/60">
      <summary className="flex cursor-pointer list-none items-center justify-between gap-2 px-4 py-3 text-sm font-semibold text-white">
        <span>
          {titulo}
          {resumo && <span className="ml-2 text-xs font-normal text-slate-500">{resumo}</span>}
        </span>
        <ChevronDown className="h-4 w-4 shrink-0 text-slate-400 transition group-open:rotate-180" />
      </summary>
      <div className="space-y-4 border-t border-slate-800 p-4">{children}</div>
    </details>
  );
}

export function CampoTexto({ rotulo, valor, onChange, maxLength = 80, placeholder = "", ajuda, tipo = "text" }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs text-slate-400">{rotulo}</span>
      <input
        type={tipo}
        value={valor}
        maxLength={maxLength}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        className="h-9 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 text-sm text-slate-100 outline-none placeholder:text-slate-600 focus:border-sky-400"
      />
      {ajuda && <span className="mt-1 block text-[11px] text-slate-500">{ajuda}</span>}
    </label>
  );
}
