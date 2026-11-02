import { useRef, useState } from "react";
import { AlertTriangle } from "lucide-react";
import { Button } from "../../ui/Button";
import ControleCor from "./ControleCor";
import {
  CHAVES_COR,
  COMBINACOES,
  GRUPOS_COR,
  MODELOS,
  avisosDeContraste,
  coresEfetivas,
  esquemaDeCores
} from "../../../services/temaVitrine";

const MAX_RECENTES = 8;

/** Cores de cada elemento: seletor visual, hexadecimal, RGB, paletas, combinações e restauração. */
export default function PainelCores({ vitrine, alterar }) {
  const [ativo, setAtivo] = useState("fundo");
  const [recentes, setRecentes] = useState([]);
  const tema = vitrine.tema;
  const efetivas = coresEfetivas(tema);
  const padroes = MODELOS[tema.modelo]?.cores || MODELOS.padrao.cores;
  const personalizadas = Object.keys(tema.cores).length;
  const avisos = avisosDeContraste(tema);
  const ultimoRecente = useRef("");

  const definir = (id, hex) => {
    // A cor só é gravada se for diferente do padrão do modelo; igual ao padrão = "sem alteração"
    alterar((r) => {
      const cores = { ...r.tema.cores };
      if (hex === padroes[id]) delete cores[id];
      else cores[id] = hex;
      return { ...r, tema: { ...r.tema, cores } };
    }, `cor:${id}`);
    if (ultimoRecente.current !== hex) {
      ultimoRecente.current = hex;
      setRecentes((l) => [hex, ...l.filter((c) => c !== hex)].slice(0, MAX_RECENTES));
    }
  };

  const restaurar = (id) =>
    alterar((r) => {
      const cores = { ...r.tema.cores };
      delete cores[id];
      return { ...r, tema: { ...r.tema, cores } };
    });

  const restaurarTodas = () => {
    if (personalizadas === 0) return;
    if (!window.confirm(`Voltar as ${personalizadas} cor(es) alterada(s) para as cores do modelo? Dá para desfazer.`)) return;
    alterar((r) => ({ ...r, tema: { ...r.tema, cores: {} } }));
  };

  const aplicarCombinacao = (c) => {
    if (personalizadas > 0 && !window.confirm(`Aplicar "${c.nome}" substitui as cores que você alterou. Dá para desfazer. Continuar?`)) return;
    alterar((r) => ({ ...r, tema: { ...r.tema, cores: esquemaDeCores(c) } }));
  };

  const corrigir = (aviso) => definir(aviso.texto, aviso.corrigir);

  return (
    <div className="space-y-5">
      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-white">Combinações prontas</h3>
        <p className="text-xs text-slate-400">Trocam todas as cores de uma vez, já com texto legível. Depois ajuste item por item.</p>
        <div className="grid grid-cols-2 gap-2">
          {COMBINACOES.map((c) => {
            const e = esquemaDeCores(c);
            return (
              <button
                key={c.nome}
                type="button"
                onClick={() => aplicarCombinacao(c)}
                className="rounded-xl border border-slate-700 bg-slate-950/50 p-2 text-left transition hover:border-sky-400"
              >
                <span className="mb-1.5 flex h-7 overflow-hidden rounded-md border border-slate-700" aria-hidden="true">
                  {[e.fundo, e.cartao, e.titulo, e.destaque].map((cor, i) => (
                    <span key={i} className="flex-1" style={{ background: cor }} />
                  ))}
                </span>
                <span className="text-xs text-slate-200">{c.nome}</span>
              </button>
            );
          })}
        </div>
      </section>

      {avisos.length > 0 && (
        <section className="space-y-2 rounded-xl border border-amber-500/40 bg-amber-500/5 p-3" role="status">
          <h3 className="flex items-center gap-2 text-sm font-semibold text-amber-300">
            <AlertTriangle className="h-4 w-4" />
            Texto difícil de ler
          </h3>
          {avisos.map((a) => (
            <div key={a.texto} className="flex items-center justify-between gap-2 text-xs text-slate-300">
              <span>
                {a.rotulo} (contraste {String(a.razao).replace(".", ",")}:1, o mínimo é 4,5:1)
              </span>
              <Button size="sm" variant="outline" onClick={() => corrigir(a)}>
                Corrigir
              </Button>
            </div>
          ))}
        </section>
      )}

      <div className="flex items-center justify-between">
        <p className="text-xs text-slate-400">
          {personalizadas === 0 ? "Usando as cores do modelo." : `${personalizadas} cor(es) alterada(s).`}
        </p>
        <Button size="sm" variant="ghost" onClick={restaurarTodas} disabled={personalizadas === 0}>
          Restaurar todas as cores
        </Button>
      </div>

      {GRUPOS_COR.map((grupo) => (
        <section key={grupo} className="space-y-2">
          <h3 className="text-sm font-semibold text-white">{grupo}</h3>
          {CHAVES_COR.filter((c) => c.grupo === grupo).map((c) => (
            <ControleCor
              key={c.id}
              rotulo={c.rotulo}
              valor={efetivas[c.id]}
              padrao={padroes[c.id]}
              ativo={ativo === c.id}
              onAtivar={() => setAtivo(c.id)}
              onChange={(hex) => definir(c.id, hex)}
              onRestaurar={() => restaurar(c.id)}
              recentes={recentes}
            />
          ))}
        </section>
      ))}
    </div>
  );
}
