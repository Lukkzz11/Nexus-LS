import { useState } from "react";
import { Copy, Trash2 } from "lucide-react";
import { Button } from "../../ui/Button";
import { cn } from "../../../utils/cn";
import { MODELOS, IDS_MODELO } from "../../../services/temaVitrine";
import { MAX_MODELOS_SALVOS, aplicarModelo, aplicarModeloSalvo, extrairLook, limparTexto } from "../../../services/vitrineConfig";

function Amostra({ cores }) {
  return (
    <span className="flex h-9 overflow-hidden rounded-md border border-slate-700" aria-hidden="true">
      {[cores.fundo, cores.cartao, cores.titulo, cores.destaque, cores.botao].map((c, i) => (
        <span key={i} className="flex-1" style={{ background: c }} />
      ))}
    </span>
  );
}

/** Modelos prontos (pontos de partida) e "Meus modelos" (o visual atual guardado com um nome). */
export default function PainelModelos({ vitrine, alterar }) {
  const [nome, setNome] = useState("");
  const personalizado = Object.keys(vitrine.tema.cores).length > 0;

  const usarModelo = (id) => {
    if (id === vitrine.tema.modelo && !personalizado) return;
    if (personalizado && !window.confirm("Trocar de modelo substitui as cores, a fonte e o formato dos cartões que você alterou. Dá para desfazer. Continuar?")) return;
    alterar((r) => aplicarModelo(r, id));
  };

  const usarSalvo = (m) => {
    if (!window.confirm(`Aplicar o modelo "${m.nome}"? Substitui o visual atual (dá para desfazer).`)) return;
    alterar((r) => aplicarModeloSalvo(r, m));
  };

  const salvarAtual = () => {
    const limpo = limparTexto(nome, 40);
    if (!limpo) return;
    if (vitrine.modelosSalvos.length >= MAX_MODELOS_SALVOS) return;
    const look = extrairLook(vitrine);
    alterar((r) => ({
      ...r,
      modelosSalvos: [...r.modelosSalvos, { id: `m-${Date.now().toString(36)}`, nome: limpo, vidro: look.vidro, tema: look.tema, produtos: look.produtos, margens: look.margens }]
    }));
    setNome("");
  };

  const remover = (m) => {
    if (!window.confirm(`Excluir o modelo "${m.nome}"? O catálogo atual não muda.`)) return;
    alterar((r) => ({ ...r, modelosSalvos: r.modelosSalvos.filter((x) => x.id !== m.id) }));
  };

  return (
    <div className="space-y-5">
      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-white">Modelos</h3>
        <p className="text-xs text-slate-400">Escolha um ponto de partida. Cores, fonte e formato dos cartões continuam editáveis depois.</p>
        <div className="grid grid-cols-1 gap-2">
          {IDS_MODELO.map((id) => {
            const m = MODELOS[id];
            const atual = vitrine.tema.modelo === id;
            return (
              <button
                key={id}
                type="button"
                onClick={() => usarModelo(id)}
                aria-pressed={atual}
                className={cn("rounded-xl border p-3 text-left transition", atual ? "border-sky-400 bg-sky-500/10" : "border-slate-700 bg-slate-950/50 hover:border-slate-500")}
              >
                <Amostra cores={m.cores} />
                <span className="mt-2 flex items-center justify-between text-sm font-medium text-white">
                  {m.rotulo}
                  {atual && <span className="text-[11px] font-normal text-sky-300">{personalizado ? "em uso, com ajustes" : "em uso"}</span>}
                </span>
                <span className="block text-xs text-slate-400">{m.descricao}</span>
              </button>
            );
          })}
        </div>
      </section>

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-white">
          Meus modelos <span className="font-normal text-slate-500">({vitrine.modelosSalvos.length}/{MAX_MODELOS_SALVOS})</span>
        </h3>
        <p className="text-xs text-slate-400">Guarda o visual de agora (cores, fonte, cartões e margens) com um nome, para duplicar ou voltar depois. Textos, banners e produtos não entram.</p>
        <div className="flex gap-2">
          <input
            type="text"
            value={nome}
            maxLength={40}
            placeholder="Nome do modelo"
            aria-label="Nome do novo modelo"
            onChange={(e) => setNome(e.target.value)}
            className="h-9 min-w-0 flex-1 rounded-lg border border-slate-700 bg-slate-950 px-3 text-sm text-slate-100 outline-none focus:border-sky-400"
          />
          <Button size="sm" variant="primary" onClick={salvarAtual} disabled={!limparTexto(nome, 40) || vitrine.modelosSalvos.length >= MAX_MODELOS_SALVOS} leftIcon={<Copy className="h-4 w-4" />}>
            Salvar visual atual
          </Button>
        </div>
        {vitrine.modelosSalvos.length >= MAX_MODELOS_SALVOS && <p className="text-xs text-amber-400">Limite de {MAX_MODELOS_SALVOS} modelos: exclua um para salvar outro.</p>}
        <ul className="space-y-2">
          {vitrine.modelosSalvos.map((m) => (
            <li key={m.id} className="flex items-center gap-2 rounded-xl border border-slate-800 bg-slate-950/50 p-2">
              <span className="min-w-0 flex-1 truncate text-sm text-slate-100">{m.nome}</span>
              <Button size="sm" variant="outline" onClick={() => usarSalvo(m)}>
                Aplicar
              </Button>
              <button type="button" aria-label={`Excluir o modelo ${m.nome}`} onClick={() => remover(m)} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-800 hover:text-white">
                <Trash2 className="h-4 w-4" />
              </button>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
