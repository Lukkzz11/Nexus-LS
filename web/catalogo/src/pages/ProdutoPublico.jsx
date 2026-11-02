import CampoQuantidade from "../components/catalog/CampoQuantidade";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import {
  ArrowLeft,
  Check,
  ChevronLeft,
  ChevronRight,
  Minus,
  MessageCircle,
  Plus,
  Share2,
  ShoppingBag,
  Store,
  Building
} from "lucide-react";
import { fetchCompanyPublicInfo, fetchPublicProduct } from "../services/catalogService";
import { DEFAULT_PRODUCT_FALLBACK } from "../services/imageUploadService";
import { fotosDoProduto, precoVarejoEfetivo, sanitizarVitrine } from "../services/vitrineConfig";
import { variaveisCss } from "../services/temaVitrine";
import {
  linkDoProduto,
  linkWhatsAppDoProduto,
  listaDeCoresDoProduto,
  lerPontoDeRetorno,
  lerSacola,
  maximoParaAdicionar,
  montarItemSacola,
  percentualDesconto,
  quantidadeNaSacola,
  resumoDeEstoque,
  rotuloDeEstoque,
  salvarSacola,
  somarNaSacola
} from "../services/produtoCatalogo";
import { getColorSwatchClass } from "../components/catalog/vitrine/cores";
import { formatCurrency } from "../utils/formatters";
import { cn } from "../utils/cn";

/**
 * Página própria de cada peça do catálogo online: /catalogo/:empresa/produto/:produto
 * Dados reais (1 leitura do produto + dados da empresa). Navegar aqui nunca altera o estoque:
 * só "Adicionar à sacola" grava, e na sacola do navegador, não no Firestore.
 */
