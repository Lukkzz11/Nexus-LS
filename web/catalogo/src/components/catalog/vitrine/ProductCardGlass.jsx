import { useState } from "react";
import { Plus } from "lucide-react";
import { cn } from "../../../utils/cn";
import { formatCurrency } from "../../../utils/formatters";
import { DEFAULT_PRODUCT_FALLBACK } from "../../../services/imageUploadService";
import { criarPersonalizacaoPadrao, fotosDoProduto, precosDoCartao } from "../../../services/vitrineConfig";
import { getColorSwatchClass, listaDeCores } from "./cores";

const CFG_PADRAO = criarPersonalizacaoPadrao().produtos;

/**
 * Cartão de produto da vitrine: ao passar o mouse a foto muda para a próxima; nos celulares os pontinhos
 * trocam a foto. O que aparece, a ordem, o alinhamento e as cores vêm da configuração do Editor visual (`cfg`).
 */
export default function ProductCardGlass({ produto, tipoTabela, atacadoMin, onAbrir, cfg = CFG_PADRAO, className, estatico = false }) {
  const fotos = fotosDoProduto(produto);
  const [hover, setHover] = useState(false);
  const [escolhida, setEscolhida] = useState(null);

  const mostrar = escolhida ?? (hover && fotos.length > 1 ? 1 : 0);
  const preco = precosDoCartao(produto, tipoTabela);
  const tamanhos = produto.gradeTamanhos ? Object.keys(produto.gradeTamanhos) : ["P", "M", "G", "GG"];
  const cores = listaDeCores(produto);
  const centro = cfg.alinhamento === "centro";
  const m = cfg.mostrar;
  const precoNaFoto = cfg.precoPosicao === "foto" && m.preco;
  // Modo estático (catálogo em PDF): sem botão, sem pontinhos e sem cliques
  const abrir = estatico ? undefined : () => onAbrir(produto);

  const blocoPreco = (
    <div>
      <div className={cn("flex items-baseline gap-1.5", centro && "justify-center")}>
        <strong className={cn("vt-price tracking-tight", preco.promo && !preco.atacado && cfg.destacarPromocao && "vt-promo")}>
          {formatCurrency(preco.final)}
        </strong>
        {preco.riscado > 0 && <span className="vt-pcode text-xs line-through">{formatCurrency(preco.riscado)}</span>}
      </div>
      {preco.atacado ? (
        <span className="vt-sub mt-0.5 block text-[10px]">Pedido de atacado: a partir de {atacadoMin} peças</span>
      ) : (
        preco.comAtacado && (
          <span className="vt-sub mt-0.5 block text-[10px]">
            Atacado (a partir de {atacadoMin} peças): <strong className="vt-price" style={{ fontSize: "inherit" }}>{formatCurrency(preco.atacadoValor)}</strong>
          </span>
        )
      )}
    </div>
  );

  const blocos = {
    categoria: (m.categoria || m.codigo) && (
      <div key="categoria" className={cn("vt-pcode flex items-center gap-2 text-[10px] uppercase tracking-wider", centro ? "justify-center" : "justify-between")}>
        {m.categoria && <span>{produto.categoria || "Moda"}</span>}
        {m.codigo && produto.referencia && <span className="font-mono">Ref: {produto.referencia}</span>}
      </div>
    ),
    nome: m.nome && (
      <h3 key="nome" onClick={abrir} className={cn("vt-pname line-clamp-2 leading-snug", !estatico && "cursor-pointer")}>
        {produto.nome}
      </h3>
    ),
    descricao: m.descricao && produto.descricao && (
      <p key="descricao" className="vt-pdesc line-clamp-2 leading-snug">
        {produto.descricao}
      </p>
    ),
    cores: m.cores && cores.length > 0 && (
      <div key="cores" className={cn("flex items-center gap-1", centro && "justify-center")}>
        {cores.slice(0, 4).map((c, i) => (
          <span key={i} className={cn("h-2.5 w-2.5 rounded-full border", getColorSwatchClass(c))} title={c} />
        ))}
        {cores.length > 4 && <span className="vt-pcode font-mono text-[9px]">+{cores.length - 4}</span>}
      </div>
    ),
    tamanhos: m.tamanhos && (
      <div key="tamanhos" className={cn("flex items-center gap-1 overflow-x-auto", centro && "justify-center")}>
        <span className="vt-pcode mr-1 text-[10px]">Tam:</span>
        {tamanhos.slice(0, 5).map((tam) => (
          <span key={tam} className="vt-pdesc rounded border px-1.5 py-0.5 font-mono text-[10px] font-medium" style={{ borderColor: "var(--vt-divider)" }}>
            {tam}
          </span>
        ))}
      </div>
    ),
    preco: m.preco && !precoNaFoto && <div key="preco">{blocoPreco}</div>
  };

  return (
    <article className={cn("vt-produto group flex flex-col overflow-hidden transition-shadow duration-300 hover:shadow-xl", className)} data-produto={produto.id}>
      {m.foto && (
        <div
          className={cn("relative w-full overflow-hidden", !estatico && "cursor-pointer")}
          style={{ aspectRatio: "var(--vt-aspect, 3/4)", background: "color-mix(in srgb, var(--vt-card, #fff) 85%, var(--vt-border, #ddd))" }}
          onClick={abrir}
          onMouseEnter={() => setHover(true)}
          onMouseLeave={() => {
            setHover(false);
            setEscolhida(null);
          }}
        >
          {(fotos.length ? fotos : [DEFAULT_PRODUCT_FALLBACK]).map((src, i) => (
            <img
              key={src}
              src={src}
              alt={i === 0 ? produto.nome : ""}
              loading={estatico ? "eager" : "lazy"}
              draggable={false}
              className={cn(
                "absolute inset-0 h-full w-full object-cover transition-all duration-500 ease-out",
                i === mostrar ? "opacity-100" : "opacity-0",
                i === mostrar && "group-hover:scale-105"
              )}
              onError={(e) => {
                e.currentTarget.onerror = null;
                e.currentTarget.src = DEFAULT_PRODUCT_FALLBACK;
              }}
            />
          ))}

          <div className="absolute left-2.5 top-2.5 z-10 flex flex-col items-start gap-1">
            {produto.destaque && <span className="vt-tag rounded-full px-2.5 py-0.5 text-[10px] font-bold uppercase tracking-tight">Novo</span>}
            {preco.descontoPromo > 0 && cfg.destacarPromocao && (
              <span className="vt-tag rounded-full px-2.5 py-0.5 text-[10px] font-bold">Promoção -{preco.descontoPromo}%</span>
            )}
            {preco.atacado && preco.descontoAtacado > 0 && (
              <span className="vt-tag rounded-full px-2.5 py-0.5 text-[10px] font-bold">-{preco.descontoAtacado}% atacado</span>
            )}
          </div>

          {precoNaFoto && (
            <div className="vt-glass-strong absolute bottom-2.5 right-2.5 z-10 rounded-xl px-2.5 py-1 text-right">{blocoPreco}</div>
          )}

          {fotos.length > 1 && !precoNaFoto && !estatico && (
            <div className="vt-glass absolute bottom-2.5 left-1/2 z-10 flex -translate-x-1/2 items-center gap-1.5 rounded-full px-2 py-1.5">
              {fotos.map((_, i) => (
                <button
                  key={i}
                  type="button"
                  aria-label={`Ver foto ${i + 1} de ${produto.nome}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setEscolhida(i);
                  }}
                  className={cn("h-1.5 rounded-full transition-all", i === mostrar ? "vt-accent w-4" : "w-1.5")}
                  style={i === mostrar ? undefined : { background: "var(--vt-pcode)" }}
                />
              ))}
            </div>
          )}
        </div>
      )}

      <div className="flex flex-1 flex-col justify-between gap-3" style={{ padding: "calc(var(--vt-padding, 16px) * 0.85)" }}>
        <div className="space-y-1.5">{cfg.ordemInfo.map((k) => blocos[k]).filter(Boolean)}</div>

        {m.botao && !estatico && (
          <button
            type="button"
            onClick={() => onAbrir(produto)}
            className="vt-btn flex h-9 w-full items-center justify-center gap-1.5 text-xs font-semibold transition"
            style={{ borderRadius: "calc(var(--vt-radius, 16px) * 0.6)" }}
          >
            <Plus className="h-3.5 w-3.5" />
            Comprar
          </button>
        )}
      </div>
    </article>
  );
}
