import { useMemo } from "react";
import { cn } from "../../../utils/cn";
import ProductCardGlass from "../vitrine/ProductCardGlass";
import {
  contatoDaLoja,
  estiloEnquadramento,
  linhaDeContatos,
  medidasPdf,
  totalPaginasPdf
} from "../../../services/vitrineConfig";
import { corEfetiva, textoLegivelSobre, variaveisCss } from "../../../services/temaVitrine";

const mm = (n) => `${n}mm`;

/** Logotipo da loja (o enviado no editor ou o padrão do sistema, invertido quando o fundo é claro). */
function Logo({ vitrine, fundoHex, altura }) {
  const l = vitrine.loja;
  if (!l.mostrarLogo) return null;
  const escuro = textoLegivelSobre(fundoHex) === "#ffffff";
  return (
    <img
      src={l.logoUrl || "/assets/logo-white.png"}
      alt=""
      style={{ height: altura, width: "auto", maxWidth: "60mm", objectFit: "contain", filter: l.logoUrl || escuro ? undefined : "invert(1)" }}
      onError={(e) => {
        e.currentTarget.style.display = "none";
      }}
    />
  );
}

function Capa({ vitrine, empresa, medidas, produtos, totalProdutos }) {
  const c = contatoDaLoja(vitrine, empresa);
  const slide = vitrine.banners.find((b) => b.image);
  const contatos = linhaDeContatos(vitrine, empresa);
  const fundo = corEfetiva(vitrine.tema, "fundo");
  return (
    <div className="flex h-full flex-col" style={{ padding: mm(medidas.margem) }}>
      {slide && (
        <div className="relative w-full overflow-hidden" style={{ height: mm(medidas.altura * 0.34), borderRadius: "var(--vt-radius)" }}>
          <img src={slide.image} alt="" className="absolute inset-0 h-full w-full" style={estiloEnquadramento(slide)} />
        </div>
      )}
      <div className={cn("flex flex-1 flex-col justify-center gap-4", vitrine.loja.alinhamento === "centro" ? "items-center text-center" : "items-start text-left")}>
        <Logo vitrine={vitrine} fundoHex={fundo} altura={Math.max(vitrine.loja.logoAltura, 48)} />
        <h1 className="vt-store" style={{ fontSize: "2.4rem", lineHeight: 1.1, fontWeight: "var(--vt-title-weight)" }}>
          {c.nome}
        </h1>
        {vitrine.textos.titulo && <p className="vt-title" style={{ fontSize: "1.25rem" }}>{vitrine.textos.titulo}</p>}
        {c.apresentacao && <p className="vt-sub max-w-[120mm]" style={{ fontSize: "0.95rem", lineHeight: 1.5 }}>{c.apresentacao}</p>}
        <p className="vt-sub" style={{ fontSize: "0.8rem" }}>
          {totalProdutos} {totalProdutos === 1 ? "produto" : "produtos"} • {produtos.length ? "preços " + (vitrine.pdf.tabela === "atacado" ? "de atacado" : "de varejo") : ""}
        </p>
      </div>
      {contatos && <p className="vt-sub text-center" style={{ fontSize: "0.75rem" }}>{contatos}</p>}
    </div>
  );
}

function FaixaTopo({ vitrine, empresa, altura }) {
  const c = contatoDaLoja(vitrine, empresa);
  const fundo = corEfetiva(vitrine.tema, "fundo");
  return (
    <div className="vt-divider flex items-center justify-between border-b" style={{ height: mm(altura), paddingBottom: "1mm" }}>
      <div className="flex items-center gap-2">
        <Logo vitrine={vitrine} fundoHex={fundo} altura={26} />
        {(vitrine.loja.mostrarNome || !vitrine.loja.mostrarLogo) && <span className="vt-store text-xs font-bold uppercase">{c.nome}</span>}
      </div>
      <span className="vt-sub text-[10px]">Catálogo de produtos</span>
    </div>
  );
}

