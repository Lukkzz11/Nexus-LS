import { useMemo, useState } from "react";
import { ArrowDown, ArrowUp, Search } from "lucide-react";
import { Alternar, Secao, Segmentado } from "./campos";
import { ItemOrdenavel, ListaOrdenavel } from "./ListaOrdenavel";
import {
  INFO_PRODUTO_LABELS,
  MODOS_SELECAO,
  MOSTRAR_PRODUTO,
  MOSTRAR_PRODUTO_LABELS,
  ORDENS_PRODUTO,
  ORDEM_LABELS,
  precoPromocional,
  selecionarProdutos
} from "../../../services/vitrineConfig";
import { formatCurrency } from "../../../utils/formatters";

const ROTULOS_MODO = { todos: "Todos", categorias: "Categorias", produtos: "Peças" };

/** O que aparece em cada produto, em que ordem, e quais produtos entram no catálogo e em que sequência. */
export default function PainelProdutos({ vitrine, alterar, produtos }) {
  const [busca, setBusca] = useState("");
  const p = vitrine.produtos;
  const sel = vitrine.selecao;

  const categorias = useMemo(() => [...new Set(produtos.map((x) => (x.categoria || "").trim()).filter(Boolean))].sort((a, b) => a.localeCompare(b, "pt-BR")), [produtos]);
  const noCatalogo = useMemo(() => selecionarProdutos(produtos, sel), [produtos, sel]);
  const promocoes = produtos.filter((x) => precoPromocional(x) > 0).length;

  const setMostrar = (k) => (v) => alterar((r) => ({ ...r, produtos: { ...r.produtos, mostrar: { ...r.produtos.mostrar, [k]: v } } }));
  const moverInfo = (k, delta) =>
    alterar((r) => {
      const lista = [...r.produtos.ordemInfo];
      const de = lista.indexOf(k);
      const para = de + delta;
      if (para < 0 || para >= lista.length) return r;
      lista.splice(de, 1);
      lista.splice(para, 0, k);
      return { ...r, produtos: { ...r.produtos, ordemInfo: lista } };
    });
  const setSel = (mudanca) => alterar((r) => ({ ...r, selecao: { ...r.selecao, ...mudanca } }));

  const alternarId = (campo, id) => {
    const atual = new Set(sel[campo]);
    if (atual.has(id)) atual.delete(id);
    else atual.add(id);
    setSel({ [campo]: [...atual] });
  };

  // Ordem manual: parte da ordem gravada e acrescenta as peças novas no fim
  const listaManual = useMemo(() => {
    const base = selecionarProdutos(produtos, { ...sel, ordem: "manual" });
    return base;
  }, [produtos, sel]);

  const moverManual = (id, delta) => {
    const ids = listaManual.map((x) => x.id);
    const de = ids.indexOf(id);
    const para = de + delta;
    if (de < 0 || para < 0 || para >= ids.length) return;
    ids.splice(de, 1);
    ids.splice(para, 0, id);
    setSel({ ordemManual: ids });
  };

  const filtradas = produtos.filter((x) => !busca.trim() || `${x.nome} ${x.referencia || ""}`.toLowerCase().includes(busca.toLowerCase()));

  return (
    <div className="space-y-3">
      <Secao titulo="O que aparece em cada produto" aberta>
        <div className="space-y-2">
          {MOSTRAR_PRODUTO.map((k) => (
            <Alternar key={k} rotulo={MOSTRAR_PRODUTO_LABELS[k]} valor={p.mostrar[k]} onChange={setMostrar(k)} />
          ))}
        </div>
        <Alternar
          rotulo="Destacar produtos em promoção"
          ajuda={`Etiqueta "Promoção" e preço na cor promocional. ${promocoes} produto(s) com preço promocional cadastrado.`}
          valor={p.destacarPromocao}
          onChange={(v) => alterar((r) => ({ ...r, produtos: { ...r.produtos, destacarPromocao: v } }))}
        />
      </Secao>

      <Secao titulo="Ordem das informações" resumo="nome, preço, tamanhos...">
        <p className="text-xs text-slate-400">A foto fica sempre em cima. Use as setas para mudar a ordem do que vem abaixo dela.</p>
        <ol className="space-y-1.5">
          {p.ordemInfo.map((k, i) => (
            <li key={k} className="flex items-center gap-2 rounded-lg border border-slate-800 bg-slate-950/50 px-2 py-1.5">
              <span className="min-w-0 flex-1 truncate text-sm text-slate-100">{INFO_PRODUTO_LABELS[k]}</span>
              <button type="button" aria-label={`Subir ${INFO_PRODUTO_LABELS[k]}`} disabled={i === 0} onClick={() => moverInfo(k, -1)} className="grid h-7 w-7 place-items-center rounded-md text-slate-300 hover:bg-slate-800 disabled:opacity-25">
                <ArrowUp className="h-4 w-4" />
              </button>
              <button type="button" aria-label={`Descer ${INFO_PRODUTO_LABELS[k]}`} disabled={i === p.ordemInfo.length - 1} onClick={() => moverInfo(k, 1)} className="grid h-7 w-7 place-items-center rounded-md text-slate-300 hover:bg-slate-800 disabled:opacity-25">
                <ArrowDown className="h-4 w-4" />
              </button>
            </li>
          ))}
        </ol>
      </Secao>

      <Secao titulo="Quais produtos entram" aberta resumo={`${noCatalogo.length} de ${produtos.length}`}>
        <Segmentado rotulo="Mostrar" opcoes={MODOS_SELECAO} rotulos={ROTULOS_MODO} valor={sel.modo} onChange={(modo) => setSel({ modo })} />

        {sel.modo === "categorias" && (
          <div className="space-y-1.5">
            {categorias.length === 0 && <p className="text-xs text-slate-500">Nenhuma categoria cadastrada nos produtos.</p>}
            {categorias.map((c) => (
              <Alternar key={c} rotulo={c} valor={sel.categorias.includes(c)} onChange={() => alternarId("categorias", c)} />
            ))}
            {categorias.length > 0 && sel.categorias.length === 0 && <p className="text-xs text-amber-400">Nenhuma categoria marcada: o catálogo mostra todos os produtos.</p>}
          </div>
        )}

        {sel.modo === "produtos" && (
          <div className="space-y-2">
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" />
              <input
                type="text"
                value={busca}
                placeholder="Buscar por nome ou código"
                aria-label="Buscar produto"
                onChange={(e) => setBusca(e.target.value)}
                className="h-9 w-full rounded-lg border border-slate-700 bg-slate-950 pl-9 pr-3 text-sm text-slate-100 outline-none focus:border-sky-400"
              />
            </div>
            <div className="flex gap-2 text-xs">
              <button type="button" className="text-sky-400 hover:underline" onClick={() => setSel({ produtos: produtos.map((x) => x.id) })}>
                Marcar todas
              </button>
              <button type="button" className="text-slate-400 hover:underline" onClick={() => setSel({ produtos: [] })}>
                Limpar
              </button>
              <span className="ml-auto text-slate-500">{sel.produtos.length} marcada(s)</span>
            </div>
            <ul className="max-h-64 space-y-1 overflow-y-auto pr-1">
              {filtradas.map((x) => (
                <li key={x.id}>
                  <label className="flex cursor-pointer items-center gap-2 rounded-lg border border-slate-800 bg-slate-950/40 px-2 py-1.5">
                    <input type="checkbox" className="h-4 w-4" checked={sel.produtos.includes(x.id)} onChange={() => alternarId("produtos", x.id)} />
                    <span className="min-w-0 flex-1 truncate text-sm text-slate-100">{x.nome}</span>
                    <span className="shrink-0 text-xs text-slate-500">{formatCurrency(Number(x.precoVarejo) || 0)}</span>
                  </label>
                </li>
              ))}
              {filtradas.length === 0 && <li className="py-3 text-center text-xs text-slate-500">Nenhum produto encontrado.</li>}
            </ul>
            {sel.produtos.length === 0 && <p className="text-xs text-amber-400">Nenhuma peça marcada: o catálogo mostra todos os produtos.</p>}
          </div>
        )}
      </Secao>

      <Secao titulo="Ordem dos produtos" resumo={ORDEM_LABELS[sel.ordem]}>
        <label className="block text-xs text-slate-400">
          Ordenar por
          <select
            value={sel.ordem}
            onChange={(e) => setSel({ ordem: e.target.value })}
            className="mt-1 h-9 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 text-sm text-slate-100 outline-none focus:border-sky-400"
          >
            {ORDENS_PRODUTO.map((o) => (
              <option key={o} value={o}>
                {ORDEM_LABELS[o]}
              </option>
            ))}
          </select>
        </label>

        {sel.ordem === "manual" && (
          <div className="space-y-1.5">
            <p className="text-xs text-slate-400">Arraste pela alça ou use as setas para posicionar cada peça. Peças novas entram no fim.</p>
            <ListaOrdenavel ids={listaManual.map((x) => x.id)} onReordenar={(ids) => setSel({ ordemManual: ids })} rotulo="Ordem das peças" className="max-h-72 space-y-1 overflow-y-auto pr-1">
              {listaManual.map((x, i) => (
                <ItemOrdenavel key={x.id} id={x.id} rotuloAlca={`Arrastar ${x.nome}`} className="rounded-lg border border-slate-800 bg-slate-950/50 px-2 py-1.5">
                  {(alca) => (
                    <div className="flex items-center gap-2">
                      {alca}
                      <span className="w-6 shrink-0 text-center font-mono text-xs text-slate-500">{i + 1}</span>
                      <span className="min-w-0 flex-1 truncate text-sm text-slate-100">{x.nome}</span>
                      <button type="button" aria-label={`Subir ${x.nome}`} disabled={i === 0} onClick={() => moverManual(x.id, -1)} className="grid h-7 w-7 place-items-center rounded-md text-slate-300 hover:bg-slate-800 disabled:opacity-25">
                        <ArrowUp className="h-4 w-4" />
                      </button>
                      <button type="button" aria-label={`Descer ${x.nome}`} disabled={i === listaManual.length - 1} onClick={() => moverManual(x.id, 1)} className="grid h-7 w-7 place-items-center rounded-md text-slate-300 hover:bg-slate-800 disabled:opacity-25">
                        <ArrowDown className="h-4 w-4" />
                      </button>
                    </div>
                  )}
                </ItemOrdenavel>
              ))}
            </ListaOrdenavel>
          </div>
        )}
      </Secao>
    </div>
  );
}