export default function ProdutoPublico() {
  const { companyId, produtoId } = useParams();
  const tenantId = companyId || "lifesurf";
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();

  const [tipoTabela, setTipoTabela] = useState(searchParams.get("tabela") === "atacado" ? "atacado" : "varejo");
  const [dados, setDados] = useState({ chave: "", info: null, produto: null, erro: "" });
  const chaveAtual = `${tenantId}/${produtoId}`;
  const carregando = dados.chave !== chaveAtual;

  const [fotoIdx, setFotoIdx] = useState(0);
  const [tamanhoEscolhido, setTamanhoEscolhido] = useState(null);
  const [corEscolhida, setCorEscolhida] = useState(null);
  const [quantidade, setQuantidade] = useState(1);
  const [sacola, setSacola] = useState(() => lerSacola(tenantId));
  const [adicionado, setAdicionado] = useState(false);
  const [linkCopiado, setLinkCopiado] = useState(false);
  const toqueInicial = useRef(null);

  // 1. Carrega a empresa e a peça (1 leitura para o produto, sem baixar o catálogo todo)
  useEffect(() => {
    let ativo = true;
    Promise.all([fetchCompanyPublicInfo(tenantId), fetchPublicProduct(tenantId, produtoId)])
      .then(([info, produto]) => ativo && setDados({ chave: chaveAtual, info, produto, erro: "" }))
      .catch((err) => {
        console.error("[ProdutoPublico] Erro ao carregar:", err);
        if (ativo) setDados({ chave: chaveAtual, info: null, produto: null, erro: err.message || "Não foi possível carregar a peça." });
      });
    return () => {
      ativo = false;
    };
  }, [tenantId, produtoId, chaveAtual]);

  const { info, produto } = dados;
  const vitrine = useMemo(() => sanitizarVitrine(info?.vitrine), [info]);
  const variaveis = useMemo(() => variaveisCss(vitrine), [vitrine]);
  const loja = vitrine.loja;
  const nomeLoja = loja.nome || info?.nome || "";
  const whatsapp = loja.whatsapp || info?.whatsapp || "";
  const atacadoMin = vitrine.atacado.minPecas;

  const fotos = useMemo(() => (produto ? fotosDoProduto(produto) : []), [produto]);
  // Segue a loja: com "Vender sem Baixa" ativado (padrão), o catálogo não bloqueia por estoque
  const semBaixa = info?.permitirVendaSemBaixa !== false;
  const estoque = useMemo(() => (produto ? resumoDeEstoque(produto, { semBaixa }) : null), [produto, semBaixa]);
  const cores = useMemo(() => (produto ? listaDeCoresDoProduto(produto) : []), [produto]);

  // Escolhas: o cliente escolhe; antes disso vale o primeiro tamanho com estoque e a primeira cor
  const tamanho = estoque
    ? estoque.tamanhos.find((t) => t.nome === tamanhoEscolhido) || estoque.tamanhos.find((t) => t.disponivel) || estoque.tamanhos[0]
    : null;
  const cor = corEscolhida && cores.includes(corEscolhida) ? corEscolhida : cores[0] || "Padrão";

  // 2. Título, descrição e imagem para compartilhar o link
  useEffect(() => {
    if (!produto) return undefined;
    const tituloAnterior = document.title;
    document.title = `${produto.nome}${nomeLoja ? ` | ${nomeLoja}` : ""}`;
    const metas = [
      ["description", (produto.descricao || `${produto.nome} no catálogo online.`).slice(0, 200)],
      ["og:title", produto.nome],
      ["og:description", (produto.descricao || "").slice(0, 200)],
      ["og:image", fotos[0] || ""],
      ["og:type", "product"]
    ];
    const criadas = metas
      .filter(([, valor]) => valor)
      .map(([nome, valor]) => {
        const el = document.createElement("meta");
        el.setAttribute(nome.startsWith("og:") ? "property" : "name", nome);
        el.setAttribute("content", valor);
        document.head.appendChild(el);
        return el;
      });
    return () => {
      document.title = tituloAnterior;
      criadas.forEach((el) => el.remove());
    };
  }, [produto, nomeLoja, fotos]);

  // 3. Navegação entre fotos pelo teclado
  useEffect(() => {
    if (fotos.length < 2) return undefined;
    const aoTeclar = (e) => {
      if (["INPUT", "TEXTAREA", "SELECT"].includes(e.target?.tagName)) return;
      if (e.key === "ArrowLeft") setFotoIdx((i) => (i - 1 + fotos.length) % fotos.length);
      if (e.key === "ArrowRight") setFotoIdx((i) => (i + 1) % fotos.length);
    };
    window.addEventListener("keydown", aoTeclar);
    return () => window.removeEventListener("keydown", aoTeclar);
  }, [fotos.length]);

  const voltar = () => {
    if (location.state?.deCatalogo) {
      navigate(-1);
      return;
    }
    const ponto = lerPontoDeRetorno(tenantId);
    navigate(`/catalogo/${encodeURIComponent(tenantId)}${ponto?.search || ""}`);
  };

  // Preço conforme a tabela escolhida
  const precoCheio = Number(produto?.precoVarejo) || 0;
  const precoAtacado = Number(produto?.precoAtacado) || 0;
  const precoVarejo = produto ? precoVarejoEfetivo(produto) : 0;
  const precoExibido = tipoTabela === "atacado" ? precoAtacado || precoCheio : precoVarejo;
  const precoRiscado = tipoTabela === "atacado" ? (precoCheio > precoExibido ? precoCheio : 0) : precoVarejo < precoCheio ? precoCheio : 0;
  const desconto = percentualDesconto(precoRiscado, precoExibido);

  const itemAtual = produto && tamanho ? montarItemSacola({ produto, tamanho: tamanho.nome, cor, tipoTabela, quantidade: 1 }) : null;
  const jaNaSacola = itemAtual ? quantidadeNaSacola(sacola, itemAtual) : 0;
  const maximo = tamanho ? maximoParaAdicionar(tamanho, jaNaSacola) : 0;
  const quantidadeValida = Math.max(1, Math.min(quantidade, Math.max(1, maximo)));
  const podeAdicionar = Boolean(itemAtual) && !estoque.esgotado && tamanho?.disponivel && maximo > 0;
  const totalNaSacola = sacola.reduce((acc, it) => acc + it.quantidade, 0);

  const adicionarNaSacola = () => {
    if (!podeAdicionar) return;
    const nova = somarNaSacola(lerSacola(tenantId), { ...itemAtual, quantidade: quantidadeValida });
    salvarSacola(tenantId, nova);
    setSacola(nova);
    setAdicionado(true);
    setQuantidade(1);
  };

  const compartilhar = () => {
    const link = linkDoProduto(tenantId, produto.id, window.location.origin);
    if (navigator.share) {
      navigator.share({ title: produto.nome, text: `${produto.nome}${nomeLoja ? ` | ${nomeLoja}` : ""}`, url: link }).catch(() => {});
      return;
    }
    navigator.clipboard?.writeText(link);
    setLinkCopiado(true);
    setTimeout(() => setLinkCopiado(false), 2500);
  };

  const aoTocar = {
    onTouchStart: (e) => {
      toqueInicial.current = e.touches[0].clientX;
    },
    onTouchEnd: (e) => {
      if (toqueInicial.current === null || fotos.length < 2) return;
      const delta = e.changedTouches[0].clientX - toqueInicial.current;
      toqueInicial.current = null;
      if (Math.abs(delta) < 40) return;
      setFotoIdx((i) => (delta < 0 ? (i + 1) % fotos.length : (i - 1 + fotos.length) % fotos.length));
    }
  };

  // A sacola do catálogo abre na tabela em uso (varejo e atacado são pedidos separados)
  const linkDaSacola = `/catalogo/${encodeURIComponent(tenantId)}?sacola=${tipoTabela}${tipoTabela === "atacado" ? "&tabela=atacado" : ""}`;

  const raiz = (conteudo) => (
    <div
      className={cn("vt-bg min-h-screen flex flex-col font-sans", !vitrine.estilo.vidro && "vt-flat")}
      style={variaveis}
      data-tema={vitrine.tema.modelo}
      data-fonte={vitrine.tema.tipografia.fonte === "sistema" ? undefined : "custom"}
    >
      <header className="vt-header sticky top-0 z-40 vt-glass-strong border-x-0 border-t-0 rounded-none">
        <div className="mx-auto flex min-h-14 max-w-6xl items-center justify-between gap-3 px-4 py-2">
          <Link to={`/catalogo/${encodeURIComponent(tenantId)}`} className="flex items-center gap-2.5 min-w-0" aria-label={`Ir para o catálogo de ${nomeLoja}`}>
            {loja.mostrarLogo && loja.logoUrl && (
              <img
                src={loja.logoUrl}
                alt=""
                style={{ height: Math.min(loja.logoAltura || 36, 40) }}
                className="w-auto max-w-[160px] object-contain"
              />
            )}
            {(loja.mostrarNome || !loja.logoUrl || !loja.mostrarLogo) && nomeLoja && (
              <span className="vt-store truncate text-sm font-bold tracking-tight">{nomeLoja}</span>
            )}
          </Link>
          <Link
            to={linkDaSacola}
            className="vt-btn flex items-center gap-2 rounded-full px-3.5 py-2 text-xs font-medium"
          >
            <ShoppingBag className="h-4 w-4" />
            <span className="hidden sm:inline">Sacola</span>
            <span className="vt-tag rounded-full px-1.5 text-[11px] font-bold" aria-label={`${totalNaSacola} peças na sacola`}>
              {totalNaSacola}
            </span>
          </Link>
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-5 sm:py-8">{conteudo}</main>
      {(info?.endereco || info?.cidade) && (
        <footer className="vt-sub px-4 py-6 text-center text-xs">
          {[nomeLoja, vitrine.loja.endereco || [info?.endereco, info?.bairro, info?.cidade].filter(Boolean).join(", ")].filter(Boolean).join(" • ")}
        </footer>
      )}
    </div>
  );

  if (carregando) {
    return raiz(
      <div className="grid gap-8 lg:grid-cols-2" aria-busy="true" role="status" aria-label="Carregando a peça">
        <div className="aspect-[3/4] w-full animate-pulse rounded-2xl" style={{ background: "var(--vt-border)" }} />
        <div className="space-y-4">
          <div className="h-4 w-24 animate-pulse rounded" style={{ background: "var(--vt-border)" }} />
          <div className="h-8 w-3/4 animate-pulse rounded" style={{ background: "var(--vt-border)" }} />
          <div className="h-7 w-32 animate-pulse rounded" style={{ background: "var(--vt-border)" }} />
        </div>
      </div>
    );
  }

  if (dados.erro || !produto) {
    return raiz(
      <div className="mx-auto max-w-md space-y-4 py-16 text-center">
        <h1 className="vt-title text-xl font-bold">{dados.erro ? "Não foi possível abrir esta peça" : "Peça não encontrada"}</h1>
        <p className="vt-sub text-sm">
          {dados.erro || "Ela pode ter saído do catálogo ou o endereço está incorreto."}
        </p>
        <button type="button" onClick={voltar} className="vt-btn inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold">
          <ArrowLeft className="h-4 w-4" />
          Ver o catálogo
        </button>
      </div>
    );
  }

  const fotoAtual = fotos[Math.min(fotoIdx, Math.max(0, fotos.length - 1))] || DEFAULT_PRODUCT_FALLBACK;
  const linkWhats = linkWhatsAppDoProduto({
    whatsapp: loja.mostrarWhatsapp === false ? "" : whatsapp,
    loja: nomeLoja,
    produto,
    tamanho: tamanho?.nome === "Único" ? "" : tamanho?.nome,
    cor: cores.length ? cor : "",
    link: linkDoProduto(tenantId, produto.id, window.location.origin)
  });

  return raiz(
    <article className="space-y-5" data-produto-pagina={produto.id}>
      <nav aria-label="Navegação" className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
        <button type="button" onClick={voltar} className="vt-sub inline-flex items-center gap-1 font-medium hover:underline">
          <ArrowLeft className="h-3.5 w-3.5" />
          Voltar ao catálogo
        </button>
        {produto.categoria && (
          <>
            <span className="vt-pcode" aria-hidden="true">/</span>
            <Link
              to={`/catalogo/${encodeURIComponent(tenantId)}?cat=${encodeURIComponent(produto.categoria)}`}
              className="vt-sub hover:underline"
            >
              {produto.categoria}
            </Link>
          </>
        )}
      </nav>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)] lg:gap-10">
        {/* Galeria */}
        <section aria-label="Fotos da peça" className="lg:sticky lg:top-20 lg:self-start">
          <div className="flex flex-col-reverse gap-3 sm:flex-row">
            {fotos.length > 1 && (
              <ul className="flex gap-2 overflow-x-auto sm:max-h-[34rem] sm:flex-col sm:overflow-y-auto" aria-label="Escolher foto">
                {fotos.map((src, i) => (
                  <li key={src} className="shrink-0">
                    <button
                      type="button"
                      onClick={() => setFotoIdx(i)}
                      aria-label={`Ver foto ${i + 1} de ${fotos.length}`}
                      aria-current={i === fotoIdx}
                      className={cn("block h-20 w-16 overflow-hidden rounded-lg border-2 transition", i === fotoIdx ? "opacity-100" : "opacity-60 hover:opacity-100")}
                      style={{ borderColor: i === fotoIdx ? "var(--vt-accent)" : "var(--vt-border)" }}
                    >
                      <img src={src} alt="" className="h-full w-full object-cover" loading="lazy" />
                    </button>
                  </li>
                ))}
              </ul>
            )}

            <div
              className="relative flex-1 overflow-hidden rounded-2xl border"
              style={{ borderColor: "var(--vt-border)", background: "color-mix(in srgb, var(--vt-card, #fff) 85%, var(--vt-border, #ddd))" }}
              {...aoTocar}
            >
              <img
                key={fotoAtual}
                src={fotoAtual}
                alt={`${produto.nome}${fotos.length > 1 ? ` (foto ${fotoIdx + 1} de ${fotos.length})` : ""}`}
                className="mx-auto block max-h-[70vh] min-h-[22rem] w-full object-contain sm:min-h-[30rem]"
                style={{ aspectRatio: "3 / 4" }}
                onError={(e) => {
                  e.currentTarget.onerror = null;
                  e.currentTarget.src = DEFAULT_PRODUCT_FALLBACK;
                }}
              />
              {fotos.length > 1 && (
                <>
                  <button
                    type="button"
                    aria-label="Foto anterior"
                    onClick={() => setFotoIdx((i) => (i - 1 + fotos.length) % fotos.length)}
                    className="vt-glass-strong absolute left-2 top-1/2 flex h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full"
                  >
                    <ChevronLeft className="h-5 w-5" />
                  </button>
                  <button
                    type="button"
                    aria-label="Próxima foto"
                    onClick={() => setFotoIdx((i) => (i + 1) % fotos.length)}
                    className="vt-glass-strong absolute right-2 top-1/2 flex h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full"
                  >
                    <ChevronRight className="h-5 w-5" />
                  </button>
                  <span className="vt-glass absolute bottom-3 left-1/2 -translate-x-1/2 rounded-full px-2.5 py-0.5 text-[11px] font-medium" aria-live="polite">
                    {fotoIdx + 1} / {fotos.length}
                  </span>
                </>
              )}
              {desconto > 0 && (
                <span className="vt-tag absolute left-3 top-3 rounded-full px-2.5 py-0.5 text-xs font-bold">-{desconto}%</span>
              )}
            </div>
          </div>
        </section>

        {/* Informações e compra */}
        <section className="space-y-5" aria-label="Informações da peça">
          <header className="space-y-1.5">
            {produto.categoria && <p className="vt-pcode text-xs uppercase tracking-wider">{produto.categoria}</p>}
            <h1 className="vt-title text-2xl font-bold leading-tight sm:text-3xl">{produto.nome}</h1>
            {produto.referencia && <p className="vt-pcode text-xs">Ref. {produto.referencia}</p>}
          </header>

          {atacadoMin > 0 && precoAtacado > 0 && (
            <div className="inline-flex items-center rounded-full border p-1 text-xs font-medium" style={{ borderColor: "var(--vt-border)" }} role="group" aria-label="Tabela de preços">
              {[
                ["varejo", "Varejo", Store],
                ["atacado", "Atacado", Building]
              ].map(([id, rotulo, Icone]) => (
                <button
                  key={id}
                  type="button"
                  aria-pressed={tipoTabela === id}
                  onClick={() => {
                    setTipoTabela(id);
                    setQuantidade(1);
                    setAdicionado(false);
                  }}
                  className={cn("flex items-center gap-1.5 rounded-full px-3.5 py-1", tipoTabela === id ? "vt-btn font-semibold" : "opacity-70 hover:opacity-100")}
                >
                  <Icone className="h-3.5 w-3.5" />
                  {rotulo}
                </button>
              ))}
            </div>
          )}

          <div>
            <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <strong className={cn("vt-price text-3xl font-bold tracking-tight", precoRiscado > 0 && tipoTabela === "varejo" && "vt-promo")}>
                {formatCurrency(precoExibido)}
              </strong>
              {precoRiscado > 0 && <span className="vt-pcode text-base line-through">{formatCurrency(precoRiscado)}</span>}
              {desconto > 0 && <span className="vt-tag rounded-full px-2 py-0.5 text-xs font-bold">-{desconto}%</span>}
            </div>
            <p className="vt-sub mt-1 text-xs">
              {tipoTabela === "atacado"
                ? `Preço de atacado, em pedido separado a partir de ${atacadoMin} peças.`
                : precoAtacado > 0 && atacadoMin > 0
                  ? `Atacado a partir de ${atacadoMin} peças: ${formatCurrency(precoAtacado)} cada.`
                  : "Preço de varejo."}
            </p>
          </div>

          {produto.descricao && (
            <div>
              <h2 className="vt-title mb-1 text-sm font-semibold">Descrição</h2>
              <p className="vt-pdesc whitespace-pre-line text-sm leading-relaxed">{produto.descricao}</p>
            </div>
          )}

          {/* Tamanhos com a disponibilidade real do estoque */}
          <div>
            <div className="mb-2 flex items-baseline justify-between gap-2">
              <h2 className="vt-title text-sm font-semibold">Tamanho</h2>
              {tamanho && <span className={cn("text-xs", tamanho.disponivel ? "vt-sub" : "font-semibold")} style={tamanho.disponivel ? undefined : { color: "var(--vt-promo)" }}>{rotuloDeEstoque(tamanho.estoque, semBaixa)}</span>}
            </div>
            <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Tamanho">
              {estoque.tamanhos.map((t) => {
                const escolhido = tamanho?.nome === t.nome;
                return (
                  <button
                    key={t.nome}
                    type="button"
                    role="radio"
                    aria-checked={escolhido}
                    aria-disabled={!t.disponivel}
                    title={rotuloDeEstoque(t.estoque, semBaixa)}
                    onClick={() => {
                      setTamanhoEscolhido(t.nome);
                      setQuantidade(1);
                      setAdicionado(false);
                    }}
                    className={cn(
                      "relative min-w-12 rounded-lg border px-3.5 py-2.5 text-sm font-semibold transition",
                      escolhido && t.disponivel && "vt-btn",
                      !t.disponivel && "cursor-not-allowed overflow-hidden opacity-55"
                    )}
                    style={escolhido && t.disponivel ? undefined : { borderColor: escolhido ? "var(--vt-pname)" : "var(--vt-border)", background: "var(--vt-card)", color: "var(--vt-pname)" }}
                  >
                    <span className={cn(!t.disponivel && "line-through")}>{t.nome}</span>
                  </button>
                );
              })}
            </div>
            {estoque.tamanhos.some((t) => !t.disponivel) && (
              <p className="vt-sub mt-1.5 text-xs">Tamanhos riscados estão indisponíveis no momento.</p>
            )}
          </div>

          {cores.length > 0 && (
            <div>
              <div className="mb-2 flex items-baseline justify-between gap-2">
                <h2 className="vt-title text-sm font-semibold">Cor</h2>
                <span className="vt-sub text-xs">{cor}</span>
              </div>
              <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Cor">
                {cores.map((c) => (
                  <button
                    key={c}
                    type="button"
                    role="radio"
                    aria-checked={cor === c}
                    onClick={() => {
                      setCorEscolhida(c);
                      setAdicionado(false);
                    }}
                    className="flex items-center gap-2 rounded-lg border px-3 py-2 text-xs font-medium transition"
                    style={{
                      borderColor: cor === c ? "var(--vt-pname)" : "var(--vt-border)",
                      borderWidth: cor === c ? 2 : 1,
                      background: "var(--vt-card)",
                      color: "var(--vt-pname)"
                    }}
                  >
                    <span className={cn("h-3.5 w-3.5 shrink-0 rounded-full border", getColorSwatchClass(c))} />
                    {c}
                  </button>
                ))}
              </div>
              <p className="vt-sub mt-1.5 text-xs">A disponibilidade é informada por tamanho. Confirme a cor com a loja se precisar.</p>
            </div>
          )}

          {estoque.esgotado ? (
            <p className="rounded-lg border px-4 py-3 text-sm font-semibold" style={{ borderColor: "var(--vt-promo)", color: "var(--vt-promo)" }} role="status">
              Peça indisponível no momento.
            </p>
          ) : (
            <div className="flex flex-wrap items-center gap-3">
              <div className="flex items-center rounded-lg border" style={{ borderColor: "var(--vt-border)", background: "var(--vt-card)" }}>
                <button
                  type="button"
                  aria-label="Diminuir quantidade"
                  onClick={() => setQuantidade(Math.max(1, quantidadeValida - 1))}
                  disabled={quantidadeValida <= 1}
                  className="flex h-11 w-11 items-center justify-center disabled:opacity-40"
                >
                  <Minus className="h-4 w-4" />
                </button>
                <CampoQuantidade
                  value={quantidadeValida}
                  onChange={setQuantidade}
                  max={Math.max(1, maximo)}
                  ariaLabel="Quantidade (clique e digite)"
                  className="w-14 bg-transparent text-center text-sm font-bold outline-none"
                />
                <button
                  type="button"
                  aria-label="Aumentar quantidade"
                  onClick={() => setQuantidade(Math.min(maximo, quantidadeValida + 1))}
                  disabled={quantidadeValida >= maximo}
                  className="flex h-11 w-11 items-center justify-center disabled:opacity-40"
                >
                  <Plus className="h-4 w-4" />
                </button>
              </div>
              <button
                type="button"
                onClick={adicionarNaSacola}
                disabled={!podeAdicionar}
                className="vt-btn flex h-11 flex-1 items-center justify-center gap-2 rounded-lg px-5 text-sm font-semibold disabled:cursor-not-allowed disabled:opacity-50"
                style={{ minWidth: "11rem" }}
              >
                <ShoppingBag className="h-4 w-4" />
                {podeAdicionar ? `Adicionar à sacola • ${formatCurrency(precoExibido * quantidadeValida)}` : "Tamanho indisponível"}
              </button>
            </div>
          )}
          {jaNaSacola > 0 && tamanho?.estoque !== null && maximo === 0 && !estoque.esgotado && (
            <p className="vt-sub text-xs">Você já colocou todo o estoque deste tamanho na sacola.</p>
          )}

          {adicionado && (
            <div className="vt-glass flex flex-wrap items-center justify-between gap-3 rounded-xl px-4 py-3 text-sm" role="status">
              <span className="flex items-center gap-2 font-medium">
                <Check className="h-4 w-4 text-emerald-600" />
                Adicionado à sacola
              </span>
              <span className="flex gap-2">
                <button type="button" onClick={voltar} className="rounded-lg border px-3 py-1.5 text-xs font-semibold" style={{ borderColor: "var(--vt-border)" }}>
                  Continuar comprando
                </button>
                <Link to={linkDaSacola} className="vt-btn rounded-lg px-3 py-1.5 text-xs font-semibold">
                  Ver sacola
                </Link>
              </span>
            </div>
          )}

          <div className="flex flex-wrap gap-2 border-t pt-4" style={{ borderColor: "var(--vt-divider)" }}>
            {linkWhats && (
              <a
                href={linkWhats}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm font-medium hover:opacity-80"
                style={{ borderColor: "var(--vt-border)" }}
              >
                <MessageCircle className="h-4 w-4 text-emerald-600" />
                Falar sobre esta peça
              </a>
            )}
            <button
              type="button"
              onClick={compartilhar}
              className="inline-flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm font-medium hover:opacity-80"
              style={{ borderColor: "var(--vt-border)" }}
            >
              {linkCopiado ? <Check className="h-4 w-4 text-emerald-600" /> : <Share2 className="h-4 w-4" />}
              {linkCopiado ? "Link copiado" : "Compartilhar"}
            </button>
          </div>
        </section>
      </div>
    </article>
  );
}