function FaixaRodape({ vitrine, empresa, altura, pagina, total }) {
  const contatos = linhaDeContatos(vitrine, empresa);
  return (
    <div className="vt-divider flex items-end justify-between gap-3 border-t text-[9px]" style={{ height: mm(altura), paddingTop: "1mm" }}>
      <span className="vt-sub min-w-0 flex-1 truncate">{vitrine.rodape.texto || contatos}</span>
      {vitrine.pdf.numeracao && (
        <span className="vt-sub shrink-0">
          Página {pagina} de {total}
        </span>
      )}
    </div>
  );
}

/**
 * Uma página do catálogo em PDF (medidas reais em milímetros). Usa as mesmas variáveis `--vt-*` e o mesmo
 * cartão de produto da vitrine online, por isso cores, fontes e preços saem iguais aos da tela.
 */
export function PaginaPdf({ vitrine, produtos, empresa, indice, atacadoMin }) {
  const { pdf } = vitrine;
  const medidas = useMemo(() => medidasPdf(pdf, vitrine), [pdf, vitrine]);
  const total = totalPaginasPdf(pdf, produtos.length);
  const ehCapa = pdf.capa && indice === 0;
  const indiceProdutos = indice - (pdf.capa ? 1 : 0);
  const itens = ehCapa ? [] : produtos.slice(indiceProdutos * medidas.porPagina, (indiceProdutos + 1) * medidas.porPagina);

  return (
    <section
      className="pdf-page vt-pdf-pagina"
      data-pagina={indice + 1}
      style={{ width: mm(medidas.largura), height: mm(medidas.altura) }}
    >
      {ehCapa ? (
        <Capa vitrine={vitrine} empresa={empresa} medidas={medidas} produtos={produtos} totalProdutos={produtos.length} />
      ) : (
        <div className="flex h-full flex-col" style={{ padding: mm(medidas.margem) }}>
          {pdf.cabecalhoPagina && <FaixaTopo vitrine={vitrine} empresa={empresa} altura={medidas.cabecalho} />}
          <div
            className="vt-pdf-grade"
            style={{
              flex: 1,
              display: "grid",
              gridTemplateColumns: `repeat(${pdf.colunas}, minmax(0, 1fr))`,
              gridTemplateRows: `repeat(${pdf.linhas}, ${mm(medidas.celulaA)})`,
              gap: mm(medidas.gap),
              marginTop: pdf.cabecalhoPagina ? mm(medidas.gap) : 0,
              "--vt-aspect": String(medidas.proporcaoFoto.toFixed(3))
            }}
          >
            {itens.length === 0 && <p className="vt-sub col-span-full text-center text-sm">Nenhum produto selecionado para o catálogo.</p>}
            {itens.map((p) => (
              <div key={p.id} className="vt-pdf-celula min-h-0 overflow-hidden" style={{ height: mm(medidas.celulaA) }}>
                <ProductCardGlass produto={p} tipoTabela={pdf.tabela} atacadoMin={atacadoMin} cfg={vitrine.produtos} estatico className="h-full" />
              </div>
            ))}
          </div>
          {pdf.rodapePagina && <FaixaRodape vitrine={vitrine} empresa={empresa} altura={medidas.rodape} pagina={indice + 1} total={total} />}
        </div>
      )}
    </section>
  );
}

/** Raiz com as variáveis do tema: tudo que estiver dentro usa as cores e fontes escolhidas. */
export function RaizTema({ vitrine, children, className, id }) {
  const variaveis = useMemo(() => variaveisCss(vitrine), [vitrine]);
  return (
    <div id={id} className={cn("vt-bg vt-pdf-raiz", !vitrine.estilo.vidro && "vt-flat", className)} style={variaveis} data-fonte={vitrine.tema.tipografia.fonte === "sistema" ? undefined : "custom"}>
      {children}
    </div>
  );
}
