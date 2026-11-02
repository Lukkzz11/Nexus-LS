import { useState } from "react";
import { RotateCcw } from "lucide-react";
import { cn } from "../../../utils/cn";
import { PALETAS, hexParaRgb, normalizarHex, rgbParaHex } from "../../../services/temaVitrine";

/**
 * Uma linha de cor: amostra (abre o seletor visual), código hexadecimal, restaurar a cor original e,
 * quando selecionada, canais RGB, paletas e as últimas cores usadas.
 */
export default function ControleCor({ rotulo, valor, padrao, ativo, onAtivar, onChange, onRestaurar, recentes = [] }) {
  const alterada = valor !== padrao;
  const [texto, setTexto] = useState(null); // null = mostra o valor atual; string = o que está sendo digitado
  const rgb = hexParaRgb(valor) || { r: 0, g: 0, b: 0 };
  const hexInvalido = texto !== null && !normalizarHex(texto);

  const aplicarTexto = (t) => {
    setTexto(t);
    const hex = normalizarHex(t);
    if (hex) onChange(hex);
  };

  return (
    <div className={cn("rounded-xl border p-2 transition", ativo ? "border-sky-400 bg-sky-500/5" : "border-slate-800 bg-slate-950/40")}>
      <div className="flex items-center gap-2">
        <label className="relative h-8 w-8 shrink-0 cursor-pointer overflow-hidden rounded-lg border border-slate-600" style={{ background: valor }} title={`Escolher cor: ${rotulo}`}>
          <input
            type="color"
            value={valor}
            aria-label={`Seletor de cor: ${rotulo}`}
            onFocus={onAtivar}
            onChange={(e) => onChange(e.target.value)}
            className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
          />
        </label>
        <button type="button" onClick={onAtivar} className="min-w-0 flex-1 truncate text-left text-xs text-slate-200">
          {rotulo}
          {alterada && <span className="ml-1.5 text-[10px] text-sky-400">alterada</span>}
        </button>
        <input
          type="text"
          value={texto ?? valor}
          maxLength={7}
          spellCheck={false}
          aria-label={`Código hexadecimal: ${rotulo}`}
          aria-invalid={hexInvalido}
          onFocus={() => {
            onAtivar();
            setTexto(valor);
          }}
          onChange={(e) => aplicarTexto(e.target.value)}
          onBlur={() => setTexto(null)}
          className={cn(
            "h-8 w-[5.5rem] rounded-lg border bg-slate-950 px-2 font-mono text-xs uppercase text-slate-100 outline-none focus:border-sky-400",
            hexInvalido ? "border-rose-500" : "border-slate-700"
          )}
        />
        <button
          type="button"
          onClick={onRestaurar}
          disabled={!alterada}
          aria-label={`Restaurar a cor original: ${rotulo}`}
          title="Restaurar a cor original"
          className="grid h-8 w-8 shrink-0 place-items-center rounded-lg text-slate-400 transition hover:bg-slate-800 hover:text-white disabled:opacity-20"
        >
          <RotateCcw className="h-3.5 w-3.5" />
        </button>
      </div>

      {ativo && (
        <div className="mt-3 space-y-3 border-t border-slate-800 pt-3">
          <div>
            <p className="mb-1 text-[11px] text-slate-400">RGB</p>
            <div className="grid grid-cols-3 gap-2">
              {["r", "g", "b"].map((canal) => (
                <label key={canal} className="text-[10px] uppercase text-slate-500">
                  {canal}
                  <input
                    type="number"
                    min="0"
                    max="255"
                    value={rgb[canal]}
                    aria-label={`Canal ${canal.toUpperCase()}: ${rotulo}`}
                    onChange={(e) => onChange(rgbParaHex({ ...rgb, [canal]: e.target.value }))}
                    className="mt-0.5 h-8 w-full rounded-lg border border-slate-700 bg-slate-950 px-2 text-xs text-slate-100 outline-none focus:border-sky-400"
                  />
                </label>
              ))}
            </div>
          </div>

          {recentes.length > 0 && (
            <div>
              <p className="mb-1 text-[11px] text-slate-400">Usadas há pouco</p>
              <div className="flex flex-wrap gap-1.5">
                {recentes.map((c) => (
                  <button key={c} type="button" title={c} aria-label={`Usar ${c}`} onClick={() => onChange(c)} className="h-6 w-6 rounded-md border border-slate-600" style={{ background: c }} />
                ))}
              </div>
            </div>
          )}

          <div className="space-y-1.5">
            <p className="text-[11px] text-slate-400">Paletas</p>
            {PALETAS.map((p) => (
              <div key={p.nome} className="flex items-center gap-2">
                <span className="w-16 shrink-0 text-[10px] text-slate-500">{p.nome}</span>
                <div className="flex flex-wrap gap-1.5">
                  {p.cores.map((c) => (
                    <button key={c} type="button" title={c} aria-label={`Usar ${c} (${p.nome})`} onClick={() => onChange(c)} className="h-5 w-5 rounded-md border border-slate-600" style={{ background: c }} />
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
